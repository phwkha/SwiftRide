namespace MatchingService.Domain.Pricing;

public sealed record PricingContext(
    decimal DistanceKm,
    int DurationMinutes,
    decimal BaseDistanceRate,
    decimal BaseTimeRate,
    decimal SurgeMultiplier,
    decimal TollFee,
    decimal PromoMultiplier,
    decimal TaxRate);

public interface IPricingStrategy
{
    decimal GetPrice(PricingContext context);
}
