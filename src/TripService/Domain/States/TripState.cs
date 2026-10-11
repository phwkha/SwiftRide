using TripService.Domain.Enums;

namespace TripService.Domain.States;

/// <summary>
/// Lop co so truu tuong cho State pattern - vong doi chuyen di.
/// Moi trang thai cu the override cac transition hop le.
/// </summary>
public abstract class TripState
{
    public abstract TripStatus Status { get; }

    public virtual TripState Accept()
        => throw new InvalidOperationException($"Khong the chap nhan chuyen di o trang thai {Status}");

    public virtual TripState StartRoute()
        => throw new InvalidOperationException($"Khong the bat dau di o trang thai {Status}");

    public virtual TripState PickUp()
        => throw new InvalidOperationException($"Khong the don khach o trang thai {Status}");

    public virtual TripState DropOff()
        => throw new InvalidOperationException($"Khong the tra khach o trang thai {Status}");

    public virtual TripState RequestPayment()
        => throw new InvalidOperationException($"Khong the yeu cau thanh toan o trang thai {Status}");

    public virtual TripState ConfirmPayment()
        => throw new InvalidOperationException($"Khong the xac nhan thanh toan o trang thai {Status}");

    public virtual TripState Cancel(string? reason = null)
        => throw new InvalidOperationException($"Khong the huy chuyen di o trang thai {Status}");

    public virtual TripState Fail(string? reason = null)
        => throw new InvalidOperationException($"Khong the danh dau that bai o trang thai {Status}");

    /// <summary>
    /// Factory method: tao TripState tu TripStatus enum.
    /// </summary>
    public static TripState FromStatus(TripStatus status) => status switch
    {
        TripStatus.Requested => new RequestedState(),
        TripStatus.DriverAccepted => new DriverAcceptedState(),
        TripStatus.EnRoute => new EnRouteState(),
        TripStatus.PickedUp => new PickedUpState(),
        TripStatus.DroppedOff => new DroppedOffState(),
        TripStatus.PaymentPending => new PaymentPendingState(),
        TripStatus.Paid => new PaidState(),
        TripStatus.Completed => new CompletedState(),
        TripStatus.Cancelled => new CancelledState(),
        TripStatus.Failed => new FailedState(),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Trang thai khong hop le")
    };
}
