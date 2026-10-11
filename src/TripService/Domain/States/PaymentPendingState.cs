using TripService.Domain.Enums;

namespace TripService.Domain.States;

public sealed class PaymentPendingState : TripState
{
    public override TripStatus Status => TripStatus.PaymentPending;

    public override TripState ConfirmPayment() => new PaidState();

    public override TripState Fail(string? reason = null) => new FailedState();
}
