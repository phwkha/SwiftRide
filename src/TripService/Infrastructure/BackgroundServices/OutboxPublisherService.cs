using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SwiftRide.Contracts.Events;
using SwiftRide.Messaging;
using TripService.Domain.Entities;
using TripService.Infrastructure.Data;

namespace TripService.Infrastructure.BackgroundServices;

/// <summary>
/// Background service poll OutboxMessages chua xu ly va publish len Kafka.
/// Dam bao Transactional Outbox pattern: du lieu va event duoc ghi cung transaction,
/// worker sau do publish event ra broker.
/// </summary>
public class OutboxPublisherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxPublisherService> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(1);
    private const int BatchSize = 20;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public OutboxPublisherService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxPublisherService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxPublisherService started.");

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

        _logger.LogInformation("OutboxPublisherService stopped.");
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(stoppingToken);

        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await dbContext.Database.ExecuteSqlRawAsync("""
            EXEC @result = sp_getapplock
                @Resource = N'SwiftRide.TripOutbox',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 0;
            """, [result], stoppingToken);

        var lockResult = (int)result.Value;
        if (lockResult == -1)
            return;
        if (lockResult < 0)
            throw new InvalidOperationException($"SQL Server could not lock the trip outbox (result {lockResult}).");

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

    private async Task PublishMessageAsync(IServiceProvider services, OutboxMessage message, CancellationToken cancellationToken)
    {
        Task<TEvent> Produce<TEvent>(Func<TEvent, Guid> getTripId) where TEvent : class
            => services.PublishOutboxEvent(message.Payload, JsonOptions, getTripId, message.Id, cancellationToken);

        switch (message.EventType)
        {
            case nameof(TripRequestedEvent):
                var requested = await Produce<TripRequestedEvent>(e => e.TripId);
                _logger.LogInformation("Published matching request for trip {TripId} from outbox {MessageId} after {DelayMs} ms.",
                    requested.TripId, message.Id, (DateTimeOffset.UtcNow - message.CreatedAt).TotalMilliseconds);
                break;
            case nameof(RetryMatchingCommand):
                var retry = await Produce<RetryMatchingCommand>(e => e.TripId);
                _logger.LogInformation("Published matching retry {Attempt} for trip {TripId} from outbox {MessageId}.",
                    retry.Attempt, retry.TripId, message.Id);
                break;
            case nameof(TripAcceptedEvent):
                await Produce<TripAcceptedEvent>(e => e.TripId);
                break;
            case nameof(TripOfferDispatchedEvent):
                await Produce<TripOfferDispatchedEvent>(e => e.TripId);
                break;
            case nameof(TripOfferExpiredEvent):
                await Produce<TripOfferExpiredEvent>(e => e.TripId);
                break;
            case nameof(MatchingAttemptClosedEvent):
                await Produce<MatchingAttemptClosedEvent>(e => e.TripId);
                break;
            case nameof(DriverRejectedOfferEvent):
                await Produce<DriverRejectedOfferEvent>(e => e.TripId);
                break;
            case nameof(TripDroppedOffEvent):
                await Produce<TripDroppedOffEvent>(e => e.TripId);
                break;
            case nameof(TripCancelledEvent):
                await Produce<TripCancelledEvent>(e => e.TripId);
                break;
            case nameof(TripCompletedEvent):
                await Produce<TripCompletedEvent>(e => e.TripId);
                break;
            case nameof(TripStatusChangedEvent):
                await Produce<TripStatusChangedEvent>(e => e.TripId);
                break;
            default:
                throw new InvalidOperationException($"Unsupported outbox event type '{message.EventType}'.");
        }
    }
}
