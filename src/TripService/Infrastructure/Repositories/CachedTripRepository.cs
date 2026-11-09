using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using TripService.Domain.Entities;
using TripService.Domain.Interfaces;

namespace TripService.Infrastructure.Repositories;

public sealed class CachedTripRepository : ITripReader, ITripWriter
{
    private readonly TripRepository _innerRepo;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachedTripRepository>? _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        PropertyNameCaseInsensitive = true
    };

    private static string GetTripKey(Guid tripId) => $"trip:active:{tripId}";

    public CachedTripRepository(
        TripRepository innerRepo,
        IDistributedCache cache,
        ILogger<CachedTripRepository>? logger = null)
    {
        _innerRepo = innerRepo;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Trip?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var key = GetTripKey(id);
        var cached = await _cache.GetStringAsync(key, ct);
        if (!string.IsNullOrEmpty(cached))
        {
            _logger?.LogInformation("[Redis HIT] Đọc Trip {TripId} từ bộ nhớ đệm.", id);
            return JsonSerializer.Deserialize<Trip>(cached, JsonOptions);
        }

        _logger?.LogInformation("[Redis MISS] Đọc Trip {TripId} từ SQL Server.", id);
        var trip = await _innerRepo.GetByIdAsync(id, ct);
        if (trip is not null)
        {
            await _cache.SetStringAsync(
                key,
                JsonSerializer.Serialize(trip, JsonOptions),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
                },
                ct);
        }

        return trip;
    }

    public Task<Trip?> GetActiveTripByUserAsync(Guid userId, CancellationToken ct = default)
        => _innerRepo.GetActiveTripByUserAsync(userId, ct);

    public async Task AddAsync(
        Trip trip,
        TripStatusHistory history,
        OutboxMessage? outbox = null,
        CancellationToken ct = default)
    {
        await _innerRepo.AddAsync(trip, history, outbox, ct);
        await InvalidateCacheAsync(trip.Id, ct);
    }

    public async Task UpdateWithOutboxAsync(
        Trip trip,
        TripStatusHistory history,
        OutboxMessage? outbox = null,
        CancellationToken ct = default)
    {
        await _innerRepo.UpdateWithOutboxAsync(trip, history, outbox, ct);
        await InvalidateCacheAsync(trip.Id, ct);
    }

    public async Task UpdateWithOutboxesAsync(
        Trip trip,
        TripStatusHistory history,
        IReadOnlyCollection<OutboxMessage> outboxes,
        CancellationToken ct = default)
    {
        await _innerRepo.UpdateWithOutboxesAsync(trip, history, outboxes, ct);
        await InvalidateCacheAsync(trip.Id, ct);
    }

    public async Task UpdateWithHistoriesAndOutboxAsync(
        Trip trip,
        IReadOnlyCollection<TripStatusHistory> histories,
        OutboxMessage outbox,
        CancellationToken ct = default)
    {
        await _innerRepo.UpdateWithHistoriesAndOutboxAsync(trip, histories, outbox, ct);
        await InvalidateCacheAsync(trip.Id, ct);
    }

    private async Task InvalidateCacheAsync(Guid tripId, CancellationToken ct)
    {
        var key = GetTripKey(tripId);
        await _cache.RemoveAsync(key, ct);
        _logger?.LogInformation("[Redis Invalidation] Đã xóa cache cho Trip {TripId}.", tripId);
    }
}
