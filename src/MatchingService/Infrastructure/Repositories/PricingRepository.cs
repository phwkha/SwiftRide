using MatchingService.Domain.Entities;
using MatchingService.Domain.Interfaces;
using MatchingService.Infrastructure.Data;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;

namespace MatchingService.Infrastructure.Repositories;

public sealed class PricingRepository(MongoDbContext mongoContext) : IPricingRepository
{
    public async Task<PricingConfig?> GetActivePricingConfigAsync(string vehicleType, DateTime asOfUtc, CancellationToken ct = default)
        => await mongoContext.PricingConfigs
            .Find(p => p.VehicleType == vehicleType && p.IsActive && p.EffectiveFrom <= asOfUtc)
            .SortByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

    public async Task<SurgeZone?> GetActiveSurgeZoneAsync(decimal latitude, decimal longitude, DateTime asOfUtc, CancellationToken ct = default)
    {
        var pickupPoint = GeoJson.Point(GeoJson.Geographic((double)longitude, (double)latitude));
        var zoneFilter = Builders<SurgeZone>.Filter.And(
            Builders<SurgeZone>.Filter.Eq(zone => zone.IsActive, true),
            Builders<SurgeZone>.Filter.Gt(zone => zone.ExpiresAt, asOfUtc),
            Builders<SurgeZone>.Filter.GeoIntersects(zone => zone.Boundary, pickupPoint));

        return await mongoContext.SurgeZones.Find(zoneFilter).FirstOrDefaultAsync(ct);
    }

    public Task SaveQuoteAsync(TripQuote quote, CancellationToken ct = default)
        => mongoContext.TripQuotes.InsertOneAsync(quote, cancellationToken: ct);
}
