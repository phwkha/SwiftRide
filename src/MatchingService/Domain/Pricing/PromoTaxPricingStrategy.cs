namespace MatchingService.Domain.Pricing;

public sealed class PromoTaxPricingStrategy(IPricingStrategy basePricing) : IPricingStrategy
{
    private readonly IPricingStrategy _basePricing = basePricing;

    public decimal GetPrice(PricingContext context)
        => _basePricing.GetPrice(context) * context.PromoMultiplier * (1m + context.TaxRate);
}
