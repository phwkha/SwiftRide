namespace SwiftRide.Messaging;

public static class KafkaTopics
{
    public const string TripRequested = "swiftride.trip.requested.v1";
    public const string TripAccepted = "swiftride.trip.accepted.v1";
    public const string TripOfferProposed = "swiftride.trip.offer-proposed.v1";
    public const string TripOfferDispatched = "swiftride.trip.offer-dispatched.v1";
    public const string TripOfferExpired = "swiftride.trip.offer-expired.v1";
    public const string TripStatusChanged = "swiftride.trip.status-changed.v1";
    public const string TripDroppedOff = "swiftride.trip.dropped-off.v1";
    public const string TripCancelled = "swiftride.trip.cancelled.v1";
    public const string TripCompleted = "swiftride.trip.completed.v1";
    public const string MatchingRetry = "swiftride.matching.retry.v1";
    public const string MatchingFailed = "swiftride.matching.failed.v1";
    public const string MatchingAttemptClosed = "swiftride.matching.attempt-closed.v1";
    public const string DriverRejectedOffer = "swiftride.driver.rejected-offer.v1";
    public const string DriverLocationUpdated = "swiftride.driver.location-updated.v1";
    public const string PaymentSettled = "swiftride.payment.settled.v1";
    public const string PaymentFailed = "swiftride.payment.failed.v1";
    public const string PaymentRefunded = "swiftride.payment.refunded.v1";
    public const string ConsumerFailures = "swiftride.consumer-failures.v1";

    internal static IReadOnlyList<string> All { get; } =
    [
        TripRequested,
        TripAccepted,
        TripOfferProposed,
        TripOfferDispatched,
        TripOfferExpired,
        TripStatusChanged,
        TripDroppedOff,
        TripCancelled,
        TripCompleted,
        MatchingRetry,
        MatchingFailed,
        MatchingAttemptClosed,
        DriverRejectedOffer,
        DriverLocationUpdated,
        PaymentSettled,
        PaymentFailed,
        PaymentRefunded,
        ConsumerFailures
    ];
}
