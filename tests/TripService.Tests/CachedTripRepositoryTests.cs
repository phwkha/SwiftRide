using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Infrastructure.Repositories;
using Xunit;

namespace TripService.Tests;

public class CachedTripRepositoryTests
{
    private readonly Mock<TripRepository> _innerRepoMock = new();
    private readonly Mock<IDistributedCache> _cacheMock = new();
    private readonly CachedTripRepository _cachedRepo;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        PropertyNameCaseInsensitive = true
    };

    public CachedTripRepositoryTests()
    {
        _cachedRepo = new CachedTripRepository(
            _innerRepoMock.Object,
            _cacheMock.Object,
            NullLogger<CachedTripRepository>.Instance);
    }

    [Fact]
    public async Task GetByIdAsync_WhenCacheHit_ReturnsCachedTripWithoutCallingInnerRepo()
    {
        var tripId = Guid.NewGuid();
        var trip = new Trip
        {
            Id = tripId,
            RiderId = Guid.NewGuid(),
            Status = TripStatus.PickedUp,
            EstimatedFare = 50000m
        };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(trip, JsonOptions));

        _cacheMock
            .Setup(c => c.GetAsync($"trip:active:{tripId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        var result = await _cachedRepo.GetByIdAsync(tripId);

        Assert.NotNull(result);
        Assert.Equal(tripId, result.Id);
        Assert.Equal(TripStatus.PickedUp, result.Status);
        _innerRepoMock.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_WhenCacheMiss_QueriesInnerRepoAndCachesWith30MinTTL()
    {
        var tripId = Guid.NewGuid();
        var trip = new Trip
        {
            Id = tripId,
            RiderId = Guid.NewGuid(),
            Status = TripStatus.Requested,
            EstimatedFare = 70000m
        };

        _cacheMock
            .Setup(c => c.GetAsync($"trip:active:{tripId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        _innerRepoMock
            .Setup(r => r.GetByIdAsync(tripId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(trip);

        var result = await _cachedRepo.GetByIdAsync(tripId);

        Assert.NotNull(result);
        Assert.Equal(tripId, result.Id);
        _innerRepoMock.Verify(r => r.GetByIdAsync(tripId, It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.SetAsync(
            $"trip:active:{tripId}",
            It.IsAny<byte[]>(),
            It.Is<DistributedCacheEntryOptions>(opt => opt.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(30)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateWithOutboxAsync_SavesToInnerRepoAndInvalidatesCache()
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            Status = TripStatus.EnRoute
        };
        var history = new TripStatusHistory { TripId = trip.Id, ToStatus = TripStatus.EnRoute };

        await _cachedRepo.UpdateWithOutboxAsync(trip, history);

        _innerRepoMock.Verify(r => r.UpdateWithOutboxAsync(trip, history, null, It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.RemoveAsync($"trip:active:{trip.Id}", It.IsAny<CancellationToken>()), Times.Once);
    }
}
