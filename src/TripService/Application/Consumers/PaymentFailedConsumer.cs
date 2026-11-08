using MassTransit;
using TripService.Domain.Enums;
using TripService.Domain.Interfaces;
using TripService.Domain.States;
using SwiftRide.Contracts.Events;

namespace TripService.Application.Consumers;

public sealed class PaymentFailedConsumer : IConsumer<PaymentFailedEvent>
{
    private readonly ITripReader _tripReader;
    private readonly ITripWriter _tripWriter;
    private readonly ILogger<PaymentFailedConsumer> _logger;

    public PaymentFailedConsumer(
        ITripReader tripReader,
        ITripWriter tripWriter,
        ILogger<PaymentFailedConsumer> logger)
    {
        _tripReader = tripReader;
        _tripWriter = tripWriter;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentFailedEvent> context)
    {
        var trip = await _tripReader.GetByIdAsync(context.Message.TripId, context.CancellationToken);
        if (trip is null)
        {
            _logger.LogWarning("Ignoring payment failure for missing trip {TripId}.", context.Message.TripId);
            return;
        }

        if (trip.Status != TripStatus.DroppedOff)
        {
            _logger.LogInformation(
                "Ignoring duplicate or stale payment failure for trip {TripId} in status {Status}.",
                trip.Id,
                trip.Status);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var previousStatus = trip.Status;
        trip.Status = TripState.FromStatus(trip.Status).RequestPayment().Status;
        trip.UpdatedAt = now;

        await _tripWriter.UpdateWithOutboxAsync(
            trip,
            new()
            {
                TripId = trip.Id,
                FromStatus = previousStatus,
                ToStatus = trip.Status,
                Reason = $"{context.Message.Status}: {context.Message.Reason}",
                ChangedAt = now
            },
            ct: context.CancellationToken);
    }
}
