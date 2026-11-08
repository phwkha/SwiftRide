namespace MatchingService.Domain.Pricing;

public sealed class FareCalculator
{
    public decimal Calculate(PricingContext context, IPricingStrategy strategy)
    {
        var price = Math.Round(strategy.GetPrice(context), 0);
        if (price < 0)
        {
            throw new InvalidOperationException("Gia khong hop le (am)");
        }

        return price;
    }
}
