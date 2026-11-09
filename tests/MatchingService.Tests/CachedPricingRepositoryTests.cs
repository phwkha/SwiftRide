using System.Text;
using System.Text.Json;
using MatchingService.Domain.Entities;
using MatchingService.Domain.Interfaces;
using MatchingService.Infrastructure.Repositories;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MatchingService.Tests;

public class CachedPricingRepositoryTests
{
    private readonly Mock<IPricingRepository> _innerRepoMock = new();
    private readonly Mock<IDistributedCache> _cacheMock = new();
    private readonly CachedPricingRepository _repository;

    public CachedPricingRepositoryTests()
    {
        _repository = new CachedPricingRepository(
            _innerRepoMock.Object,
            _cacheMock.Object,
            NullLogger<CachedPricingRepository>.Instance);
    }

    [Fact]
    public async Task GetActivePricingConfigAsync_WhenCacheHit_ReturnsCachedAndDoesNotCallInner()
    {
        var config = new PricingConfig
        {
            Id = "config-1",
            VehicleType = "Bike",
            BaseDistanceRate = 5000,
            BaseTimeRate = 500,
            IsActive = true
        };
        var json = JsonSerializer.Serialize(config);
        var bytes = Encoding.UTF8.GetBytes(json);

        _cacheMock
            .Setup(c => c.GetAsync("matching:pricing:config:Bike", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        var result = await _repository.GetActivePricingConfigAsync("Bike", DateTime.UtcNow);

        Assert.NotNull(result);
        Assert.Equal("Bike", result.VehicleType);
        Assert.Equal(5000, result.BaseDistanceRate);
        _innerRepoMock.Verify(r => r.GetActivePricingConfigAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetActivePricingConfigAsync_WhenCacheMiss_CallsInnerAndSetsCache()
    {
        var config = new PricingConfig
        {
            Id = "config-2",
            VehicleType = "Car4",
            BaseDistanceRate = 12000,
            BaseTimeRate = 1000,
            IsActive = true
        };

        _cacheMock
            .Setup(c => c.GetAsync("matching:pricing:config:Car4", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        _innerRepoMock
            .Setup(r => r.GetActivePricingConfigAsync("Car4", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var result = await _repository.GetActivePricingConfigAsync("Car4", DateTime.UtcNow);

        Assert.NotNull(result);
        Assert.Equal("Car4", result.VehicleType);
        _innerRepoMock.Verify(r => r.GetActivePricingConfigAsync("Car4", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.SetAsync(
            "matching:pricing:config:Car4",
            It.IsAny<byte[]>(),
            It.Is<DistributedCacheEntryOptions>(opt => opt.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(60)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveQuoteAsync_SavesToInnerAndStoresInCacheWith5MinTTL()
    {
        var quote = new TripQuote
        {
            Id = "quote-123",
            RiderId = "rider-1",
            VehicleType = "Bike",
            TotalFare = 45000
        };

        await _repository.SaveQuoteAsync(quote);

        _innerRepoMock.Verify(r => r.SaveQuoteAsync(quote, It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.SetAsync(
            "matching:quote:quote-123",
            It.IsAny<byte[]>(),
            It.Is<DistributedCacheEntryOptions>(opt => opt.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(5)),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
