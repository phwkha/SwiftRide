using MatchingService.Domain.Entities;

namespace MatchingService.Domain.Interfaces;

public interface IDriverRepository
{
    Task<IReadOnlyList<Driver>> FindNearbyAvailableDriversAsync(
        decimal pickupLatitude,
        decimal pickupLongitude,
        double radiusKm,
        int limit,
        string? vehicleType,
        CancellationToken ct = default);

    Task<Driver?> GetByDriverIdAsync(string driverId, CancellationToken ct = default);
    Task<bool> UpdateLocationAsync(string driverId, decimal latitude, decimal longitude, double? bearing, double? speed, DateTime updatedAtUtc, CancellationToken ct = default);
    Task<bool> UpdateAvailabilityAsync(string driverId, bool isAvailable, CancellationToken ct = default);
    Task<bool> ExistsAsync(string driverId, CancellationToken ct = default);
    Task<Driver?> ReserveNearestDriverAsync(
        decimal pickupLatitude,
        decimal pickupLongitude,
        string vehicleType,
        IEnumerable<string> excludedDriverIds,
        string tripId,
        CancellationToken ct = default);
    Task ReleaseDriverAsync(string driverId, string tripId, CancellationToken ct = default);
    Task ReleaseTripReservationAsync(string tripId, CancellationToken ct = default);
    Task<IReadOnlyList<Driver>> GetDriversReservedForTripAsync(string tripId, CancellationToken ct = default);
    Task UpsertCancelledTripAsync(CancelledTrip cancelledTrip, CancellationToken ct = default);
    Task<bool> IsTripCancelledAsync(string tripId, CancellationToken ct = default);
}
