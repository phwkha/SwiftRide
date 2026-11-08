using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Application.DTOs;

public record ApiError(string Code, string Message, string? Field = null);

public record ApiResponse<T>(
    int StatusCode,
    string Message,
    T? Data,
    object? Meta = null,
    List<ApiError>? Errors = null,
    string? TraceId = null)
{
    public static ApiResponse<T> Ok(T data, string message = "Success", string? traceId = null)
        => new(200, message, data, null, null, traceId);

    public static ApiResponse<T> Created(T data, string message = "Created", string? traceId = null)
        => new(201, message, data, null, null, traceId);

    public static ApiResponse<T> Fail(
        int statusCode,
        string code,
        string message,
        string? field = null,
        string? traceId = null)
        => new(statusCode, message, default, null, [new ApiError(code, message, field)], traceId);
}

public sealed record CreateTripRequest(
    Guid? QuoteId,
    decimal PickupLatitude,
    decimal PickupLongitude,
    string PickupAddress,
    decimal DropoffLatitude,
    decimal DropoffLongitude,
    string DropoffAddress,
    decimal DistanceKm,
    int EstimatedDurationMinutes,
    decimal EstimatedFare,
    string VehicleType = "Bike");

public sealed record TripActionRequest(
    string? Reason = null,
    string PaymentMethod = "Cash");

public sealed record TripDto(
    Guid Id,
    Guid RiderId,
    Guid? DriverId,
    Guid? QuoteId,
    TripStatus Status,
    decimal PickupLatitude,
    decimal PickupLongitude,
    string PickupAddress,
    decimal DropoffLatitude,
    decimal DropoffLongitude,
    string DropoffAddress,
    decimal DistanceKm,
    int EstimatedDurationMinutes,
    decimal EstimatedFare,
    decimal? ActualFare,
    bool FareConfirmed,
    string VehicleType,
    int DriverRejectionCount,
    string? ExcludedDriverIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? PickedUpAt,
    DateTimeOffset? DroppedOffAt,
    DateTimeOffset? CompletedAt,
    string? CancellationReason,
    byte[] RowVersion)
{
    public static TripDto FromEntity(Trip trip)
        => new(
            trip.Id,
            trip.RiderId,
            trip.DriverId,
            trip.QuoteId,
            trip.Status,
            trip.PickupLatitude,
            trip.PickupLongitude,
            trip.PickupAddress,
            trip.DropoffLatitude,
            trip.DropoffLongitude,
            trip.DropoffAddress,
            trip.DistanceKm,
            trip.EstimatedDurationMinutes,
            trip.EstimatedFare,
            trip.ActualFare,
            trip.FareConfirmed,
            trip.VehicleType,
            trip.DriverRejectionCount,
            trip.ExcludedDriverIds,
            trip.CreatedAt,
            trip.UpdatedAt,
            trip.AcceptedAt,
            trip.PickedUpAt,
            trip.DroppedOffAt,
            trip.CompletedAt,
            trip.CancellationReason,
            trip.RowVersion.ToArray());
}
