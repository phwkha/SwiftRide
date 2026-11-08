using MatchingService.Application.Services;
using MassTransit;
using SwiftRide.Contracts.Events;

namespace MatchingService.Application.Consumers;

public sealed class TripRequestedConsumer(DriverMatchingAppService matchingService) : IConsumer<TripRequestedEvent>
{
    public Task Consume(ConsumeContext<TripRequestedEvent> context)
    {
        var message = context.Message;
        return matchingService.MatchTripAsync(
            message.TripId,
            message.PickupLatitude,
            message.PickupLongitude,
            message.PickupAddress,
            message.DropoffLatitude,
            message.DropoffLongitude,
            message.DropoffAddress,
            message.VehicleType,
            message.DistanceKm,
            message.EstimatedDurationMinutes,
            message.EstimatedFare,
            message.ExcludedDriverIds,
            context.CancellationToken);
    }
}
