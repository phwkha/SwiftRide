using MatchingService.Domain.Entities;
using MatchingService.Domain.Interfaces;
using MatchingService.Infrastructure.Data;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;

namespace MatchingService.Infrastructure.Repositories;

public sealed class DriverRepository(MongoDbContext mongoContext) : IDriverRepository
{
    public async Task<IReadOnlyList<Driver>> FindNearbyAvailableDriversAsync(
        decimal pickupLatitude,
        decimal pickupLongitude,
        double radiusKm,
        int limit,
        string? vehicleType,
        CancellationToken ct = default)
    {
        var pickupPoint = GeoJson.Point(GeoJson.Geographic((double)pickupLongitude, (double)pickupLatitude));
        var filters = new List<FilterDefinition<Driver>>
        {
            Builders<Driver>.Filter.Eq(driver => driver.IsActive, true),
            Builders<Driver>.Filter.Eq(driver => driver.IsAvailable, true),
            Builders<Driver>.Filter.Eq(driver => driver.ReservedTripId, null),
            Builders<Driver>.Filter.NearSphere(driver => driver.Location, pickupPoint, radiusKm * 1000)
        };
        if (!string.IsNullOrWhiteSpace(vehicleType))
        {
            filters.Add(Builders<Driver>.Filter.Eq(driver => driver.Vehicle.Type, vehicleType));
        }

        return await mongoContext.Drivers.Find(Builders<Driver>.Filter.And(filters))
            .Limit(limit)
            .ToListAsync(ct);
    }

    public async Task<Driver?> GetByDriverIdAsync(string driverId, CancellationToken ct = default)
        => await mongoContext.Drivers.Find(d => d.DriverId == driverId).FirstOrDefaultAsync(ct);

    public async Task<bool> UpdateLocationAsync(string driverId, decimal latitude, decimal longitude, double? bearing, double? speed, DateTime updatedAtUtc, CancellationToken ct = default)
    {
        var update = Builders<Driver>.Update
            .Set(d => d.Location, GeoJson.Point(GeoJson.Geographic((double)longitude, (double)latitude)))
            .Set(d => d.Bearing, bearing)
            .Set(d => d.Speed, speed)
            .Set(d => d.LastLocationUpdatedAt, updatedAtUtc);

        var result = await mongoContext.Drivers.UpdateOneAsync(d => d.DriverId == driverId, update, cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    public async Task<bool> UpdateAvailabilityAsync(string driverId, bool isAvailable, CancellationToken ct = default)
    {
        var filter = Builders<Driver>.Filter.Eq(driver => driver.DriverId, driverId);
        if (isAvailable)
        {
            filter &= Builders<Driver>.Filter.Eq(driver => driver.ReservedTripId, null);
        }

        var update = Builders<Driver>.Update
            .Set(driver => driver.IsAvailable, isAvailable)
            .Set(driver => driver.LastLocationUpdatedAt, DateTime.UtcNow);

        var result = await mongoContext.Drivers.UpdateOneAsync(
            filter,
            update,
            cancellationToken: ct);

        return result.MatchedCount > 0;
    }

    public async Task<bool> ExistsAsync(string driverId, CancellationToken ct = default)
        => await mongoContext.Drivers.Find(driver => driver.DriverId == driverId).AnyAsync(ct);

    public async Task<Driver?> ReserveNearestDriverAsync(
        decimal pickupLatitude,
        decimal pickupLongitude,
        string vehicleType,
        IEnumerable<string> excludedDriverIds,
        string tripId,
        int attempt,
        CancellationToken ct = default)
    {
        var pickupPoint = GeoJson.Point(GeoJson.Geographic((double)pickupLongitude, (double)pickupLatitude));
        var available = Builders<Driver>.Filter.Where(driver =>
            driver.IsActive && driver.IsAvailable && driver.Vehicle.Type == vehicleType && driver.ReservedTripId == null);
        var nearby = available &
            Builders<Driver>.Filter.Nin(driver => driver.DriverId, excludedDriverIds) &
            Builders<Driver>.Filter.NearSphere(driver => driver.Location, pickupPoint);

        var reservation = Builders<Driver>.Update
            .Set(driver => driver.ReservedTripId, tripId)
            .Set(driver => driver.ReservedAttempt, attempt);

        using var candidates = await mongoContext.Drivers.Find(nearby).Project(driver => driver.DriverId).ToCursorAsync(ct);
        try
        {
            while (await candidates.MoveNextAsync(ct))
            {
                foreach (var driverId in candidates.Current)
                {
                    var reserved = await mongoContext.Drivers.FindOneAndUpdateAsync(
                        available & Builders<Driver>.Filter.Eq(driver => driver.DriverId, driverId),
                        reservation, new FindOneAndUpdateOptions<Driver> { ReturnDocument = ReturnDocument.After }, ct);
                    if (reserved is not null)
                        return reserved;
                }
            }
        }
        catch (MongoCommandException ex) when (ex.Code == 11000)
        {
            // The unique trip index allows concurrent deliveries to share one reservation.
            return null;
        }

        return null;
    }

    public async Task<Driver?> GetReservedDriverAsync(string tripId, CancellationToken ct = default)
        => await mongoContext.Drivers.Find(driver => driver.ReservedTripId == tripId).FirstOrDefaultAsync(ct);

    public async Task ReleaseReservationsThroughAsync(string tripId, int attempt, CancellationToken ct = default)
    {
        var release = Builders<Driver>.Update
            .Set(driver => driver.ReservedTripId, null)
            .Set(driver => driver.ReservedAttempt, null);

        // Older reservations set IsAvailable=false on acquisition. New reservations preserve the driver's setting.
        await mongoContext.Drivers.UpdateManyAsync(
            driver => driver.ReservedTripId == tripId && driver.ReservedAttempt == null,
            release.Set(driver => driver.IsAvailable, true), cancellationToken: ct);
        await mongoContext.Drivers.UpdateManyAsync(
            driver => driver.ReservedTripId == tripId && driver.ReservedAttempt <= attempt,
            release, cancellationToken: ct);
    }

    public async Task RecordClosedAttemptsAsync(string tripId, int attempt, CancellationToken ct = default)
    {
        var update = Builders<MatchingClosure>.Update
            .Max(trip => trip.ClosedThroughAttempt, attempt)
            .Max(trip => trip.ExpiresAt, DateTime.UtcNow.AddDays(7));
        await mongoContext.MatchingClosures.UpdateOneAsync(
            trip => trip.TripId == tripId, update, new UpdateOptions { IsUpsert = true }, ct);
    }

    public async Task<int> GetClosedThroughAttemptAsync(string tripId, CancellationToken ct = default)
    {
        var closure = await mongoContext.MatchingClosures.Find(trip => trip.TripId == tripId).FirstOrDefaultAsync(ct);
        return closure?.ClosedThroughAttempt ?? -1;
    }
}
