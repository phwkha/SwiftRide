using System.Diagnostics;
using SwiftRide.Contracts;
using MatchingService.Application.DTOs;
using MatchingService.Domain.Interfaces;
using MassTransit;
using SwiftRide.Contracts.Events;

namespace MatchingService.Application.Services;

public sealed class DriverMatchingAppService(
    IDriverRepository driverRepository,
    ITopicProducer<string, DriverLocationUpdatedEvent> locationProducer,
    ITopicProducer<string, MatchingFailedEvent> matchingFailedProducer,
    ITopicProducer<string, TripOfferProposedEvent> tripOfferProducer,
    ILogger<DriverMatchingAppService>? logger = null)
{
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
            await locationProducer.Produce(tripId.ToString("D"), new DriverLocationUpdatedEvent(
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
        Guid tripId, decimal pickupLatitude, decimal pickupLongitude, string vehicleType,
        IEnumerable<Guid> excludedDriverIds, int attempt, DateTimeOffset deadline,
        CancellationToken cancellationToken = default)
    {
        // Older messages have no deadline and cannot participate in the new offer lifecycle.
        if (deadline == default || attempt < 1)
            return;

        var started = Stopwatch.GetTimestamp();
        var outcome = "failure";
        var tripKey = tripId.ToString("D");
        try
        {
            PricingAppService.ValidateCoordinates(pickupLatitude, pickupLongitude, "pickup");
            PricingAppService.ValidateVehicleType(vehicleType);

            // The retry and release are on separate queues. Close earlier attempts before selecting a driver.
            await CloseMatchingAttemptsAsync(tripId, attempt - 1, cancellationToken);
            if (await IsAttemptClosedAsync(tripKey, attempt, cancellationToken) || deadline <= DateTimeOffset.UtcNow)
            {
                outcome = "stale";
                return;
            }

            var driver = await driverRepository.GetReservedDriverAsync(tripKey, cancellationToken);
            if (driver is null)
            {
                var excluded = excludedDriverIds.Select(id => id.ToString("D"));
                driver = await driverRepository.ReserveNearestDriverAsync(
                    pickupLatitude, pickupLongitude, vehicleType, excluded, tripKey, attempt, cancellationToken)
                    ?? await driverRepository.GetReservedDriverAsync(tripKey, cancellationToken);
            }

            if (await IsAttemptClosedAsync(tripKey, attempt, cancellationToken))
            {
                outcome = "stale";
                return;
            }

            if (driver is null)
            {
                await matchingFailedProducer.Produce(tripKey, new MatchingFailedEvent(
                    tripId, "Không tìm thấy tài xế khả dụng quanh khu vực", attempt), cancellationToken);
                return;
            }

            var reservedAttempt = driver.ReservedAttempt.GetValueOrDefault();
            if (reservedAttempt < attempt)
                throw new InvalidOperationException("A concurrent delivery reserved a driver for an older matching attempt.");
            if (reservedAttempt > attempt)
            {
                outcome = "stale";
                return;
            }

            // Republish the proposal on redelivery in case the previous delivery stopped after reserving.
            // The TripService row version allows a single committed offer for this attempt.
            await tripOfferProducer.Produce(tripKey,
                new TripOfferProposedEvent(tripId, Guid.Parse(driver.DriverId), attempt), cancellationToken);
            outcome = "success";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError(ex, "Matching failed while processing trip {TripId}, attempt {Attempt}.", tripId, attempt);
            throw;
        }
        finally
        {
            BusinessMetrics.MatchingResults.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
            BusinessMetrics.MatchingDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }

    public async Task CloseMatchingAttemptsAsync(Guid tripId, int attempt, CancellationToken ct)
    {
        var tripKey = tripId.ToString("D");
        await driverRepository.RecordClosedAttemptsAsync(tripKey, attempt, ct);
        await driverRepository.ReleaseReservationsThroughAsync(tripKey, attempt, ct);
    }

    private async Task<bool> IsAttemptClosedAsync(string tripId, int attempt, CancellationToken ct)
    {
        if (await driverRepository.GetClosedThroughAttemptAsync(tripId, ct) < attempt)
            return false;

        // Covers a reservation created concurrently with closure or left before a consumer restarted.
        await driverRepository.ReleaseReservationsThroughAsync(tripId, attempt, ct);
        return true;
    }
}
