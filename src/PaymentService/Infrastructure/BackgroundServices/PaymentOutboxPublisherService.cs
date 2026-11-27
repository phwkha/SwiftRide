using Microsoft.EntityFrameworkCore;
using PaymentService.Domain.Entities;
using PaymentService.Infrastructure.Data;
using SwiftRide.Contracts.Events;
using SwiftRide.Messaging;

namespace PaymentService.Infrastructure.BackgroundServices;

/// <summary>
/// Publishes pending payment outbox messages to Kafka.
/// </summary>
public class PaymentOutboxPublisherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentOutboxPublisherService> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(3);
    private const int BatchSize = 20;
    private const long OutboxLockKey = 0x53574946544F5554;

    public PaymentOutboxPublisherService(
        IServiceScopeFactory scopeFactory,
        ILogger<PaymentOutboxPublisherService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PaymentOutboxPublisherService started.");

        using var timer = new PeriodicTimer(_pollingInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessOutboxMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox messages.");
            }
        }

        _logger.LogInformation("PaymentOutboxPublisherService stopped.");
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(stoppingToken);

        var acquired = await dbContext.Database
            .SqlQuery<bool>($"""SELECT pg_try_advisory_xact_lock({OutboxLockKey}) AS "Value" """)
            .SingleAsync(stoppingToken);
        if (!acquired)
            return;

        var messages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(stoppingToken);

        if (messages.Count == 0) return;

        _logger.LogInformation("Processing {Count} outbox messages.", messages.Count);

        foreach (var message in messages)
        {
            try
            {
                await PublishMessageAsync(scope.ServiceProvider, message, stoppingToken);

                message.ProcessedAt = DateTimeOffset.UtcNow;
                message.Error = null;
                _logger.LogDebug("Published outbox message {Id} of type {Type}.", message.Id, message.EventType);
            }
            catch (Exception ex)
            {
                message.Error = ex.Message;
                _logger.LogWarning(ex, "Failed to publish outbox message {Id}.", message.Id);
            }
        }

        await dbContext.SaveChangesAsync(stoppingToken);
        await transaction.CommitAsync(stoppingToken);
    }

    private static Task PublishMessageAsync(IServiceProvider services, OutboxMessage message, CancellationToken cancellationToken)
    {
        Task Produce<TEvent>(Func<TEvent, Guid> getTripId) where TEvent : class
            => services.PublishOutboxEvent(message.Payload, null, getTripId, message.Id, cancellationToken);

        return message.EventType switch
        {
            nameof(PaymentSettledEvent) => Produce<PaymentSettledEvent>(e => e.TripId),
            nameof(PaymentFailedEvent) => Produce<PaymentFailedEvent>(e => e.TripId),
            nameof(PaymentRefundedEvent) => Produce<PaymentRefundedEvent>(e => e.TripId),
            _ => throw new InvalidOperationException($"Unknown outbox event type '{message.EventType}'.")
        };
    }
}
