using MatchingService.Application.Services;
using MassTransit;
using SwiftRide.Contracts.Events;

namespace MatchingService.Application.Consumers;

public sealed class DriverAvailabilityConsumer(DriverMatchingAppService matchingService) :
    IConsumer<DriverRejectedOfferEvent>,
    IConsumer<TripCancelledEvent>,
    IConsumer<TripCompletedEvent>
{
    public Task Consume(ConsumeContext<DriverRejectedOfferEvent> context)
        => matchingService.ReleaseDriverAsync(context.Message.DriverId, context.Message.TripId, context.CancellationToken);

    public Task Consume(ConsumeContext<TripCancelledEvent> context)
        => matchingService.CancelTripAsync(
            context.Message.TripId,
            context.Message.DriverId,
            context.CancellationToken);

    public Task Consume(ConsumeContext<TripCompletedEvent> context)
        => matchingService.ReleaseDriverAsync(context.Message.DriverId, context.Message.TripId, context.CancellationToken);
}
