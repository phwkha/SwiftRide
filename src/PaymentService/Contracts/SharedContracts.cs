// TAM THOI: ban sao cac contract dung chung cua BE Lead (plan_member_1_be_lead.md muc 2).
// Xoa file nay khi project src/Shared.Contracts duoc merge va reference vao PaymentService.
// Giu nguyen namespace vi MassTransit dinh tuyen theo "Namespace + Ten Type".

namespace SwiftRide.Contracts.Common
{
    public record ApiError(string Code, string Message, string? Field = null);

    public record ApiResponse<T>(
        int StatusCode,
        string Message,
        T? Data,
        object? Meta = null,
        List<ApiError>? Errors = null,
        string? TraceId = null)
    {
        public static ApiResponse<T> Ok(T data, string message = "Success", string? traceId = null)
            => new(200, message, data, null, null, traceId);

        public static ApiResponse<T> Created(T data, string message = "Created", string? traceId = null)
            => new(201, message, data, null, null, traceId);

        public static ApiResponse<T> Fail(int statusCode, string code, string message, string? field = null, string? traceId = null)
            => new(statusCode, message, default, null, [new ApiError(code, message, field)], traceId);
    }
}

namespace SwiftRide.Contracts.Events
{
    // 9. TripService -> PaymentService (tai xe tra khach tai dich o SD 06 & SD 12)
    public record TripDroppedOffEvent(
        Guid TripId,
        Guid RiderId,
        Guid DriverId,
        decimal ActualFare,
        string PaymentMethod,
        string IdempotencyKey);

    // 10. PaymentService -> TripService (quyet toan so cai kep thanh cong o SD 08 & SD 12)
    public record PaymentSettledEvent(
        Guid TripId,
        Guid TransactionId,
        decimal TotalAmount,
        string PaymentMethod,
        decimal DriverCreditAmount);

    // 11. PaymentService -> TripService (loi cong thanh toan sau 3 lan retry o SD 13)
    public record PaymentFailedEvent(Guid TripId, string Status, string Reason);

    // CHUA CO trong danh sach cua BE Lead - can bo sung vao Shared.Contracts (SD 14)
    public record PaymentRefundedEvent(
        Guid TransactionId,
        Guid TripId,
        Guid RefundTransactionId,
        decimal RefundAmount,
        string Reason);
}
