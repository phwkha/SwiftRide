using MatchingService.Application.DTOs;
using MatchingService.Domain.Entities;
using MatchingService.Infrastructure.Data;
using MassTransit;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;
using SwiftRide.Contracts.Events;

namespace MatchingService.Application.Services;

public sealed class DriverMatchingAppService(MongoDbContext mongoContext, IPublishEndpoint publishEndpoint)
{
    private static readonly TimeSpan OfferCountdown = TimeSpan.FromSeconds(15);

    public async Task<IReadOnlyList<NearbyDriverDto>> FindNearbyDriversAsync(FindNearbyDriversRequest request, CancellationToken cancellationToken = default)
    {
        PricingAppService.ValidateCoordinates(request.PickupLatitude, request.PickupLongitude, "pickup");
        if (request.RadiusKm is <= 0 or > 100)
        {
            throw new ArgumentException("radiusKm phải lớn hơn 0 và không vượt quá 100.", nameof(request.RadiusKm));
        }

        if (request.Limit is <= 0 or > 100)
        {
            throw new ArgumentException("limit phải nằm trong khoảng từ 1 đến 100.", nameof(request.Limit));
        }

        if (!string.IsNullOrWhiteSpace(request.VehicleType))
        {
            PricingAppService.ValidateVehicleType(request.VehicleType);
        }

        var pickupPoint = GeoJson.Point(GeoJson.Geographic((double)request.PickupLongitude, (double)request.PickupLatitude));
        var filters = new List<FilterDefinition<Driver>>
        {
            Builders<Driver>.Filter.Eq(driver => driver.IsActive, true),
            Builders<Driver>.Filter.Eq(driver => driver.IsAvailable, true),
            Builders<Driver>.Filter.Eq(driver => driver.ReservedTripId, null),
            Builders<Driver>.Filter.NearSphere(driver => driver.Location, pickupPoint, request.RadiusKm * 1000)
        };
        if (!string.IsNullOrWhiteSpace(request.VehicleType))
        {
            filters.Add(Builders<Driver>.Filter.Eq(driver => driver.Vehicle.Type, request.VehicleType));
        }

        var drivers = await mongoContext.Drivers.Find(Builders<Driver>.Filter.And(filters))
            .Limit(request.Limit)
            .ToListAsync(cancellationToken);

        return drivers
            .Where(driver => driver.Location is not null)
            .Select(driver => new NearbyDriverDto(
                driver.DriverId,
                driver.Location!.Coordinates.Latitude,
                driver.Location.Coordinates.Longitude,
                (double)PricingAppService.HaversineKm(
                    request.PickupLatitude,
                    request.PickupLongitude,
                    (decimal)driver.Location.Coordinates.Latitude,
                    (decimal)driver.Location.Coordinates.Longitude),
                driver.Rating,
                driver.Vehicle.Type))
            .ToArray();
    }

    public async Task<DriverLocationDto?> GetDriverLocationAsync(string driverId, CancellationToken cancellationToken = default)
    {
        var driver = await mongoContext.Drivers.Find(d => d.DriverId == driverId).FirstOrDefaultAsync(cancellationToken);
        if (driver?.Location is null)
        {
            return null;
        }

        return new DriverLocationDto(
            driver.DriverId,
            driver.Location.Coordinates.Latitude,
            driver.Location.Coordinates.Longitude,
            driver.Bearing,
            driver.Speed,
            driver.LastLocationUpdatedAt);
    }

    public async Task<bool> UpdateDriverLocationAsync(string driverId, DriverLocationUpdateRequest request, CancellationToken cancellationToken = default)
    {
        PricingAppService.ValidateCoordinates(request.Latitude, request.Longitude, "location");
        if (request.Bearing is < 0 or >= 360)
        {
            throw new ArgumentException("bearing phải nằm trong khoảng từ 0 đến dưới 360.", nameof(request.Bearing));
        }

        if (request.Speed is < 0)
        {
            throw new ArgumentException("speed không được âm.", nameof(request.Speed));
        }

        var now = DateTime.UtcNow;
        var point = GeoJson.Point(GeoJson.Geographic((double)request.Longitude, (double)request.Latitude));
        var update = Builders<Driver>.Update
            .Set(driver => driver.Location, point)
            .Set(driver => driver.Bearing, request.Bearing)
            .Set(driver => driver.Speed, request.Speed)
            .Set(driver => driver.LastLocationUpdatedAt, now);
        var result = await mongoContext.Drivers.UpdateOneAsync(d => d.DriverId == driverId, update, cancellationToken: cancellationToken);
        if (result.MatchedCount == 0)
        {
            return false;
        }

        if (request.ActiveTripId is Guid tripId)
        {
            await publishEndpoint.Publish(new DriverLocationUpdatedEvent(
                tripId,
                Guid.Parse(driverId),
                request.Latitude,
                request.Longitude,
                request.Bearing,
                request.Speed,
                new DateTimeOffset(now, TimeSpan.Zero)), cancellationToken);
        }

        return true;
    }

    public async Task<bool> UpdateAvailabilityAsync(string driverId, bool isAvailable, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Driver>.Filter.Eq(driver => driver.DriverId, driverId);
        if (isAvailable)
        {
            filter &= Builders<Driver>.Filter.Eq(driver => driver.ReservedTripId, null);
        }

        var result = await mongoContext.Drivers.UpdateOneAsync(
            filter,
            Builders<Driver>.Update.Set(driver => driver.IsAvailable, isAvailable),
            cancellationToken: cancellationToken);
        if (result.MatchedCount > 0)
        {
            return true;
        }

        if (isAvailable && await mongoContext.Drivers.Find(driver => driver.DriverId == driverId).AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException("Tài xế đang được giữ cho một chuyến đi khác.");
        }

        return false;
    }

    public async Task MatchTripAsync(
        Guid tripId,
        decimal pickupLatitude,
        decimal pickupLongitude,
        string pickupAddress,
        decimal dropoffLatitude,
        decimal dropoffLongitude,
        string dropoffAddress,
        string vehicleType,
        decimal distanceKm,
        int durationMinutes,
        decimal estimatedFare,
        IEnumerable<Guid>? excludedDriverIds,
        CancellationToken cancellationToken = default)
    {
        if (await IsTripCancelledAsync(tripId, cancellationToken))
        {
            return;
        }

        PricingAppService.ValidateCoordinates(pickupLatitude, pickupLongitude, "pickup");
        PricingAppService.ValidateCoordinates(dropoffLatitude, dropoffLongitude, "dropoff");
        PricingAppService.ValidateVehicleType(vehicleType);

        var point = GeoJson.Point(GeoJson.Geographic((double)pickupLongitude, (double)pickupLatitude));
        var excludedIds = (excludedDriverIds ?? []).Select(id => id.ToString()).ToList();
        var filter = Builders<Driver>.Filter.And(
            Builders<Driver>.Filter.Eq(driver => driver.IsActive, true),
            Builders<Driver>.Filter.Eq(driver => driver.IsAvailable, true),
            Builders<Driver>.Filter.Eq(driver => driver.ReservedTripId, null),
            Builders<Driver>.Filter.Eq(driver => driver.Vehicle.Type, vehicleType),
            Builders<Driver>.Filter.Nin(driver => driver.DriverId, excludedIds),
            Builders<Driver>.Filter.NearSphere(driver => driver.Location, point, 5000));
        var update = Builders<Driver>.Update
            .Set(driver => driver.IsAvailable, false)
            .Set(driver => driver.ReservedTripId, tripId.ToString());
        var driver = await mongoContext.Drivers.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<Driver> { ReturnDocument = ReturnDocument.After },
            cancellationToken);

        if (driver is null)
        {
            if (await IsTripCancelledAsync(tripId, cancellationToken))
            {
                return;
            }

            await publishEndpoint.Publish(new MatchingFailedEvent(tripId, "Không tìm thấy tài xế khả dụng quanh khu vực"), cancellationToken);
            return;
        }

        if (await IsTripCancelledAsync(tripId, cancellationToken))
        {
            await ReleaseTripReservationAsync(tripId, CancellationToken.None);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var driverId = Guid.Parse(driver.DriverId);
        try
        {
            await publishEndpoint.Publish(new TripOfferDispatchedEvent(
                tripId,
                driverId,
                pickupAddress,
                pickupLatitude,
                pickupLongitude,
                dropoffAddress,
                dropoffLatitude,
                dropoffLongitude,
                estimatedFare,
                distanceKm,
                durationMinutes,
                (int)OfferCountdown.TotalSeconds,
                now.Add(OfferCountdown)), cancellationToken);

            if (await IsTripCancelledAsync(tripId, cancellationToken))
            {
                await PublishOfferExpiredAsync(tripId, driverId, cancellationToken);
                await ReleaseTripReservationAsync(tripId, CancellationToken.None);
            }
        }
        catch
        {
            await mongoContext.Drivers.UpdateOneAsync(
                d => d.DriverId == driver.DriverId && d.ReservedTripId == tripId.ToString(),
                Builders<Driver>.Update
                    .Set(d => d.IsAvailable, true)
                    .Set(d => d.ReservedTripId, null),
                cancellationToken: CancellationToken.None);
            throw;
        }
    }

    public async Task ReleaseDriverAsync(Guid driverId, Guid tripId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Driver>.Filter.And(
            Builders<Driver>.Filter.Eq(driver => driver.DriverId, driverId.ToString()),
            Builders<Driver>.Filter.Eq(driver => driver.ReservedTripId, tripId.ToString()));
        await mongoContext.Drivers.UpdateOneAsync(
            filter,
            Builders<Driver>.Update
                .Set(driver => driver.IsAvailable, true)
                .Set(driver => driver.ReservedTripId, null),
            cancellationToken: cancellationToken);
    }

    public async Task ReleaseTripReservationAsync(Guid tripId, CancellationToken cancellationToken = default)
    {
        await mongoContext.Drivers.UpdateManyAsync(
            driver => driver.ReservedTripId == tripId.ToString(),
            Builders<Driver>.Update
                .Set(driver => driver.IsAvailable, true)
                .Set(driver => driver.ReservedTripId, null),
            cancellationToken: cancellationToken);
    }

    public async Task CancelTripAsync(Guid tripId, Guid? driverId, CancellationToken cancellationToken = default)
    {
        var cancellation = new CancelledTrip
        {
            TripId = tripId.ToString(),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        await mongoContext.CancelledTrips.ReplaceOneAsync(
            record => record.TripId == cancellation.TripId,
            cancellation,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);

        var reservedDrivers = await mongoContext.Drivers
            .Find(driver => driver.ReservedTripId == cancellation.TripId)
            .ToListAsync(cancellationToken);
        try
        {
            foreach (var driver in reservedDrivers)
            {
                if (Guid.TryParse(driver.DriverId, out var reservedDriverId))
                {
                    await PublishOfferExpiredAsync(tripId, reservedDriverId, cancellationToken);
                }
            }
        }
        finally
        {
            await ReleaseTripReservationAsync(tripId, cancellationToken);
            if (driverId is Guid assignedDriverId)
            {
                await ReleaseDriverAsync(assignedDriverId, tripId, cancellationToken);
            }
        }
    }

    private Task PublishOfferExpiredAsync(Guid tripId, Guid driverId, CancellationToken cancellationToken)
        => publishEndpoint.Publish(new TripOfferExpiredEvent(tripId, driverId, "Chuyến đi đã bị hủy"), cancellationToken);

    private Task<bool> IsTripCancelledAsync(Guid tripId, CancellationToken cancellationToken)
        => mongoContext.CancelledTrips.Find(record => record.TripId == tripId.ToString())
            .AnyAsync(cancellationToken);
}
