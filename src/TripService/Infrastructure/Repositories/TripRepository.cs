using Microsoft.EntityFrameworkCore;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Domain.Interfaces;
using TripService.Infrastructure.Data;

namespace TripService.Infrastructure.Repositories;

public class TripRepository : ITripReader, ITripWriter
{
    private readonly TripDbContext _context;

    public TripRepository(TripDbContext context)
    {
        _context = context;
    }

    public async Task<Trip?> GetByIdAsync(
        Guid id,
        CancellationToken ct = default)
    {
        return await _context.Trips
            .FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<Trip?> GetActiveTripByUserAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var activeStatuses = new[]
        {
            TripStatus.Requested,
            TripStatus.DriverAccepted,
            TripStatus.EnRoute,
            TripStatus.PickedUp,
            TripStatus.DroppedOff,
            TripStatus.PaymentPending
        };

        return await _context.Trips
        .Where(t =>
            (t.RiderId == userId || t.DriverId == userId) &&
            activeStatuses.Contains(t.Status))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task AddAsync(
        Trip trip,
        TripStatusHistory history,
        OutboxMessage? outbox = null,
        CancellationToken ct = default)
    {
        history.TripId = trip.Id;

        await _context.Trips.AddAsync(trip, ct);
        await _context.TripStatusHistories.AddAsync(history, ct);

        if (outbox is not null)
        {
            await _context.OutboxMessages.AddAsync(outbox, ct);
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateWithOutboxAsync(
        Trip trip,
        TripStatusHistory history,
        OutboxMessage? outbox = null,
        CancellationToken ct = default)
    {
        history.TripId = trip.Id;

        _context.Trips.Update(trip);
        await _context.TripStatusHistories.AddAsync(history, ct);

        if (outbox is not null)
        {
            await _context.OutboxMessages.AddAsync(outbox, ct);
        }

        await _context.SaveChangesAsync(ct);
    }

    public Task UpdateWithOutboxesAsync(
        Trip trip,
        TripStatusHistory history,
        IReadOnlyCollection<OutboxMessage> outboxes,
        CancellationToken ct = default)
    {
        history.TripId = trip.Id;
        _context.Trips.Update(trip);
        _context.TripStatusHistories.Add(history);
        _context.OutboxMessages.AddRange(outboxes);
        return _context.SaveChangesAsync(ct);
    }

    public async Task UpdateWithHistoriesAndOutboxAsync(
        Trip trip,
        IReadOnlyCollection<TripStatusHistory> histories,
        OutboxMessage outbox,
        CancellationToken ct = default)
    {
        _context.Trips.Update(trip);
        await _context.TripStatusHistories.AddRangeAsync(histories, ct);
        await _context.OutboxMessages.AddAsync(outbox, ct);
        await _context.SaveChangesAsync(ct);
    }
}