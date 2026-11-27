using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SwiftRide.Contracts.Events;
using TripService.Application.DTOs;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Domain.States;

namespace TripService.Application.Services;

public sealed partial class TripAppService
{
    private const int MaxMatchingAttempts = 4;
    private static readonly TimeSpan OfferDuration = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MatchingTimeout = TimeSpan.FromMinutes(1);

    public async Task<ApiResponse<TripDto>> AcceptTripAsync(Guid tripId, Guid driverId, CancellationToken ct = default, Guid? offerId = null)
    {
        var trip = await _tripReader.GetCurrentByIdAsync(tripId, ct);
        if (trip is null)
            return NotFoundResponse();

        var now = _clock.GetUtcNow();
        var error = ValidateOffer(trip, driverId, offerId, now);
        if (error is not null)
            return error;

        trip.Status = TripState.FromStatus(trip.Status).Accept().Status;
        trip.DriverId = driverId;
        trip.AcceptedAt = now;
        trip.UpdatedAt = now;
        ClearOffer(trip);
        var history = CreateHistory(trip, TripStatus.Requested, "Trip accepted", now);
        var accepted = CreateOutbox(nameof(TripAcceptedEvent), new TripAcceptedEvent(trip.Id, driverId, trip.RiderId));
        return await SaveTransitionAsync(trip, history, [accepted], ct);
    }

    public async Task<ApiResponse<TripDto>> RejectTripAsync(
        Guid tripId, Guid driverId, string? reason = null, CancellationToken ct = default, Guid? offerId = null)
    {
        if (reason?.Length > 255)
            return ApiResponse<TripDto>.Fail(400, "INVALID_REASON", "Reason must be at most 255 characters.", "reason");

        var trip = await _tripReader.GetCurrentByIdAsync(tripId, ct);
        if (trip is null)
            return NotFoundResponse();

        var now = _clock.GetUtcNow();
        var error = ValidateOffer(trip, driverId, offerId, now);
        if (error is not null)
            return error;

        trip.DriverRejectionCount++;
        try
        {
            await CloseOfferAsync(trip, driverId, reason ?? "Driver rejected offer", now, expired: false, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }

        return ApiResponse<TripDto>.Ok(TripDto.FromEntity(trip));
    }

    internal async Task RegisterOfferAsync(TripOfferProposedEvent proposal, CancellationToken ct)
    {
        var trip = await _tripReader.GetCurrentByIdAsync(proposal.TripId, ct);
        var now = _clock.GetUtcNow();
        if (trip is not { Status: TripStatus.Requested, FareConfirmedAt: not null, OfferId: null } ||
            trip.MatchingAttempt != proposal.Attempt || trip.MatchingDeadline is not { } deadline || deadline <= now ||
            proposal.DriverId == Guid.Empty || GetExcludedDriverIds(trip).Contains(proposal.DriverId))
            return;

        var offerId = Guid.NewGuid();
        var expiresAt = now.Add(OfferDuration);
        trip.MatchingDeadline = null;
        trip.OfferId = offerId;
        trip.OfferedDriverId = proposal.DriverId;
        trip.OfferExpiresAt = expiresAt;
        trip.UpdatedAt = now;
        var history = CreateHistory(trip, trip.Status, "Offer dispatched", now);
        var dispatch = CreateOutbox(nameof(TripOfferDispatchedEvent), new TripOfferDispatchedEvent(
            trip.Id, proposal.DriverId, trip.PickupAddress, trip.PickupLatitude, trip.PickupLongitude,
            trip.DropoffAddress, trip.DropoffLatitude, trip.DropoffLongitude, trip.EstimatedFare, trip.DistanceKm,
            trip.EstimatedDurationMinutes, (int)OfferDuration.TotalSeconds, expiresAt, offerId));

        await _tripWriter.UpdateWithOutboxesAsync(trip, history, [dispatch], ct);
    }

    internal async Task FailMatchingAsync(MatchingFailedEvent failure, CancellationToken ct)
    {
        var trip = await _tripReader.GetCurrentByIdAsync(failure.TripId, ct);
        if (trip is not { Status: TripStatus.Requested, FareConfirmedAt: not null, MatchingDeadline: not null } ||
            trip.MatchingAttempt != failure.Attempt)
            return;

        await FailMatchingAsync(trip, failure.Reason, _clock.GetUtcNow(), ct);
    }

    internal async Task ProcessMatchingDeadlineAsync(Guid tripId, CancellationToken ct)
    {
        var trip = await _tripReader.GetCurrentByIdAsync(tripId, ct);
        if (trip is not { Status: TripStatus.Requested, FareConfirmedAt: not null })
            return;

        var now = _clock.GetUtcNow();
        if (trip.OfferExpiresAt <= now && trip.OfferedDriverId is Guid driverId)
            await CloseOfferAsync(trip, driverId, "Cuốc xe đã hết thời gian phản hồi", now, expired: true, ct);
        else if (trip.MatchingDeadline <= now)
            await FailMatchingAsync(trip, "Quá thời gian tìm tài xế", now, ct);
    }

    private async Task CloseOfferAsync(Trip trip, Guid driverId, string reason, DateTimeOffset now, bool expired, CancellationToken ct)
    {
        var excluded = GetExcludedDriverIds(trip);
        excluded.Add(driverId);
        trip.ExcludedDriverIds = JsonSerializer.Serialize(excluded, JsonOptions);
        trip.UpdatedAt = now;

        var outboxes = new List<OutboxMessage> { CloseMatchingAttempt(trip) };
        if (expired)
            outboxes.Add(CreateOfferExpired(trip.Id, driverId, trip.OfferId!.Value, reason));

        ClearOffer(trip);
        if (trip.MatchingAttempt >= MaxMatchingAttempts)
        {
            MarkMatchingFailed(trip, "Đã hết lượt tìm tài xế.");
        }
        else
        {
            StartMatchingAttempt(trip, now);
            var retry = new RetryMatchingCommand(
                trip.Id, trip.PickupLatitude, trip.PickupLongitude, trip.PickupAddress,
                trip.DropoffLatitude, trip.DropoffLongitude, trip.DropoffAddress, trip.VehicleType,
                trip.DistanceKm, trip.EstimatedDurationMinutes, trip.EstimatedFare, trip.MatchingAttempt, excluded,
                trip.MatchingDeadline!.Value);
            outboxes.Add(CreateOutbox(nameof(RetryMatchingCommand), retry));
        }

        var history = CreateHistory(trip, TripStatus.Requested, reason, now);
        if (trip.Status == TripStatus.Failed)
            outboxes.Add(TripStatusOutbox.Create(trip, history, trip.CancellationReason));
        await _tripWriter.UpdateWithOutboxesAsync(trip, history, outboxes, ct);
    }

    private async Task FailMatchingAsync(Trip trip, string reason, DateTimeOffset now, CancellationToken ct)
    {
        trip.UpdatedAt = now;
        MarkMatchingFailed(trip, reason);
        var history = CreateHistory(trip, TripStatus.Requested, reason, now);
        await _tripWriter.UpdateWithOutboxesAsync(trip, history,
            [CloseMatchingAttempt(trip), TripStatusOutbox.Create(trip, history, reason)], ct);
    }

    private static void MarkMatchingFailed(Trip trip, string reason)
    {
        ClearOffer(trip);
        trip.Status = TripState.FromStatus(trip.Status).Fail(reason).Status;
        trip.CancellationReason = reason;
    }

    private static ApiResponse<TripDto>? ValidateOffer(Trip trip, Guid driverId, Guid? offerId, DateTimeOffset now)
    {
        if (driverId == Guid.Empty)
            return InvalidUserResponse();
        if (trip.Status != TripStatus.Requested || trip.FareConfirmedAt is null || trip.OfferId is null ||
            (offerId.HasValue && offerId != trip.OfferId))
            return ApiResponse<TripDto>.Fail(409, "OFFER_NOT_ACTIVE", "This trip has no matching active offer.");
        if (trip.OfferedDriverId != driverId)
            return ApiResponse<TripDto>.Fail(403, "FORBIDDEN_OFFER_ACCESS", "The offer was sent to another driver.");
        if (trip.OfferExpiresAt is not { } expiresAt || expiresAt <= now)
            return ApiResponse<TripDto>.Fail(409, "OFFER_EXPIRED", "The offer has expired.");

        return null;
    }

    private static void ClearOffer(Trip trip)
    {
        trip.MatchingDeadline = null;
        trip.OfferId = null;
        trip.OfferedDriverId = null;
        trip.OfferExpiresAt = null;
    }

    private static void StartMatchingAttempt(Trip trip, DateTimeOffset now)
    {
        trip.MatchingAttempt++;
        trip.MatchingDeadline = now.Add(MatchingTimeout);
    }

    private static OutboxMessage CloseMatchingAttempt(Trip trip)
        => CreateOutbox(nameof(MatchingAttemptClosedEvent), new MatchingAttemptClosedEvent(trip.Id, trip.MatchingAttempt));

    private static OutboxMessage CreateOfferExpired(Guid tripId, Guid driverId, Guid offerId, string reason)
        => CreateOutbox(nameof(TripOfferExpiredEvent), new TripOfferExpiredEvent(tripId, driverId, reason, offerId));

    private async Task<ApiResponse<TripDto>> SaveTransitionAsync(
        Trip trip, TripStatusHistory history, IReadOnlyCollection<OutboxMessage> messages, CancellationToken ct)
    {
        try
        {
            await _tripWriter.UpdateWithOutboxesAsync(trip, history, messages, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }

        return ApiResponse<TripDto>.Ok(TripDto.FromEntity(trip));
    }

    private static ApiResponse<TripDto> ConcurrencyConflict()
        => ApiResponse<TripDto>.Fail(409, "TRIP_CONCURRENCY_CONFLICT", "The trip was modified by another request.");
}
