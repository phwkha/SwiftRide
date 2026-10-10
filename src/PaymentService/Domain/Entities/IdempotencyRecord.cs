using PaymentService.Domain.Enums;

namespace PaymentService.Domain.Entities;

public class IdempotencyRecord
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public IdempotencyStatus Status { get; set; } = IdempotencyStatus.Processing;
    public string? ResponsePayload { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(24);
}
