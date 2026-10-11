using TripService.Domain.Enums;

namespace TripService.Domain.States;

public sealed class DroppedOffState : TripState
{
    public override TripStatus Status => TripStatus.DroppedOff;

    public override TripState RequestPayment() => new PaymentPendingState();
}
