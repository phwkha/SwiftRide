namespace PaymentService.Domain.Factories;

/// <summary>
/// Thanh toan bang vi noi bo: tien bi tru truc tiep tren so cai (Debit RiderWallet),
/// nen khong co buoc goi cong ngoai.
/// </summary>
public class WalletPaymentProcessor : IPaymentMethodProcessor
{
    public string Method => PaymentMethods.Wallet;

    public Task<(bool Success, string? ExternalRef, string? Error)> ChargeAsync(
        Guid userId, decimal amount, string? token, string idempotencyKey)
        => Task.FromResult<(bool, string?, string?)>((true, null, null));
}
