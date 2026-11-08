namespace MatchingService.Application.DTOs;

public sealed class TripQuoteRequest
{
    public required decimal PickupLatitude { get; init; }
    public required decimal PickupLongitude { get; init; }
    public required decimal DropoffLatitude { get; init; }
    public required decimal DropoffLongitude { get; init; }
    public required string VehicleType { get; init; }
}

public sealed record PricingBreakdownDto(
    decimal DistanceCost,
    decimal TimeCost,
    decimal SurgeMultiplier,
    decimal TollFee,
    decimal DiscountRate,
    decimal TaxRate);

public sealed record TripQuoteResponse(
    string QuoteId,
    decimal DistanceKm,
    int EstimatedDurationMinutes,
    PricingBreakdownDto PricingBreakdown,
    decimal TotalFare,
    DateTime ExpiresAt);

public sealed class FindNearbyDriversRequest
{
    public required decimal PickupLatitude { get; init; }
    public required decimal PickupLongitude { get; init; }
    public string? VehicleType { get; init; }
    public double RadiusKm { get; init; } = 5d;
    public int Limit { get; init; } = 5;
}

public sealed record NearbyDriverDto(
    string DriverId,
    double Latitude,
    double Longitude,
    double DistanceKm,
    double Rating,
    string VehicleType);

public sealed record DriverLocationDto(
    string DriverId,
    double Latitude,
    double Longitude,
    double? Bearing,
    double? Speed,
    DateTime LastLocationUpdatedAt);

public sealed class DriverLocationUpdateRequest
{
    public required decimal Latitude { get; init; }
    public required decimal Longitude { get; init; }
    public double? Bearing { get; init; }
    public double? Speed { get; init; }
    public Guid? ActiveTripId { get; init; }
}

public sealed class DriverAvailabilityUpdateRequest
{
    public required bool IsAvailable { get; init; }
}

public sealed record OperationResponse(string Status);

public sealed record ApiError(string Code, string Message, string? Field = null);

public sealed record ApiResponse<T>(
    int StatusCode,
    string Message,
    T? Data,
    object? Meta,
    IReadOnlyList<ApiError>? Errors,
    string? TraceId)
{
    public static ApiResponse<T> Success(T data, string message, string? traceId = null)
        => new(200, message, data, null, null, traceId);

    public static ApiResponse<T> Fail(int statusCode, string code, string message, string? field = null, string? traceId = null)
        => new(statusCode, message, default, null, [new ApiError(code, message, field)], traceId);
}
