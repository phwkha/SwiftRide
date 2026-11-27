using System.Text.Json;
using SwiftRide.Contracts.Events;
using TripService.Domain.Entities;

namespace TripService.Application;

internal static class TripStatusOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static OutboxMessage Create(Trip trip, TripStatusHistory history, string? cancellationReason = null)
        => new()
        {
            EventType = nameof(TripStatusChangedEvent),
            Payload = JsonSerializer.Serialize(new TripStatusChangedEvent(
                trip.Id, history.ToStatus.ToString(), trip.DriverId, history.ChangedAt, cancellationReason), JsonOptions)
        };
}
