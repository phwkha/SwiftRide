using TripService.Domain.Enums;

namespace TripService.Domain.States;

public sealed class CompletedState : TripState
{
    public override TripStatus Status => TripStatus.Completed;
}
