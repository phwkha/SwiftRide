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
        var update = Builders<Driver>.Update
            .Set(driver => driver.IsAvailable, isAvailable)
            .Set(driver => driver.LastLocationUpdatedAt, DateTime.UtcNow);

        if (isAvailable)
        {
            update = update.Set(driver => driver.ReservedTripId, null);
        }

        var result = await mongoContext.Drivers.UpdateOneAsync(
            driver => driver.DriverId == driverId,
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
        CancellationToken ct = default)
    {
        var pickupPoint = GeoJson.Point(GeoJson.Geographic((double)pickupLongitude, (double)pickupLatitude));
        var filter = Builders<Driver>.Filter.And(
            Builders<Driver>.Filter.Eq(driver => driver.IsActive, true),
            Builders<Driver>.Filter.Eq(driver => driver.IsAvailable, true),
            Builders<Driver>.Filter.Eq(driver => driver.Vehicle.Type, vehicleType),
            Builders<Driver>.Filter.Eq(driver => driver.ReservedTripId, null),
            Builders<Driver>.Filter.Nin(driver => driver.DriverId, excludedDriverIds),
            Builders<Driver>.Filter.NearSphere(driver => driver.Location, pickupPoint));

        var update = Builders<Driver>.Update
            .Set(driver => driver.IsAvailable, false)
            .Set(driver => driver.ReservedTripId, tripId)
            .Set(driver => driver.LastLocationUpdatedAt, DateTime.UtcNow);

        return await mongoContext.Drivers.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<Driver> { ReturnDocument = ReturnDocument.After },
            ct);
    }

    public async Task ReleaseDriverAsync(string driverId, string tripId, CancellationToken ct = default)
    {
        var update = Builders<Driver>.Update
            .Set(driver => driver.IsAvailable, true)
            .Set(driver => driver.ReservedTripId, null)
            .Set(driver => driver.LastLocationUpdatedAt, DateTime.UtcNow);

        await mongoContext.Drivers.UpdateOneAsync(
            driver => driver.DriverId == driverId && driver.ReservedTripId == tripId,
            update,
            cancellationToken: ct);
    }

    public async Task ReleaseTripReservationAsync(string tripId, CancellationToken ct = default)
    {
        var update = Builders<Driver>.Update
            .Set(driver => driver.IsAvailable, true)
            .Set(driver => driver.ReservedTripId, null)
            .Set(driver => driver.LastLocationUpdatedAt, DateTime.UtcNow);

        await mongoContext.Drivers.UpdateManyAsync(
            driver => driver.ReservedTripId == tripId,
            update,
            cancellationToken: ct);
    }

    public async Task<IReadOnlyList<Driver>> GetDriversReservedForTripAsync(string tripId, CancellationToken ct = default)
    {
        return await mongoContext.Drivers
            .Find(driver => driver.ReservedTripId == tripId)
            .ToListAsync(ct);
    }

    public async Task UpsertCancelledTripAsync(CancelledTrip cancelledTrip, CancellationToken ct = default)
    {
        await mongoContext.CancelledTrips.ReplaceOneAsync(
            record => record.TripId == cancelledTrip.TripId,
            cancelledTrip,
            new ReplaceOptions { IsUpsert = true },
            ct);
    }

    public async Task<bool> IsTripCancelledAsync(string tripId, CancellationToken ct = default)
    {
        return await mongoContext.CancelledTrips
            .Find(record => record.TripId == tripId)
            .AnyAsync(ct);
    }
}
