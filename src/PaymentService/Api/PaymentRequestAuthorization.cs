using System.Security.Claims;
using PaymentService.Application.DTOs;
using PaymentService.Application.Services;
using PaymentService.Domain.Exceptions;
using PaymentService.Domain.Interfaces;
using SwiftRide.Security;

namespace PaymentService.Api;

public sealed class PaymentRequestAuthorization(ITripSnapshotClient trips, IPaymentRepository payments)
{
    public static Guid RequireUser(ClaimsPrincipal caller)
        => caller.AuthenticatedUserId()
            ?? throw new PaymentException(ErrorCodes.Unauthorized, 401, "Thieu danh tinh da xac thuc.");

    public static void RequireAccountAccess(ClaimsPrincipal caller, Guid ownerId)
    {
        var id = RequireUser(caller);
        if (id != ownerId && !caller.IsInRole("Admin"))
            throw Forbidden();
    }

    public async Task<ProcessPaymentRequest> AuthorizeProcessAsync(
        ClaimsPrincipal caller, string authorization, ProcessPaymentRequest request, CancellationToken ct)
    {
        var userId = RequireUser(caller);
        if (!caller.IsInRole("Rider"))
            throw Forbidden();
        var trip = await trips.GetAsync(request.TripId, authorization, ct);
        if (trip.RiderId != userId)
            throw Forbidden();
        // REST is a manual retry only. RabbitMQ automatic settlement remains internal.
        if (trip.Status != 5 || trip.DriverId is null || trip.DriverId == Guid.Empty || trip.ActualFare is null or <= 0)
            throw new PaymentException("PAYMENT_NOT_PENDING", 409,
                "Chi thanh toan lai chuyen di dang PaymentPending voi gia da quyet toan.");
        if (request.RiderId != trip.RiderId || request.DriverId != trip.DriverId || request.Amount != trip.ActualFare)
            throw new PaymentException("PAYMENT_TRIP_MISMATCH", 400,
                "Rider, tai xe hoac so tien khong khop du lieu chuyen di.");
        return request with { RiderId = trip.RiderId, DriverId = trip.DriverId.Value, Amount = trip.ActualFare.Value };
    }

    public async Task AuthorizeRefundAsync(
        ClaimsPrincipal caller, string authorization, Guid transactionId, CancellationToken ct)
    {
        var userId = RequireUser(caller);
        if (caller.IsInRole("Admin"))
            return;
        if (!caller.IsInRole("Rider"))
            throw Forbidden();
        var payment = await payments.GetTransactionWithEntriesAsync(transactionId, ct)
            ?? throw new PaymentException(ErrorCodes.NotFound, 404, "Khong tim thay giao dich.");
        if (payment.TripId is not Guid tripId || tripId == Guid.Empty)
            throw Forbidden();
        var trip = await trips.GetAsync(tripId, authorization, ct);
        if (trip.RiderId != userId)
            throw Forbidden();
    }

    private static PaymentException Forbidden()
        => new(ErrorCodes.Forbidden, 403, "Ban khong co quyen thuc hien thao tac nay.");
}
