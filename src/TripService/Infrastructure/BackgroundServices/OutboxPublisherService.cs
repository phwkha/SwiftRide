using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
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
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 20;

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
                // Deserialize payload va publish len RabbitMQ
                var payload = JsonSerializer.Deserialize<JsonElement>(message.Payload);

                await publishEndpoint.Publish<object>(
                    new { Type = message.EventType, message.Id, Data = payload },
                    context =>
                    {
                        context.MessageId = message.Id;
                        context.SetRoutingKey(message.EventType);
                    },
                    stoppingToken);

                message.ProcessedAt = DateTimeOffset.UtcNow;
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
