using TripService.Domain.Enums;

namespace TripService.Domain.States;

public sealed class FailedState : TripState
{
    public override TripStatus Status => TripStatus.Failed;
}
