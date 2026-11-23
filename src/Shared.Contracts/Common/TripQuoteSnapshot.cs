namespace SwiftRide.Contracts.Common;

public sealed record TripQuoteSnapshot(
    Guid QuoteId,
    Guid RiderId,
    decimal? PickupLatitude,
    decimal? PickupLongitude,
    decimal? DropoffLatitude,
    decimal? DropoffLongitude,
    string VehicleType,
    decimal DistanceKm,
    int EstimatedDurationMinutes,
    decimal TotalFare,
    DateTimeOffset ExpiresAt)
{
    // TripDB stores coordinates to six fractional digits.
    public static decimal NormalizeCoordinate(decimal coordinate)
        => Math.Round(coordinate, 6, MidpointRounding.AwayFromZero);
}
