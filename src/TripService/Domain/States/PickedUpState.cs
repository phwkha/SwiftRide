using TripService.Domain.Enums;

namespace TripService.Domain.States;

public sealed class PickedUpState : TripState
{
    public override TripStatus Status => TripStatus.PickedUp;

    public override TripState DropOff() => new DroppedOffState();
}
