namespace PaymentService.Domain.Entities;

/// <summary>
/// Transactional Outbox pattern cho PaymentService.
/// Ghi event cung transaction voi du lieu, worker publish sau.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? Error { get; set; }

    // Backward compatibility aliases
    public string Type { get => EventType; set => EventType = value; }
    public DateTimeOffset OccurredOn { get => CreatedAt; set => CreatedAt = value; }
    public DateTimeOffset? ProcessedOn { get => ProcessedAt; set => ProcessedAt = value; }
}
