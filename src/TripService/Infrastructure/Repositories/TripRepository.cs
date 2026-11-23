using Microsoft.EntityFrameworkCore;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Domain.Interfaces;
using TripService.Infrastructure.Data;

namespace TripService.Infrastructure.Repositories;

public class TripRepository : ITripReader, ITripWriter
{
    private readonly TripDbContext _context = null!;

    protected TripRepository()
    {
    }

    public TripRepository(TripDbContext context)
    {
        _context = context;
    }

    public virtual async Task<Trip?> GetByIdAsync(
        Guid id,
        CancellationToken ct = default)
        => await _context.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<Trip?> GetCurrentByIdAsync(Guid id, CancellationToken ct = default)
        => GetByIdAsync(id, ct);

    public virtual async Task<Trip?> GetActiveTripByUserAsync(
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

        return await _context.Trips.AsNoTracking()
        .Where(t =>
            (t.RiderId == userId || t.DriverId == userId) &&
            activeStatuses.Contains(t.Status))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public virtual async Task AddAsync(
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

    public virtual async Task UpdateWithOutboxAsync(
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

        await SaveTripChangesAsync(ct);
    }

    public virtual Task UpdateWithOutboxesAsync(
        Trip trip,
        TripStatusHistory history,
        IReadOnlyCollection<OutboxMessage> outboxes,
        CancellationToken ct = default)
    {
        history.TripId = trip.Id;
        _context.Trips.Update(trip);
        _context.TripStatusHistories.Add(history);
        _context.OutboxMessages.AddRange(outboxes);
        return SaveTripChangesAsync(ct);
    }

    public virtual async Task UpdateWithHistoriesAndOutboxesAsync(
        Trip trip,
        IReadOnlyCollection<TripStatusHistory> histories,
        IReadOnlyCollection<OutboxMessage> outboxes,
        CancellationToken ct = default)
    {
        _context.Trips.Update(trip);
        await _context.TripStatusHistories.AddRangeAsync(histories, ct);
        await _context.OutboxMessages.AddRangeAsync(outboxes, ct);
        await _context.SaveChangesAsync(ct);
    }

    private async Task SaveTripChangesAsync(CancellationToken ct)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A retried consumer must load a clean copy of the trip and its pending changes.
            _context.ChangeTracker.Clear();
            throw;
        }
    }
}
