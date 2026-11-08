using MassTransit;
using Microsoft.AspNetCore.SignalR;
using SwiftRide.Contracts.Events;

namespace TripService.Api.Hubs;

// Consumer 1: Dispatch trip offer popup to driver (SD 11)
public class TripOfferDispatchedConsumer(IHubContext<TripHub> hubContext)
    : IConsumer<TripOfferDispatchedEvent>
{
    public async Task Consume(ConsumeContext<TripOfferDispatchedEvent> context)
    {
        var e = context.Message;
        var groupName = $"driver_{e.DriverId.ToString().ToLowerInvariant()}";
        await hubContext.Clients.Group(groupName)
            .SendAsync("ReceiveTripOffer", new
            {
                e.TripId,
                e.DriverId,
                e.PickupAddress,
                e.PickupLatitude,
                e.PickupLongitude,
                e.DropoffAddress,
                e.DropoffLatitude,
                e.DropoffLongitude,
                e.EstimatedFare,
                e.DistanceKm,
                e.DurationMinutes,
                e.CountdownSeconds,
                e.ExpiresAt
            });
    }
}

// Consumer 2: Notify driver when offer expired after 15s timeout (SD 11)
public class TripOfferExpiredConsumer(IHubContext<TripHub> hubContext)
    : IConsumer<TripOfferExpiredEvent>
{
    public async Task Consume(ConsumeContext<TripOfferExpiredEvent> context)
    {
        var e = context.Message;
        var groupName = $"driver_{e.DriverId.ToString().ToLowerInvariant()}";
        await hubContext.Clients.Group(groupName)
            .SendAsync("TripOfferExpired", new
            {
                e.TripId,
                e.DriverId,
                e.Message
            });
    }
}

// Consumer 3: Broadcast driver GPS to trip room (SD 10)
public class DriverLocationUpdatedConsumer(IHubContext<TripHub> hubContext)
    : IConsumer<DriverLocationUpdatedEvent>
{
    public async Task Consume(ConsumeContext<DriverLocationUpdatedEvent> context)
    {
        var e = context.Message;
        if (e.TripId is not null)
        {
            var groupName = $"trip_{e.TripId.Value.ToString().ToLowerInvariant()}";
            await hubContext.Clients.Group(groupName)
                .SendAsync("DriverLocationUpdated", new
                {
                    e.DriverId,
                    e.Latitude,
                    e.Longitude,
                    e.Bearing,
                    e.Speed,
                    e.UpdatedAt
                });
        }
    }
}

// Consumer 4: Broadcast PaymentCompleted to trip room (SD 08, SD 12)
public class PaymentSettledSignalRConsumer(IHubContext<TripHub> hubContext)
    : IConsumer<PaymentSettledEvent>
{
    public async Task Consume(ConsumeContext<PaymentSettledEvent> context)
    {
        var e = context.Message;
        var groupName = $"trip_{e.TripId.ToString().ToLowerInvariant()}";
        await hubContext.Clients.Group(groupName)
            .SendAsync("PaymentCompleted", new
            {
                e.TripId,
                e.TransactionId,
                e.TotalAmount,
                e.PaymentMethod,
                NetEarnings = e.DriverCreditAmount,
                Status = "Success",
                CompletedAt = DateTimeOffset.UtcNow
            });
    }
}

// Consumer 5: Broadcast TripStatusChanged on driver accept (SD 05)
public class TripAcceptedSignalRConsumer(IHubContext<TripHub> hubContext)
    : IConsumer<TripAcceptedEvent>
{
    public async Task Consume(ConsumeContext<TripAcceptedEvent> context)
    {
        var e = context.Message;
        var groupName = $"trip_{e.TripId.ToString().ToLowerInvariant()}";
        await hubContext.Clients.Group(groupName)
            .SendAsync("TripStatusChanged", new
            {
                e.TripId,
                NewStatus = "DriverAccepted",
                DriverId = e.DriverId,
                Timestamp = DateTimeOffset.UtcNow
            });
    }
}

// Consumer 6: Broadcast TripStatusChanged on cancel (SD 07)
public class TripCancelledSignalRConsumer(IHubContext<TripHub> hubContext)
    : IConsumer<TripCancelledEvent>
{
    public async Task Consume(ConsumeContext<TripCancelledEvent> context)
    {
        var e = context.Message;
        var groupName = $"trip_{e.TripId.ToString().ToLowerInvariant()}";
        await hubContext.Clients.Group(groupName)
            .SendAsync("TripStatusChanged", new
            {
                e.TripId,
                NewStatus = "Cancelled",
                CancellationReason = e.Reason,
                Timestamp = DateTimeOffset.UtcNow
            });
    }
}

// Consumer 7: Broadcast TripStatusChanged on matching failed (SD 11)
public class MatchingFailedSignalRConsumer(IHubContext<TripHub> hubContext)
    : IConsumer<MatchingFailedEvent>
{
    public async Task Consume(ConsumeContext<MatchingFailedEvent> context)
    {
        var e = context.Message;
        var groupName = $"trip_{e.TripId.ToString().ToLowerInvariant()}";
        await hubContext.Clients.Group(groupName)
            .SendAsync("TripStatusChanged", new
            {
                e.TripId,
                NewStatus = "Failed",
                CancellationReason = e.Reason,
                Timestamp = DateTimeOffset.UtcNow
            });
    }
}
