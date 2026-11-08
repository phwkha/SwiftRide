using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using PaymentService.Infrastructure.Data;
using SwiftRide.Contracts.Events;

namespace PaymentService.Infrastructure.BackgroundServices;

/// <summary>
/// Background service poll outbox_messages chua xu ly va publish len RabbitMQ.
/// Event duoc deserialize ve dung record trong SwiftRide.Contracts.Events de MassTransit
/// dinh tuyen toi consumer cua TripService.
/// </summary>
public class PaymentOutboxPublisherService : BackgroundService
{
    private static readonly Dictionary<string, Type> EventTypes = new()
    {
        [nameof(PaymentSettledEvent)] = typeof(PaymentSettledEvent),
        [nameof(PaymentFailedEvent)] = typeof(PaymentFailedEvent),
        [nameof(PaymentRefundedEvent)] = typeof(PaymentRefundedEvent),
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentOutboxPublisherService> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(3);
    private const int BatchSize = 20;

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
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

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
                if (!EventTypes.TryGetValue(message.EventType, out var eventType))
                    throw new InvalidOperationException($"Unknown outbox event type '{message.EventType}'.");

                var @event = JsonSerializer.Deserialize(message.Payload, eventType)!;

                await publishEndpoint.Publish(@event, eventType,
                    context => context.MessageId = message.Id,
                    stoppingToken);

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
    }
}
