using System.Text.Json;
using MatchingService.Domain.Entities;
using MatchingService.Domain.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace MatchingService.Infrastructure.Repositories;

public sealed class CachedPricingRepository : IPricingRepository
{
    private readonly IPricingRepository _innerRepository;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachedPricingRepository> _logger;

    private const string PricingConfigKeyPrefix = "matching:pricing:config:";
    private const string SurgeZoneKeyPrefix = "matching:surge:";
    private const string QuoteKeyPrefix = "matching:quote:";

    public CachedPricingRepository(
        IPricingRepository innerRepository,
        IDistributedCache cache,
        ILogger<CachedPricingRepository> logger)
    {
        _innerRepository = innerRepository;
        _cache = cache;
        _logger = logger;
    }

    public async Task<PricingConfig?> GetActivePricingConfigAsync(string vehicleType, DateTime asOfUtc, CancellationToken ct = default)
    {
        var cacheKey = $"{PricingConfigKeyPrefix}{vehicleType}";

        // 1. Đọc từ Redis trước (Cache-Aside)
        var cachedData = await _cache.GetStringAsync(cacheKey, ct);
        if (!string.IsNullOrEmpty(cachedData))
        {
            _logger.LogInformation("[Redis HIT] Đọc PricingConfig từ bộ nhớ đệm.");
            return JsonSerializer.Deserialize<PricingConfig>(cachedData);
        }

        // 2. Nếu Cache Miss -> Đọc từ MongoDB
        _logger.LogInformation("[Redis MISS] Đọc PricingConfig từ MongoDB.");
        var config = await _innerRepository.GetActivePricingConfigAsync(vehicleType, asOfUtc, ct);
        if (config is not null)
        {
            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(config),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60)
                },
                ct);
        }

        return config;
    }

    public async Task<SurgeZone?> GetActiveSurgeZoneAsync(decimal latitude, decimal longitude, DateTime asOfUtc, CancellationToken ct = default)
    {
        var cacheKey = $"{SurgeZoneKeyPrefix}{Math.Round(latitude, 3)}:{Math.Round(longitude, 3)}";
        var cachedData = await _cache.GetStringAsync(cacheKey, ct);
        if (!string.IsNullOrEmpty(cachedData))
        {
            _logger.LogInformation("[Redis HIT] Đọc SurgeZone từ bộ nhớ đệm.");
            return JsonSerializer.Deserialize<SurgeZone>(cachedData);
        }

        _logger.LogInformation("[Redis MISS] Đọc SurgeZone từ MongoDB.");
        var zone = await _innerRepository.GetActiveSurgeZoneAsync(latitude, longitude, asOfUtc, ct);
        if (zone is not null)
        {
            var cacheModel = new SurgeZone
            {
                Id = zone.Id,
                ZoneName = zone.ZoneName,
                SurgeMultiplier = zone.SurgeMultiplier,
                IsActive = zone.IsActive,
                ExpiresAt = zone.ExpiresAt
            };
            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(cacheModel),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
                },
                ct);
        }

        return zone;
    }

    public async Task SaveQuoteAsync(TripQuote quote, CancellationToken ct = default)
    {
        // Lưu vào MongoDB như bình thường
        await _innerRepository.SaveQuoteAsync(quote, ct);

        // Đồng thời lưu vào Redis với TTL 5 phút để truy xuất cực nhanh khi khách xác nhận đặt xe
        var cacheKey = $"{QuoteKeyPrefix}{quote.Id}";
        await _cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(quote),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            },
            ct);
    }
}
