namespace SwiftRide.Contracts.Events;

public record TripRequestedEvent(
    Guid TripId,
    Guid RiderId,
    decimal PickupLatitude,
    decimal PickupLongitude,
    string PickupAddress,
    decimal DropoffLatitude,
    decimal DropoffLongitude,
    string DropoffAddress,
    string VehicleType,
    decimal DistanceKm,
    int EstimatedDurationMinutes,
    decimal EstimatedFare,
    List<Guid> ExcludedDriverIds);

public record RetryMatchingCommand(
    Guid TripId,
    decimal PickupLatitude,
    decimal PickupLongitude,
    string PickupAddress,
    decimal DropoffLatitude,
    decimal DropoffLongitude,
    string DropoffAddress,
    string VehicleType,
    decimal DistanceKm,
    int EstimatedDurationMinutes,
    decimal EstimatedFare,
    int Attempt,
    List<Guid> ExcludedDriverIds);

public record TripOfferDispatchedEvent(
    Guid TripId,
    Guid DriverId,
    string PickupAddress,
    decimal PickupLatitude,
    decimal PickupLongitude,
    string DropoffAddress,
    decimal DropoffLatitude,
    decimal DropoffLongitude,
    decimal EstimatedFare,
    decimal DistanceKm,
    int DurationMinutes,
    int CountdownSeconds,
    DateTimeOffset ExpiresAt);

public record TripOfferExpiredEvent(Guid TripId, Guid DriverId, string Message);
public record DriverRejectedOfferEvent(Guid TripId, Guid DriverId, string? Reason);
public record MatchingFailedEvent(Guid TripId, string Reason);
public record TripAcceptedEvent(Guid TripId, Guid DriverId, Guid RiderId);
public record DriverLocationUpdatedEvent(
    Guid? TripId,
    Guid DriverId,
    decimal Latitude,
    decimal Longitude,
    double? Bearing,
    double? Speed,
    DateTimeOffset UpdatedAt);
public record TripCancelledEvent(Guid TripId, Guid? DriverId, Guid RiderId, string Reason);
public record TripCompletedEvent(Guid TripId, Guid RiderId, Guid DriverId, decimal TotalAmount, Guid TransactionId);
