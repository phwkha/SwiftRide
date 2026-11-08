using MatchingService.Domain.Pricing;
using Xunit;

namespace MatchingService.Tests;

public sealed class PricingStrategyTests
{
    private static readonly PricingContext SampleContext = new(
        DistanceKm: 4.8m,
        DurationMinutes: 14,
        BaseDistanceRate: 10000m,
        BaseTimeRate: 1000m,
        SurgeMultiplier: 1.3m,
        TollFee: 0m,
        PromoMultiplier: 1.0m,
        TaxRate: 0.10m);

    [Fact]
    public void StandardPricingStrategy_CalculatesDistanceAndTimeFare()
    {
        var strategy = new StandardPricingStrategy();

        var price = strategy.GetPrice(SampleContext);

        Assert.Equal(62000m, price);
    }

    [Fact]
    public void SurgePricingStrategy_AppliesSurgeAndTollToBaseFare()
    {
        var strategy = new SurgePricingStrategy(new StandardPricingStrategy());

        var price = strategy.GetPrice(SampleContext with { TollFee = 2500m });

        Assert.Equal(62000m * 1.3m + 2500m, price);
    }

    [Fact]
    public void PricingStrategyPipeline_AppliesSurgeTollPromoAndTaxFormula()
    {
        IPricingStrategy strategy = new PromoTaxPricingStrategy(
            new SurgePricingStrategy(new StandardPricingStrategy()));

        var price = strategy.GetPrice(SampleContext);

        Assert.Equal(((10000m * 4.8m + 1000m * 14) * 1.3m + 0m) * 1.0m * 1.1m, price);
        Assert.Equal(88660m, price);
    }

    [Fact]
    public void FareCalculator_RejectsNegativePrice()
    {
        var calculator = new FareCalculator();

        Assert.Throws<InvalidOperationException>(() => calculator.Calculate(
            SampleContext,
            new FixedPriceStrategy(-1m)));
    }

    private sealed class FixedPriceStrategy(decimal price) : IPricingStrategy
    {
        public decimal GetPrice(PricingContext context) => price;
    }
}
