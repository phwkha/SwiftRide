using TripService.Application.DTOs;

namespace TripService.Application.Services;

public interface ITripAppService
{
    Task<ApiResponse<TripDto>> CreateTripAsync(
        Guid riderId,
        CreateTripRequest request,
        CancellationToken ct = default);

    Task<ApiResponse<TripDto>> ConfirmQuoteAsync(Guid tripId, CancellationToken ct = default);
    Task<ApiResponse<TripDto>> GetActiveTripAsync(Guid userId, CancellationToken ct = default);
    Task<ApiResponse<TripDto>> GetTripByIdAsync(Guid tripId, CancellationToken ct = default);
    Task<ApiResponse<TripDto>> AcceptTripAsync(Guid tripId, Guid driverId, CancellationToken ct = default);
    Task<ApiResponse<TripDto>> RejectTripAsync(Guid tripId, Guid driverId, string? reason = null, CancellationToken ct = default);
    Task<ApiResponse<TripDto>> StartRouteAsync(Guid tripId, CancellationToken ct = default);
    Task<ApiResponse<TripDto>> PickUpAsync(Guid tripId, CancellationToken ct = default);
    Task<ApiResponse<TripDto>> DropOffAsync(
        Guid tripId,
        string paymentMethod = "Cash",
        CancellationToken ct = default);
    Task<ApiResponse<TripDto>> CancelTripAsync(Guid tripId, string? reason = null, CancellationToken ct = default);
}
