namespace TripService.Domain.Entities;

public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; set; } = DateTimeOffset.UtcNow;
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset? ProcessedOn { get; set; }
    public string? Error { get; set; }
}
