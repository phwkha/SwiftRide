using MatchingService.Application.DTOs;
using MatchingService.Application.Services;

namespace MatchingService.Api;

public static class MatchingEndpoints
{
    private const string FallbackRiderId = "11111111-1111-1111-1111-111111111111";

    public static IEndpointRouteBuilder MapMatchingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api");

        group.MapPost("/matching/quote", async (TripQuoteRequest request, HttpContext http, PricingAppService service, CancellationToken ct) =>
        {
            try
            {
                var riderId = http.Request.Headers["X-User-Id"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(riderId))
                {
                    riderId = FallbackRiderId;
                }

                var response = await service.CreateQuoteAsync(request, riderId, ct);
                return Results.Ok(ApiResponse<TripQuoteResponse>.Success(response, "Quote generated successfully", http.TraceIdentifier));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ApiResponse<TripQuoteResponse>.Fail(400, "VALIDATION_ERROR", ex.Message, ex.ParamName, http.TraceIdentifier));
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(ApiResponse<TripQuoteResponse>.Fail(404, "PRICING_NOT_FOUND", ex.Message, "vehicleType", http.TraceIdentifier));
            }
        }).WithName("CreateTripQuote");

        group.MapPost("/matching/find-drivers", async (FindNearbyDriversRequest request, HttpContext http, DriverMatchingAppService service, CancellationToken ct) =>
        {
            try
            {
                var response = await service.FindNearbyDriversAsync(request, ct);
                return Results.Ok(ApiResponse<IReadOnlyList<NearbyDriverDto>>.Success(response, "Nearby drivers fetched successfully", http.TraceIdentifier));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ApiResponse<IReadOnlyList<NearbyDriverDto>>.Fail(400, "VALIDATION_ERROR", ex.Message, ex.ParamName, http.TraceIdentifier));
            }
        }).WithName("FindNearbyDrivers");

        group.MapGet("/drivers/{id}/location", async (string id, HttpContext http, DriverMatchingAppService service, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var driverId))
            {
                return Results.BadRequest(ApiResponse<DriverLocationDto>.Fail(400, "INVALID_DRIVER_ID", "driver id phải là GUID.", "id", http.TraceIdentifier));
            }

            var response = await service.GetDriverLocationAsync(driverId.ToString(), ct);
            return response is null
                ? Results.NotFound(ApiResponse<DriverLocationDto>.Fail(404, "DRIVER_NOT_FOUND", "Không tìm thấy tài xế hoặc vị trí chưa được cập nhật.", traceId: http.TraceIdentifier))
                : Results.Ok(ApiResponse<DriverLocationDto>.Success(response, "Driver location fetched successfully", http.TraceIdentifier));
        }).WithName("GetDriverLocation");

        group.MapPut("/drivers/{id}/location", async (string id, DriverLocationUpdateRequest request, HttpContext http, DriverMatchingAppService service, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var driverId))
            {
                return Results.BadRequest(ApiResponse<OperationResponse>.Fail(400, "INVALID_DRIVER_ID", "driver id phải là GUID.", "id", http.TraceIdentifier));
            }

            try
            {
                var updated = await service.UpdateDriverLocationAsync(driverId.ToString(), request, ct);
                return updated
                    ? Results.Ok(ApiResponse<OperationResponse>.Success(new OperationResponse("Success"), "Location updated", http.TraceIdentifier))
                    : Results.NotFound(ApiResponse<OperationResponse>.Fail(404, "DRIVER_NOT_FOUND", "Không tìm thấy tài xế.", "id", http.TraceIdentifier));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ApiResponse<OperationResponse>.Fail(400, "VALIDATION_ERROR", ex.Message, ex.ParamName, http.TraceIdentifier));
            }
        }).WithName("UpdateDriverLocation");

        group.MapPut("/drivers/{id}/availability", async (string id, DriverAvailabilityUpdateRequest request, HttpContext http, DriverMatchingAppService service, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var driverId))
            {
                return Results.BadRequest(ApiResponse<OperationResponse>.Fail(400, "INVALID_DRIVER_ID", "driver id phải là GUID.", "id", http.TraceIdentifier));
            }

            try
            {
                var updated = await service.UpdateAvailabilityAsync(driverId.ToString(), request.IsAvailable, ct);
                return updated
                    ? Results.Ok(ApiResponse<OperationResponse>.Success(new OperationResponse("Success"), "Availability updated", http.TraceIdentifier))
                    : Results.NotFound(ApiResponse<OperationResponse>.Fail(404, "DRIVER_NOT_FOUND", "Không tìm thấy tài xế.", "id", http.TraceIdentifier));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ApiResponse<OperationResponse>.Fail(409, "DRIVER_RESERVED", ex.Message, "isAvailable", http.TraceIdentifier));
            }
        }).WithName("UpdateDriverAvailability");

        return endpoints;
    }
}
