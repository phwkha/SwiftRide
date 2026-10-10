namespace TripService.Domain.Enums;

public enum TripStatus
{
    Requested = 0,
    DriverAccepted = 1,
    EnRoute = 2,
    PickedUp = 3,
    DroppedOff = 4,
    PaymentPending = 5,
    Paid = 6,
    Completed = 7,
    Cancelled = 8,
    Failed = 9
}
