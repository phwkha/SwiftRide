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

public record DriverRejectedOfferEvent(Guid TripId, Guid DriverId, string? Reason);

public record TripAcceptedEvent(Guid TripId, Guid DriverId, Guid RiderId);

public record MatchingFailedEvent(Guid TripId, string Reason);

public record TripDroppedOffEvent(
    Guid TripId,
    Guid RiderId,
    Guid DriverId,
    decimal ActualFare,
    string PaymentMethod,
    string IdempotencyKey);

public record PaymentSettledEvent(
    Guid TripId,
    Guid TransactionId,
    decimal TotalAmount,
    string PaymentMethod,
    decimal DriverCreditAmount);

public record PaymentFailedEvent(Guid TripId, string Status, string Reason);

public record TripCancelledEvent(Guid TripId, Guid? DriverId, Guid RiderId, string Reason);

public record TripCompletedEvent(
    Guid TripId,
    Guid RiderId,
    Guid DriverId,
    decimal TotalAmount,
    Guid TransactionId);
