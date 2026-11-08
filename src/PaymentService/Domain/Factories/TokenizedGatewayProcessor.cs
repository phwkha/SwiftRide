using System.Collections.Concurrent;

namespace PaymentService.Domain.Factories;

/// <summary>
/// Gia lap cong thanh toan ngoai (Momo / VNPay / Stripe) thu tien qua token PCI-DSS.
/// Token bat dau bang "tok_fail" luon tra loi tam thoi de demo retry o SD 13.
/// </summary>
public class TokenizedGatewayProcessor : IPaymentMethodProcessor
{
    // Cong that tra lai cung ket qua cho cung idempotency key; gia lap bang bo nho dung chung.
    private static readonly ConcurrentDictionary<string, string> ChargedByKey = new();

    public TokenizedGatewayProcessor(string provider)
    {
        Method = provider;
    }

    public string Method { get; }

    public async Task<(bool Success, string? ExternalRef, string? Error)> ChargeAsync(
        Guid userId, decimal amount, string? token, string idempotencyKey)
    {
        await Task.Delay(100);

        if (string.IsNullOrWhiteSpace(token))
            return (false, null, $"{Method}: thieu payment token.");

        if (token.StartsWith("tok_fail", StringComparison.OrdinalIgnoreCase))
            return (false, null, $"{Method}: Gateway Timeout.");

        var externalRef = ChargedByKey.GetOrAdd(
            idempotencyKey, _ => $"{Method.ToLowerInvariant()}_{Guid.NewGuid():N}");

        return (true, externalRef, null);
    }
}
