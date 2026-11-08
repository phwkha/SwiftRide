using TripService.Application.DTOs;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Domain.Interfaces;
using TripService.Domain.States;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SwiftRide.Contracts.Events;

namespace TripService.Application.Services;

public sealed class TripAppService : ITripAppService
{
    private static readonly HashSet<string> SupportedVehicleTypes =
        new(StringComparer.OrdinalIgnoreCase) { "Bike", "Car4", "Car7" };

    private readonly ITripWriter _tripWriter;
    private readonly ITripReader _tripReader;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public TripAppService(ITripWriter tripWriter, ITripReader tripReader)
    {
        _tripWriter = tripWriter;
        _tripReader = tripReader;
    }

    public async Task<ApiResponse<TripDto>> CreateTripAsync(
        Guid riderId,
        CreateTripRequest request,
        CancellationToken ct = default)
    {
        var validationError = Validate(riderId, request);
        if (validationError is not null)
        {
            return ApiResponse<TripDto>.Fail(
                400,
                validationError.Value.Code,
                validationError.Value.Message,
                validationError.Value.Field);
        }

        var now = DateTimeOffset.UtcNow;
        var trip = new Trip
        {
            RiderId = riderId,
            QuoteId = request.QuoteId,
            Status = TripStatus.Requested,
            PickupLatitude = request.PickupLatitude,
            PickupLongitude = request.PickupLongitude,
            PickupAddress = request.PickupAddress.Trim(),
            DropoffLatitude = request.DropoffLatitude,
            DropoffLongitude = request.DropoffLongitude,
            DropoffAddress = request.DropoffAddress.Trim(),
            DistanceKm = request.DistanceKm,
            EstimatedDurationMinutes = request.EstimatedDurationMinutes,
            EstimatedFare = request.EstimatedFare,
            FareConfirmed = false,
            VehicleType = NormalizeVehicleType(request.VehicleType),
            CreatedAt = now
        };

        var history = new TripStatusHistory
        {
            TripId = trip.Id,
            FromStatus = null,
            ToStatus = TripStatus.Requested,
            Reason = "Trip created",
            ChangedAt = now
        };

        await _tripWriter.AddAsync(trip, history, ct: ct);

        return ApiResponse<TripDto>.Created(TripDto.FromEntity(trip));
    }

    public async Task<ApiResponse<TripDto>> ConfirmQuoteAsync(
        Guid tripId,
        CancellationToken ct = default)
    {
        var trip = await _tripReader.GetByIdAsync(tripId, ct);
        if (trip is null)
            return NotFoundResponse();

        if (trip.Status != TripStatus.Requested)
            return InvalidTransitionResponse("A quote can only be confirmed for a requested trip.");

        var now = DateTimeOffset.UtcNow;
        trip.FareConfirmed = true;
        var history = CreateHistory(trip, TripStatus.Requested, "Quote confirmed", now);
        var outbox = CreateOutbox(
            nameof(TripRequestedEvent),
            new TripRequestedEvent(
                trip.Id,
                trip.RiderId,
                trip.PickupLatitude,
                trip.PickupLongitude,
                trip.PickupAddress,
                trip.DropoffLatitude,
                trip.DropoffLongitude,
                trip.DropoffAddress,
                trip.VehicleType,
                trip.DistanceKm,
                trip.EstimatedDurationMinutes,
                trip.EstimatedFare,
                GetExcludedDriverIds(trip)));

        await _tripWriter.UpdateWithOutboxAsync(trip, history, outbox, ct);
        return ApiResponse<TripDto>.Ok(TripDto.FromEntity(trip));
    }

    public async Task<ApiResponse<TripDto>> GetActiveTripAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            return InvalidUserResponse();

        var trip = await _tripReader.GetActiveTripByUserAsync(userId, ct);
        return trip is null
            ? NotFoundResponse()
            : ApiResponse<TripDto>.Ok(TripDto.FromEntity(trip));
    }

    public async Task<ApiResponse<TripDto>> GetTripByIdAsync(
        Guid tripId,
        CancellationToken ct = default)
    {
        var trip = await _tripReader.GetByIdAsync(tripId, ct);
        return trip is null
            ? NotFoundResponse()
            : ApiResponse<TripDto>.Ok(TripDto.FromEntity(trip));
    }

    public async Task<ApiResponse<TripDto>> AcceptTripAsync(
        Guid tripId,
        Guid driverId,
        CancellationToken ct = default)
    {
        if (driverId == Guid.Empty)
            return InvalidUserResponse();

        var trip = await _tripReader.GetByIdAsync(tripId, ct);
        if (trip is null)
            return NotFoundResponse();

        TripState nextState;
        try
        {
            nextState = TripState.FromStatus(trip.Status).Accept();
        }
        catch (InvalidOperationException ex)
        {
            return InvalidTransitionResponse(ex.Message);
        }

        var now = DateTimeOffset.UtcNow;
        var previousStatus = trip.Status;
        trip.Status = nextState.Status;
        trip.DriverId = driverId;
        trip.AcceptedAt = now;
        trip.UpdatedAt = now;
        var history = CreateHistory(trip, previousStatus, "Trip accepted", now);
        var outbox = CreateOutbox(
            nameof(TripAcceptedEvent),
            new TripAcceptedEvent(trip.Id, driverId, trip.RiderId));

        try
        {
            await _tripWriter.UpdateWithOutboxAsync(trip, history, outbox, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<TripDto>.Fail(
                409,
                "TRIP_ALREADY_ACCEPTED",
                "The trip was modified by another request.");
        }

        return ApiResponse<TripDto>.Ok(TripDto.FromEntity(trip));
    }

    public async Task<ApiResponse<TripDto>> RejectTripAsync(
        Guid tripId,
        Guid driverId,
        string? reason = null,
        CancellationToken ct = default)
    {
        if (driverId == Guid.Empty)
            return InvalidUserResponse();

        var trip = await _tripReader.GetByIdAsync(tripId, ct);
        if (trip is null)
            return NotFoundResponse();

        var now = DateTimeOffset.UtcNow;
        var previousStatus = trip.Status;
        trip.DriverRejectionCount++;
        var excludedDriverIds = GetExcludedDriverIds(trip);
        if (!excludedDriverIds.Contains(driverId))
            excludedDriverIds.Add(driverId);
        trip.ExcludedDriverIds = JsonSerializer.Serialize(excludedDriverIds, JsonOptions);
        trip.UpdatedAt = now;

        var outboxes = new List<OutboxMessage>
        {
            CreateOutbox(
                nameof(DriverRejectedOfferEvent),
                new DriverRejectedOfferEvent(trip.Id, driverId, reason))
        };

        var nextState = TripState.FromStatus(trip.Status);
        if (trip.DriverRejectionCount > 3)
        {
            try
            {
                nextState = TripState.FromStatus(trip.Status).Fail(reason);
            }
            catch (InvalidOperationException ex)
            {
                return InvalidTransitionResponse(ex.Message);
            }

            trip.Status = nextState.Status;
        }
        else
        {
            outboxes.Add(CreateOutbox(
                nameof(RetryMatchingCommand),
                new RetryMatchingCommand(
                    trip.Id,
                    trip.PickupLatitude,
                    trip.PickupLongitude,
                    trip.PickupAddress,
                    trip.DropoffLatitude,
                    trip.DropoffLongitude,
                    trip.DropoffAddress,
                    trip.VehicleType,
                    trip.DistanceKm,
                    trip.EstimatedDurationMinutes,
                    trip.EstimatedFare,
                    trip.DriverRejectionCount,
                    excludedDriverIds)));
        }

        var history = CreateHistory(trip, previousStatus, "Driver rejected offer", now);
        await _tripWriter.UpdateWithOutboxesAsync(trip, history, outboxes, ct);
        return ApiResponse<TripDto>.Ok(TripDto.FromEntity(trip));
    }

    public async Task<ApiResponse<TripDto>> StartRouteAsync(
        Guid tripId,
        Guid? driverId = null,
        CancellationToken ct = default)
    {
        var trip = await _tripReader.GetByIdAsync(tripId, ct);
        if (trip is null)
            return NotFoundResponse();

        if (driverId.HasValue && driverId.Value != Guid.Empty && trip.DriverId != driverId.Value)
        {
            return ApiResponse<TripDto>.Fail(
                403,
                "FORBIDDEN_TRIP_ACCESS",
                "Bạn không phải là tài xế được phân công cho chuyến đi này.");
        }

        return await ApplyTransitionAsync(
            trip,
            () => TripState.FromStatus(trip.Status).StartRoute(),
            "Route started",
            ct,
            concurrencyErrorCode: "TRIP_CONCURRENCY_CONFLICT");
    }

    public async Task<ApiResponse<TripDto>> PickUpAsync(
        Guid tripId,
        Guid? driverId = null,
        CancellationToken ct = default)
    {
        var trip = await _tripReader.GetByIdAsync(tripId, ct);
        if (trip is null)
            return NotFoundResponse();

        if (driverId.HasValue && driverId.Value != Guid.Empty && trip.DriverId != driverId.Value)
        {
            return ApiResponse<TripDto>.Fail(
                403,
                "FORBIDDEN_TRIP_ACCESS",
                "Bạn không phải là tài xế được phân công cho chuyến đi này.");
        }

        var now = DateTimeOffset.UtcNow;
        return await ApplyTransitionAsync(
            trip,
            () => TripState.FromStatus(trip.Status).PickUp(),
            "Rider picked up",
            ct,
            now,
            trip => trip.PickedUpAt = now,
            concurrencyErrorCode: "TRIP_CONCURRENCY_CONFLICT");
    }

    public async Task<ApiResponse<TripDto>> DropOffAsync(
        Guid tripId,
        Guid? driverId = null,
        string paymentMethod = "Wallet",
        CancellationToken ct = default)
    {
        var trip = await _tripReader.GetByIdAsync(tripId, ct);
        if (trip is null)
            return NotFoundResponse();

        if (driverId.HasValue && driverId.Value != Guid.Empty && trip.DriverId != driverId.Value)
        {
            return ApiResponse<TripDto>.Fail(
                403,
                "FORBIDDEN_TRIP_ACCESS",
                "Bạn không phải là tài xế được phân công cho chuyến đi này.");
        }

        if (trip.DriverId is null)
            return ApiResponse<TripDto>.Fail(400, "DRIVER_REQUIRED", "A driver is required to drop off a trip.");
        if (string.IsNullOrWhiteSpace(paymentMethod))
            return ApiResponse<TripDto>.Fail(400, "INVALID_PAYMENT_METHOD", "PaymentMethod is required.");

        var now = DateTimeOffset.UtcNow;
        TripState nextState;
        try
        {
            nextState = TripState.FromStatus(trip.Status).DropOff();
        }
        catch (InvalidOperationException ex)
        {
            return InvalidTransitionResponse(ex.Message);
        }

        var previousStatus = trip.Status;
        trip.Status = nextState.Status;
        trip.DroppedOffAt = now;
        trip.ActualFare = trip.EstimatedFare;
        trip.UpdatedAt = now;
        var history = CreateHistory(trip, previousStatus, "Trip dropped off", now);
        var outbox = CreateOutbox(
            nameof(TripDroppedOffEvent),
            new TripDroppedOffEvent(
                trip.Id,
                trip.RiderId,
                trip.DriverId.Value,
                trip.ActualFare.Value,
                paymentMethod.Trim(),
                $"pay_trip_{trip.Id}"));

        await _tripWriter.UpdateWithOutboxAsync(trip, history, outbox, ct);
        return ApiResponse<TripDto>.Ok(TripDto.FromEntity(trip));
    }

    public async Task<ApiResponse<TripDto>> CancelTripAsync(
        Guid tripId,
        Guid? callerUserId = null,
        string? reason = null,
        CancellationToken ct = default)
    {
        var trip = await _tripReader.GetByIdAsync(tripId, ct);
        if (trip is null)
            return NotFoundResponse();

        if (callerUserId.HasValue && callerUserId.Value != Guid.Empty && trip.RiderId != callerUserId.Value && trip.DriverId != callerUserId.Value)
        {
            return ApiResponse<TripDto>.Fail(
                403,
                "FORBIDDEN_TRIP_ACCESS",
                "Bạn không có quyền hủy chuyến đi của người dùng khác.");
        }

        TripState nextState;
        try
        {
            nextState = TripState.FromStatus(trip.Status).Cancel(reason);
        }
        catch (InvalidOperationException ex)
        {
            return InvalidTransitionResponse(ex.Message);
        }

        var now = DateTimeOffset.UtcNow;
        var previousStatus = trip.Status;
        trip.Status = nextState.Status;
        trip.CancellationReason = reason;
        trip.UpdatedAt = now;
        var history = CreateHistory(trip, previousStatus, reason ?? "Trip cancelled", now);
        var outbox = CreateOutbox(
            nameof(TripCancelledEvent),
            new TripCancelledEvent(trip.Id, trip.DriverId, trip.RiderId, reason ?? "Trip cancelled"));

        await _tripWriter.UpdateWithOutboxAsync(trip, history, outbox, ct);
        return ApiResponse<TripDto>.Ok(TripDto.FromEntity(trip));
    }

    private async Task<ApiResponse<TripDto>> ApplyTransitionAsync(
        Trip trip,
        Func<TripState> transition,
        string historyReason,
        CancellationToken ct,
        DateTimeOffset? changedAt = null,
        Action<Trip>? applyAdditionalChanges = null,
        string concurrencyErrorCode = "TRIP_ALREADY_ACCEPTED")
    {
        var now = changedAt ?? DateTimeOffset.UtcNow;
        var previousStatus = trip.Status;
        TripState nextState;
        try
        {
            nextState = transition();
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<TripDto>.Fail(409, "TRIP_INVALID_TRANSITION", ex.Message);
        }

        trip.Status = nextState.Status;
        trip.UpdatedAt = now;
        applyAdditionalChanges?.Invoke(trip);

        var history = new TripStatusHistory
        {
            TripId = trip.Id,
            FromStatus = previousStatus,
            ToStatus = trip.Status,
            Reason = historyReason,
            ChangedAt = now
        };

        try
        {
            await _tripWriter.UpdateWithOutboxAsync(trip, history, ct: ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<TripDto>.Fail(
                409,
                concurrencyErrorCode,
                "The trip was modified by another request.");
        }

        return ApiResponse<TripDto>.Ok(TripDto.FromEntity(trip));
    }

    private static TripStatusHistory CreateHistory(
        Trip trip,
        TripStatus fromStatus,
        string reason,
        DateTimeOffset changedAt)
        => new()
        {
            TripId = trip.Id,
            FromStatus = fromStatus,
            ToStatus = trip.Status,
            Reason = reason,
            ChangedAt = changedAt
        };

    private static OutboxMessage CreateOutbox(string eventType, object payload)
        => new()
        {
            EventType = eventType,
            Payload = JsonSerializer.Serialize(payload, JsonOptions)
        };

    private static List<Guid> GetExcludedDriverIds(Trip trip)
    {
        if (string.IsNullOrWhiteSpace(trip.ExcludedDriverIds))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(trip.ExcludedDriverIds, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ApiResponse<TripDto> InvalidTransitionResponse(string message)
        => ApiResponse<TripDto>.Fail(409, "TRIP_INVALID_TRANSITION", message);

    private static ApiResponse<TripDto> NotFoundResponse()
        => ApiResponse<TripDto>.Fail(404, "TRIP_NOT_FOUND", "Trip was not found.");

    private static ApiResponse<TripDto> InvalidUserResponse()
        => ApiResponse<TripDto>.Fail(400, "INVALID_USER_ID", "X-User-Id must be a non-empty GUID.");

    private static (string Code, string Message, string Field)? Validate(
        Guid riderId,
        CreateTripRequest request)
    {
        if (riderId == Guid.Empty)
            return ("INVALID_RIDER_ID", "RiderId is required.", nameof(riderId));

        if (string.IsNullOrWhiteSpace(request.PickupAddress))
            return ("INVALID_PICKUP_ADDRESS", "PickupAddress is required.", nameof(request.PickupAddress));

        if (string.IsNullOrWhiteSpace(request.DropoffAddress))
            return ("INVALID_DROPOFF_ADDRESS", "DropoffAddress is required.", nameof(request.DropoffAddress));

        if (!IsLatitude(request.PickupLatitude))
            return ("INVALID_PICKUP_LATITUDE", "PickupLatitude must be between -90 and 90.", nameof(request.PickupLatitude));

        if (!IsLongitude(request.PickupLongitude))
            return ("INVALID_PICKUP_LONGITUDE", "PickupLongitude must be between -180 and 180.", nameof(request.PickupLongitude));

        if (!IsLatitude(request.DropoffLatitude))
            return ("INVALID_DROPOFF_LATITUDE", "DropoffLatitude must be between -90 and 90.", nameof(request.DropoffLatitude));

        if (!IsLongitude(request.DropoffLongitude))
            return ("INVALID_DROPOFF_LONGITUDE", "DropoffLongitude must be between -180 and 180.", nameof(request.DropoffLongitude));

        if (request.DistanceKm < 0)
            return ("INVALID_DISTANCE", "DistanceKm cannot be negative.", nameof(request.DistanceKm));

        if (request.EstimatedDurationMinutes <= 0)
            return ("INVALID_DURATION", "EstimatedDurationMinutes must be greater than zero.", nameof(request.EstimatedDurationMinutes));

        if (request.EstimatedFare < 0)
            return ("INVALID_FARE", "EstimatedFare cannot be negative.", nameof(request.EstimatedFare));

        if (string.IsNullOrWhiteSpace(request.VehicleType) ||
            !SupportedVehicleTypes.Contains(request.VehicleType.Trim()))
            return ("INVALID_VEHICLE_TYPE", "VehicleType must be Bike, Car4, or Car7.", nameof(request.VehicleType));

        return null;
    }

    private static bool IsLatitude(decimal value) => value is >= -90m and <= 90m;

    private static bool IsLongitude(decimal value) => value is >= -180m and <= 180m;

    private static string NormalizeVehicleType(string value)
    {
        var normalized = value.Trim();
        return normalized.Equals("Bike", StringComparison.OrdinalIgnoreCase)
            ? "Bike"
            : normalized.Equals("Car4", StringComparison.OrdinalIgnoreCase)
                ? "Car4"
                : "Car7";
    }
}
