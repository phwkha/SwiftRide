using TripService.Domain.Enums;

namespace TripService.Domain.States;

public sealed class EnRouteState : TripState
{
    public override TripStatus Status => TripStatus.EnRoute;

    public override TripState PickUp() => new PickedUpState();

    public override TripState Cancel(string? reason = null) => new CancelledState();
}
