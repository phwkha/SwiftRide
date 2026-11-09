using MatchingService.Application.DTOs;
using MatchingService.Domain.Entities;
using MatchingService.Domain.Interfaces;
using MassTransit;
using StackExchange.Redis;
using SwiftRide.Contracts.Events;

namespace MatchingService.Application.Services;

public sealed class DriverMatchingAppService(
    IDriverRepository driverRepository,
    IPublishEndpoint publishEndpoint,
    IConnectionMultiplexer? redisConnection = null)
{
    private static readonly TimeSpan OfferCountdown = TimeSpan.FromSeconds(15);
    private readonly IConnectionMultiplexer? _redisConnection = redisConnection;

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

        var drivers = await driverRepository.FindNearbyAvailableDriversAsync(
            request.PickupLatitude,
            request.PickupLongitude,
            request.RadiusKm,
            request.Limit,
            request.VehicleType,
            cancellationToken);

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
        var driver = await driverRepository.GetByDriverIdAsync(driverId, cancellationToken);
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
        var updated = await driverRepository.UpdateLocationAsync(
            driverId, request.Latitude, request.Longitude, request.Bearing, request.Speed, now, cancellationToken);

        if (!updated)
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
        var updated = await driverRepository.UpdateAvailabilityAsync(driverId, isAvailable, cancellationToken);
        if (updated)
        {
            return true;
        }

        if (isAvailable && await driverRepository.ExistsAsync(driverId, cancellationToken))
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

        var excludedIds = (excludedDriverIds ?? []).Select(id => id.ToString()).ToList();
        var driver = await driverRepository.ReserveNearestDriverAsync(
            pickupLatitude,
            pickupLongitude,
            vehicleType,
            excludedIds,
            tripId.ToString(),
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

        if (_redisConnection is not null)
        {
            var locked = await TryLockDriverForTripAsync(driverId, tripId);
            if (!locked)
            {
                await driverRepository.ReleaseDriverAsync(driver.DriverId, tripId.ToString(), CancellationToken.None);
                await publishEndpoint.Publish(new MatchingFailedEvent(tripId, "Không tìm thấy tài xế khả dụng quanh khu vực"), cancellationToken);
                return;
            }
        }

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
                await ReleaseDriverLockAsync(driverId);
            }
        }
        catch
        {
            await ReleaseDriverLockAsync(driverId);
            await driverRepository.ReleaseDriverAsync(driver.DriverId, tripId.ToString(), CancellationToken.None);
            throw;
        }
    }

    public async Task<bool> TryLockDriverForTripAsync(Guid driverId, Guid tripId)
    {
        if (_redisConnection is null) return true;
        var db = _redisConnection.GetDatabase();
        var lockKey = $"matching:lock:driver:{driverId}";

        // SET NX (When.NotExists): Chỉ khóa thành công nếu tài xế chưa bị khóa bởi cuốc xe khác
        return await db.StringSetAsync(
            lockKey,
            tripId.ToString(),
            TimeSpan.FromSeconds(15),
            When.NotExists);
    }

    public async Task ReleaseDriverLockAsync(Guid driverId)
    {
        if (_redisConnection is null) return;
        var db = _redisConnection.GetDatabase();
        var lockKey = $"matching:lock:driver:{driverId}";
        await db.KeyDeleteAsync(lockKey);
    }

    public async Task ReleaseDriverAsync(Guid driverId, Guid tripId, CancellationToken cancellationToken = default)
    {
        await driverRepository.ReleaseDriverAsync(driverId.ToString(), tripId.ToString(), cancellationToken);
        await ReleaseDriverLockAsync(driverId);
    }

    public Task ReleaseTripReservationAsync(Guid tripId, CancellationToken cancellationToken = default)
        => driverRepository.ReleaseTripReservationAsync(tripId.ToString(), cancellationToken);

    public async Task CancelTripAsync(Guid tripId, Guid? driverId, CancellationToken cancellationToken = default)
    {
        var cancellation = new CancelledTrip
        {
            TripId = tripId.ToString(),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        await driverRepository.UpsertCancelledTripAsync(cancellation, cancellationToken);

        var reservedDrivers = await driverRepository.GetDriversReservedForTripAsync(cancellation.TripId, cancellationToken);
        try
        {
            foreach (var driver in reservedDrivers)
            {
                if (Guid.TryParse(driver.DriverId, out var reservedDriverId))
                {
                    await PublishOfferExpiredAsync(tripId, reservedDriverId, cancellationToken);
                    await ReleaseDriverLockAsync(reservedDriverId);
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
        => driverRepository.IsTripCancelledAsync(tripId.ToString(), cancellationToken);
}
