using Microsoft.EntityFrameworkCore;
using TripQuoteSnapshot = SwiftRide.Contracts.Common.TripQuoteSnapshot;
using SwiftRide.Contracts.Events;
using TripService.Application.DTOs;
using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Application.Services;

public sealed partial class TripAppService
{
    public async Task<ApiResponse<TripDto>> CreateTripAsync(
        Guid riderId,
        CreateTripRequest request,
        CancellationToken ct = default)
    {
        var validationError = Validate(riderId, request);
        if (validationError is { } error)
            return ApiResponse<TripDto>.Fail(400, error.Code, error.Message, error.Field);

        var trip = new Trip
        {
            RiderId = riderId,
            PickupLatitude = TripQuoteSnapshot.NormalizeCoordinate(request.PickupLatitude),
            PickupLongitude = TripQuoteSnapshot.NormalizeCoordinate(request.PickupLongitude),
            PickupAddress = request.PickupAddress.Trim(),
            DropoffLatitude = TripQuoteSnapshot.NormalizeCoordinate(request.DropoffLatitude),
            DropoffLongitude = TripQuoteSnapshot.NormalizeCoordinate(request.DropoffLongitude),
            DropoffAddress = request.DropoffAddress.Trim(),
            VehicleType = NormalizeVehicleType(request.VehicleType),
            CreatedAt = _clock.GetUtcNow()
        };

        if (request.QuoteId is Guid quoteId)
        {
            var quoteError = await ApplyVerifiedQuoteAsync(trip, quoteId, ct);
            if (quoteError is not null)
                return quoteError;
        }

        var history = new TripStatusHistory
        {
            TripId = trip.Id,
            ToStatus = TripStatus.Requested,
            Reason = "Trip created",
            ChangedAt = trip.CreatedAt
        };

        await _tripWriter.AddAsync(trip, history, ct: ct);
        return ApiResponse<TripDto>.Created(TripDto.FromEntity(trip));
    }

    public async Task<ApiResponse<TripDto>> AttachQuoteAsync(Guid tripId, Guid callerUserId, Guid quoteId, CancellationToken ct = default)
    {
        var trip = await _tripReader.GetCurrentByIdAsync(tripId, ct);
        if (trip is null)
            return NotFoundResponse();
        if (callerUserId == Guid.Empty || trip.RiderId != callerUserId)
            return ForbiddenTripResponse();
        if (trip.Status != TripStatus.Requested || trip.FareConfirmedAt is not null)
            return InvalidTransitionResponse("A quote can only be attached before confirming the fare.");

        var error = await ApplyVerifiedQuoteAsync(trip, quoteId, ct);
        if (error is not null)
            return error;

        trip.FareConfirmed = false;
        trip.UpdatedAt = _clock.GetUtcNow();
        var history = CreateHistory(trip, trip.Status, "Quote attached", trip.UpdatedAt.Value);
        return await SaveTransitionAsync(trip, history, [], ct);
    }

    public async Task<ApiResponse<TripDto>> ConfirmQuoteAsync(
        Guid tripId, Guid callerUserId, CancellationToken ct = default, Guid? expectedQuoteId = null)
    {
        var trip = await _tripReader.GetCurrentByIdAsync(tripId, ct);
        if (trip is null)
            return NotFoundResponse();
        if (callerUserId == Guid.Empty || trip.RiderId != callerUserId)
            return ForbiddenTripResponse();
        if (expectedQuoteId.HasValue && expectedQuoteId != trip.QuoteId)
            return ApiResponse<TripDto>.Fail(409, "QUOTE_CHANGED", "The attached quote has changed.", "quoteId");
        if (trip.FareConfirmedAt is not null)
            return ApiResponse<TripDto>.Ok(TripDto.FromEntity(trip));
        if (trip.Status != TripStatus.Requested)
            return InvalidTransitionResponse("A quote can only be confirmed for a requested trip.");

        var quoteError = await ApplyVerifiedQuoteAsync(trip, trip.QuoteId.GetValueOrDefault(), ct);
        if (quoteError is not null)
            return quoteError;

        var now = _clock.GetUtcNow();
        trip.FareConfirmed = true;
        trip.FareConfirmedAt = now;
        trip.UpdatedAt = now;
        StartMatchingAttempt(trip, now);
        var history = CreateHistory(trip, trip.Status, "Quote confirmed", now);
        var outbox = CreateOutbox(nameof(TripRequestedEvent), new TripRequestedEvent(
            trip.Id, trip.RiderId, trip.PickupLatitude, trip.PickupLongitude, trip.PickupAddress,
            trip.DropoffLatitude, trip.DropoffLongitude, trip.DropoffAddress, trip.VehicleType,
            trip.DistanceKm, trip.EstimatedDurationMinutes, trip.EstimatedFare, GetExcludedDriverIds(trip),
            trip.MatchingDeadline!.Value));

        try
        {
            await _tripWriter.UpdateWithOutboxAsync(trip, history, outbox, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await _tripReader.GetCurrentByIdAsync(tripId, ct);
            if (current?.FareConfirmedAt is not null && current.QuoteId == trip.QuoteId)
                return ApiResponse<TripDto>.Ok(TripDto.FromEntity(current));

            return ConcurrencyConflict();
        }

        return ApiResponse<TripDto>.Ok(TripDto.FromEntity(trip));
    }

    private async Task<ApiResponse<TripDto>?> ApplyVerifiedQuoteAsync(Trip trip, Guid quoteId, CancellationToken ct)
    {
        if (quoteId == Guid.Empty)
            return ApiResponse<TripDto>.Fail(400, "QUOTE_REQUIRED", "A quoteId is required.", "quoteId");

        TripQuoteSnapshot? quote;
        try
        {
            quote = await _quoteClient.GetAsync(quoteId, ct);
        }
        catch (QuoteLookupException ex)
        {
            return ApiResponse<TripDto>.Fail(ex.StatusCode, ex.Code, ex.Message, "quoteId");
        }

        if (quote is null)
            return ApiResponse<TripDto>.Fail(404, "QUOTE_NOT_FOUND", "The quote was not found.", "quoteId");
        if (quote.RiderId != trip.RiderId)
            return ApiResponse<TripDto>.Fail(403, "FORBIDDEN_QUOTE_ACCESS", "The quote belongs to another rider.", "quoteId");
        if (quote.ExpiresAt <= _clock.GetUtcNow())
            return ApiResponse<TripDto>.Fail(409, "QUOTE_EXPIRED", "The quote has expired. Request a new quote.", "quoteId");
        if (quote.PickupLatitude != trip.PickupLatitude || quote.PickupLongitude != trip.PickupLongitude ||
            quote.DropoffLatitude != trip.DropoffLatitude || quote.DropoffLongitude != trip.DropoffLongitude ||
            quote.VehicleType != trip.VehicleType)
            return ApiResponse<TripDto>.Fail(400, "QUOTE_MISMATCH", "The quote must match the route and vehicle type.", "quoteId");

        trip.QuoteId = quoteId;
        trip.EstimatedFare = quote.TotalFare;
        trip.DistanceKm = quote.DistanceKm;
        trip.EstimatedDurationMinutes = quote.EstimatedDurationMinutes;
        return null;
    }

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
