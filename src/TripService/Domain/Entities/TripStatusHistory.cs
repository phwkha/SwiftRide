using TripService.Domain.Enums;

namespace TripService.Domain.Entities;

public class TripStatusHistory
{
    public long Id { get; set; }
    public Guid TripId { get; set; }
    public TripStatus? FromStatus { get; set; }
    public TripStatus ToStatus { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;

    public Trip? Trip { get; set; }
}
