using MassTransit;
using SwiftRide.Contracts.Events;
using TripService.Application.Services;

namespace TripService.Application.Consumers;

// Keep the existing failure queue so pending events can be drained during an upgrade.
public sealed class MatchingFailedConsumer(TripAppService trips) : IConsumer<MatchingFailedEvent>
{
    public Task Consume(ConsumeContext<MatchingFailedEvent> context)
        => trips.FailMatchingAsync(context.Message, context.CancellationToken);
}

public sealed class TripOfferProposedConsumer(TripAppService trips) : IConsumer<TripOfferProposedEvent>
{
    public Task Consume(ConsumeContext<TripOfferProposedEvent> context)
        => trips.RegisterOfferAsync(context.Message, context.CancellationToken);
}
