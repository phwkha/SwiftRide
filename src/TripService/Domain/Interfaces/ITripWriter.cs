using TripService.Domain.Entities;

namespace TripService.Domain.Interfaces;

public interface ITripWriter
{
    Task AddAsync(Trip trip, TripStatusHistory history, OutboxMessage? outbox = null, CancellationToken ct = default);
    Task UpdateWithOutboxAsync(Trip trip, TripStatusHistory history, OutboxMessage? outbox = null, CancellationToken ct = default);
    Task UpdateWithOutboxesAsync(
        Trip trip,
        TripStatusHistory history,
        IReadOnlyCollection<OutboxMessage> outboxes,
        CancellationToken ct = default);
    Task UpdateWithHistoriesAndOutboxAsync(
        Trip trip,
        IReadOnlyCollection<TripStatusHistory> histories,
        OutboxMessage outbox,
        CancellationToken ct = default);
}