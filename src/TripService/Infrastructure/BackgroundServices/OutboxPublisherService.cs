using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using SwiftRide.Contracts.Events;
using TripService.Infrastructure.Data;

namespace TripService.Infrastructure.BackgroundServices;

/// <summary>
/// Background service poll OutboxMessages chua xu ly va publish len RabbitMQ.
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
                var (payload, payloadType) = DeserializeEvent(message.EventType, message.Payload);

                await publishEndpoint.Publish(
                    payload,
                    payloadType,
                    context =>
                    {
                        context.MessageId = message.Id;
                        context.SetRoutingKey(message.EventType);
                    },
                    stoppingToken);

                message.ProcessedAt = DateTimeOffset.UtcNow;
                message.Error = null;
                if (payload is TripRequestedEvent requested)
                    _logger.LogInformation("Published matching request for trip {TripId} from outbox {MessageId} after {DelayMs} ms.",
                        requested.TripId, message.Id, (message.ProcessedAt.Value - message.CreatedAt).TotalMilliseconds);
                if (payload is RetryMatchingCommand retry)
                    _logger.LogInformation("Published matching retry {Attempt} for trip {TripId} from outbox {MessageId}.",
                        retry.Attempt, retry.TripId, message.Id);
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

    private static (object Payload, Type PayloadType) DeserializeEvent(
        string eventType,
        string payload)
    {
        var type = eventType switch
        {
            nameof(TripRequestedEvent) => typeof(TripRequestedEvent),
            nameof(TripAcceptedEvent) => typeof(TripAcceptedEvent),
            nameof(TripOfferDispatchedEvent) => typeof(TripOfferDispatchedEvent),
            nameof(TripOfferExpiredEvent) => typeof(TripOfferExpiredEvent),
            nameof(MatchingAttemptClosedEvent) => typeof(MatchingAttemptClosedEvent),
            nameof(DriverRejectedOfferEvent) => typeof(DriverRejectedOfferEvent),
            nameof(RetryMatchingCommand) => typeof(RetryMatchingCommand),
            nameof(TripDroppedOffEvent) => typeof(TripDroppedOffEvent),
            nameof(TripCancelledEvent) => typeof(TripCancelledEvent),
            nameof(TripCompletedEvent) => typeof(TripCompletedEvent),
            nameof(TripStatusChangedEvent) => typeof(TripStatusChangedEvent),
            _ => throw new InvalidOperationException(
                $"Unsupported outbox event type '{eventType}'.")
        };

        var value = JsonSerializer.Deserialize(payload, type, JsonOptions)
            ?? throw new JsonException($"Outbox payload for '{eventType}' was null.");

        return (value, type);
    }
}
