namespace MatchingService.Domain.Pricing;

public sealed class StandardPricingStrategy : IPricingStrategy
{
    public decimal GetPrice(PricingContext context)
        => (context.BaseDistanceRate * context.DistanceKm)
           + (context.BaseTimeRate * context.DurationMinutes);
}
