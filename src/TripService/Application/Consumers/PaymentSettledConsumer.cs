using System.Text.Json;
using MassTransit;
using TripService.Domain.Enums;
using TripService.Domain.Entities;
using TripService.Domain.Interfaces;
using TripService.Domain.States;
using SwiftRide.Contracts.Events;

namespace TripService.Application.Consumers;

public sealed class PaymentSettledConsumer : IConsumer<PaymentSettledEvent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITripReader _tripReader;
    private readonly ITripWriter _tripWriter;
    private readonly ILogger<PaymentSettledConsumer> _logger;

    public PaymentSettledConsumer(
        ITripReader tripReader,
        ITripWriter tripWriter,
        ILogger<PaymentSettledConsumer> logger)
    {
        _tripReader = tripReader;
        _tripWriter = tripWriter;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentSettledEvent> context)
    {
        var trip = await _tripReader.GetByIdAsync(context.Message.TripId, context.CancellationToken);
        if (trip is null)
        {
            _logger.LogWarning("Ignoring payment settlement for missing trip {TripId}.", context.Message.TripId);
            return;
        }

        if (trip.Status is not (TripStatus.DroppedOff or TripStatus.PaymentPending or TripStatus.Paid))
        {
            _logger.LogInformation(
                "Ignoring duplicate or stale payment settlement for trip {TripId} in status {Status}.",
                trip.Id,
                trip.Status);
            return;
        }

        if (trip.DriverId is null)
        {
            throw new InvalidOperationException($"Trip {trip.Id} cannot be completed without a driver.");
        }

        var now = DateTimeOffset.UtcNow;
        var histories = new List<TripStatusHistory>();
        if (trip.Status is TripStatus.DroppedOff or TripStatus.PaymentPending)
        {
            var previousStatus = trip.Status;
            trip.Status = TripState.FromStatus(trip.Status).ConfirmPayment().Status;
            histories.Add(CreateHistory(trip.Id, previousStatus, trip.Status, "Payment settled", now));
        }

        if (trip.Status == TripStatus.Paid)
        {
            var previousStatus = trip.Status;
            trip.Status = TripState.FromStatus(trip.Status).Complete().Status;
            trip.CompletedAt = now;
            histories.Add(CreateHistory(trip.Id, previousStatus, trip.Status, "Trip completed", now));
        }

        trip.UpdatedAt = now;
        var outbox = new OutboxMessage
        {
            EventType = nameof(TripCompletedEvent),
            Payload = JsonSerializer.Serialize(
                new TripCompletedEvent(
                    trip.Id,
                    trip.RiderId,
                    trip.DriverId.Value,
                    context.Message.TotalAmount,
                    context.Message.TransactionId),
                JsonOptions)
        };

        await _tripWriter.UpdateWithHistoriesAndOutboxAsync(
            trip,
            histories,
            outbox,
            context.CancellationToken);
    }

    private static TripStatusHistory CreateHistory(
        Guid tripId,
        TripStatus fromStatus,
        TripStatus toStatus,
        string reason,
        DateTimeOffset changedAt)
        => new()
        {
            TripId = tripId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Reason = reason,
            ChangedAt = changedAt
        };
}
