using TripService.Application.DTOs;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Domain.Interfaces;
using TripService.Domain.States;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SwiftRide.Contracts.Events;

namespace TripService.Application.Services;

public sealed partial class TripAppService : ITripAppService
{
    private static readonly HashSet<string> SupportedVehicleTypes =
        new(StringComparer.OrdinalIgnoreCase) { "Bike", "Car4", "Car7" };

    private readonly ITripWriter _tripWriter;
    private readonly ITripReader _tripReader;
    private readonly ITripQuoteClient _quoteClient;
    private readonly TimeProvider _clock;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public TripAppService(ITripWriter tripWriter, ITripReader tripReader, ITripQuoteClient quoteClient, TimeProvider clock)
    {
        _tripWriter = tripWriter;
        _tripReader = tripReader;
        _quoteClient = quoteClient;
        _clock = clock;
    }

    private static ApiResponse<TripDto> ForbiddenTripResponse()
        => ApiResponse<TripDto>.Fail(403, "FORBIDDEN_TRIP_ACCESS", "Bạn không có quyền truy cập chuyến đi này.");

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
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var trip = await _tripReader.GetCurrentByIdAsync(tripId, ct);
        if (trip is null)
            return NotFoundResponse();
        if (callerUserId == Guid.Empty || (trip.RiderId != callerUserId && trip.DriverId != callerUserId))
            return ForbiddenTripResponse();
        return ApiResponse<TripDto>.Ok(TripDto.FromEntity(trip));
    }

    public async Task<ApiResponse<TripDto>> StartRouteAsync(
        Guid tripId,
        Guid? driverId = null,
        CancellationToken ct = default)
    {
        var trip = await _tripReader.GetCurrentByIdAsync(tripId, ct);
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
        var trip = await _tripReader.GetCurrentByIdAsync(tripId, ct);
        if (trip is null)
            return NotFoundResponse();

        if (driverId.HasValue && driverId.Value != Guid.Empty && trip.DriverId != driverId.Value)
        {
            return ApiResponse<TripDto>.Fail(
                403,
                "FORBIDDEN_TRIP_ACCESS",
                "Bạn không phải là tài xế được phân công cho chuyến đi này.");
        }

        var now = _clock.GetUtcNow();
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
        var trip = await _tripReader.GetCurrentByIdAsync(tripId, ct);
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

        var now = _clock.GetUtcNow();
        TripState nextState;
        try
        {
            nextState = TripState.FromStatus(trip.Status).DropOff();
        }
        catch (InvalidOperationException ex)
        {
            return InvalidTransitionResponse(ex.Message);
        }

        if (trip.FareConfirmedAt is null)
            return ApiResponse<TripDto>.Fail(409, "FARE_NOT_CONFIRMED", "A verified fare is required for settlement.");

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

        return await SaveTransitionAsync(trip, history, [TripStatusOutbox.Create(trip, history), outbox], ct);
    }

    public async Task<ApiResponse<TripDto>> CancelTripAsync(
        Guid tripId,
        Guid? callerUserId = null,
        string? reason = null,
        CancellationToken ct = default)
    {
        var trip = await _tripReader.GetCurrentByIdAsync(tripId, ct);
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

        var now = _clock.GetUtcNow();
        var previousStatus = trip.Status;
        trip.Status = nextState.Status;
        trip.CancellationReason = reason;
        trip.UpdatedAt = now;
        var history = CreateHistory(trip, previousStatus, reason ?? "Trip cancelled", now);
        var outbox = CreateOutbox(
            nameof(TripCancelledEvent),
            new TripCancelledEvent(trip.Id, trip.DriverId, trip.RiderId, reason ?? "Trip cancelled"));

        var outboxes = new List<OutboxMessage> { outbox };
        if (previousStatus == TripStatus.Requested && trip.OfferId is Guid offerId && trip.OfferedDriverId is Guid offeredDriverId)
            outboxes.Add(CreateOfferExpired(trip.Id, offeredDriverId, offerId, "Chuyến đi đã bị hủy"));

        ClearOffer(trip);
        return await SaveTransitionAsync(trip, history, outboxes, ct);
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
        var now = changedAt ?? _clock.GetUtcNow();
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
            await _tripWriter.UpdateWithOutboxAsync(trip, history, TripStatusOutbox.Create(trip, history), ct);
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

}
