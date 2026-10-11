using TripService.Domain.Enums;

namespace TripService.Domain.States;

public sealed class CancelledState : TripState
{
    public override TripStatus Status => TripStatus.Cancelled;
}
