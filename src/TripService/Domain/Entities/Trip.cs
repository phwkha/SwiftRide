using TripService.Domain.Enums;

namespace TripService.Domain.Entities;

public class Trip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RiderId { get; set; }
    public Guid? DriverId { get; set; }
    public TripStatus Status { get; set; } = TripStatus.Requested;

    // Pickup Location
    public decimal PickupLatitude { get; set; }
    public decimal PickupLongitude { get; set; }
    public string PickupAddress { get; set; } = string.Empty;

    // Dropoff Location
    public decimal DropoffLatitude { get; set; }
    public decimal DropoffLongitude { get; set; }
    public string DropoffAddress { get; set; } = string.Empty;

    // Trip Metrics
    public decimal DistanceKm { get; set; }
    public int EstimatedDurationMinutes { get; set; }
    public decimal EstimatedFare { get; set; }
    public decimal? ActualFare { get; set; }
    public int DriverRejectionCount { get; set; } = 0;

    // Timestamps
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? PickedUpAt { get; set; }
    public DateTimeOffset? DroppedOffAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CancellationReason { get; set; }

    // Concurrency Token
    public byte[] RowVersion { get; set; } = [];

    // Navigation Properties
    public ICollection<TripStatusHistory> StatusHistories { get; set; } = new List<TripStatusHistory>();
}
