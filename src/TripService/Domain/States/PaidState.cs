using TripService.Domain.Enums;

namespace TripService.Domain.States;

public sealed class PaidState : TripState
{
    public override TripStatus Status => TripStatus.Paid;
}
