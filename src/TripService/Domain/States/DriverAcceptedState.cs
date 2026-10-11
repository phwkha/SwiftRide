using TripService.Domain.Enums;

namespace TripService.Domain.States;

public sealed class DriverAcceptedState : TripState
{
    public override TripStatus Status => TripStatus.DriverAccepted;

    public override TripState StartRoute() => new EnRouteState();

    public override TripState Cancel(string? reason = null) => new CancelledState();
}
