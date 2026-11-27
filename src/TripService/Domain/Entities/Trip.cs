using TripService.Domain.Enums;

namespace TripService.Domain.Entities;

public class Trip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RiderId { get; set; }
    public Guid? DriverId { get; set; }
    public Guid? QuoteId { get; set; }
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
    public bool FareConfirmed { get; set; } = false;
    public DateTimeOffset? FareConfirmedAt { get; set; }
    public string VehicleType { get; set; } = "Bike";
    public int DriverRejectionCount { get; set; } = 0;
    public string? ExcludedDriverIds { get; set; }
    public int MatchingAttempt { get; set; }
    public DateTimeOffset? MatchingDeadline { get; set; }
    public Guid? OfferId { get; set; }
    public Guid? OfferedDriverId { get; set; }
    public DateTimeOffset? OfferExpiresAt { get; set; }

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
