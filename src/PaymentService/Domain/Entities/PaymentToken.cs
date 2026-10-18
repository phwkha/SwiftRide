namespace PaymentService.Domain.Entities;

public class PaymentToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Provider { get; set; } = "Momo"; // Momo, VNPay, Stripe
    public string Token { get; set; } = string.Empty;
    public string MaskedToken { get; set; } = string.Empty;
    public bool IsDefault { get; set; } = false;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
