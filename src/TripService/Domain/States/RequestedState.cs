using TripService.Domain.Enums;

namespace TripService.Domain.States;

public sealed class RequestedState : TripState
{
    public override TripStatus Status => TripStatus.Requested;

    public override TripState Accept() => new DriverAcceptedState();

    public override TripState Cancel(string? reason = null) => new CancelledState();

    public override TripState Fail(string? reason = null) => new FailedState();
}
