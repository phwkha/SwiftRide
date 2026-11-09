using System.Diagnostics.Metrics;
namespace SwiftRide.Contracts;
public static class BusinessMetrics
{
    private static readonly Meter Meter = new("SwiftRide.Metrics");
    public static readonly Counter<long> MatchingResults = Meter.CreateCounter<long>("swiftride.matching.results", description: "Matching attempts by outcome; success means offer dispatched.");
    public static readonly Histogram<double> MatchingDuration = Meter.CreateHistogram<double>("swiftride.matching.duration", unit: "s");
    public static readonly Counter<long> PaymentResults = Meter.CreateCounter<long>("swiftride.payment.results", description: "Payment attempts by outcome, including retries.");
}
