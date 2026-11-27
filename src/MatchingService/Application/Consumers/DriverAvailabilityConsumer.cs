using MatchingService.Application.Services;
using MassTransit;
using SwiftRide.Contracts.Events;

namespace MatchingService.Application.Consumers;

public sealed class DriverAvailabilityConsumer(DriverMatchingAppService matchingService) :
    IConsumer<MatchingAttemptClosedEvent>,
    IConsumer<DriverRejectedOfferEvent>,
    IConsumer<TripCancelledEvent>,
    IConsumer<TripCompletedEvent>
{
    public Task Consume(ConsumeContext<MatchingAttemptClosedEvent> context)
        => matchingService.CloseMatchingAttemptsAsync(
            context.Message.TripId, context.Message.Attempt, context.CancellationToken);

    public Task Consume(ConsumeContext<DriverRejectedOfferEvent> context)
        => matchingService.CloseMatchingAttemptsAsync(context.Message.TripId, 0, context.CancellationToken);

    public Task Consume(ConsumeContext<TripCancelledEvent> context)
        => matchingService.CloseMatchingAttemptsAsync(context.Message.TripId, int.MaxValue, context.CancellationToken);

    public Task Consume(ConsumeContext<TripCompletedEvent> context)
        => matchingService.CloseMatchingAttemptsAsync(context.Message.TripId, int.MaxValue, context.CancellationToken);
}
