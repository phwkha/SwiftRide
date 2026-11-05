namespace MatchingService.Domain.Pricing;

public sealed class SurgePricingStrategy(IPricingStrategy basePricing) : IPricingStrategy
{
    private readonly IPricingStrategy _basePricing = basePricing;

    public decimal GetPrice(PricingContext context)
        => (_basePricing.GetPrice(context) * context.SurgeMultiplier) + context.TollFee;
}
