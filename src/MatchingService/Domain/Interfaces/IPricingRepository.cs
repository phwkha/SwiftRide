using MatchingService.Domain.Entities;

namespace MatchingService.Domain.Interfaces;

public interface IPricingRepository
{
    Task<PricingConfig?> GetActivePricingConfigAsync(string vehicleType, DateTime asOfUtc, CancellationToken ct = default);
    Task<SurgeZone?> GetActiveSurgeZoneAsync(decimal latitude, decimal longitude, DateTime asOfUtc, CancellationToken ct = default);
    Task SaveQuoteAsync(TripQuote quote, CancellationToken ct = default);
}
