namespace PaymentService.Domain.Factories;

/// <summary>
/// Bo xu ly mot phuong thuc thanh toan (Factory Pattern - Muc 7 PDF).
/// Wallet khong goi ra ngoai; Momo / VNPay / Stripe thu tien qua token do cong cap (SD 12, SD 15).
/// </summary>
public interface IPaymentMethodProcessor
{
    string Method { get; }

    /// <summary>
    /// Goi lenh tru tien. Cung mot idempotencyKey phai tra ve cung ket qua (cong khong tru 2 lan).
    /// Success = false nghia la loi tam thoi (timeout / mat ket noi), cho phep retry.
    /// </summary>
    Task<(bool Success, string? ExternalRef, string? Error)> ChargeAsync(
        Guid userId, decimal amount, string? token, string idempotencyKey);
}
