using System.Text.Json;
using SwiftRide.Contracts.Common;

namespace ApiGateway.Aggregation;

public static class TripAggregationEndpoints
{
    public static void MapTripAggregationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/trips/{tripId:guid}/overview", async (
            Guid tripId,
            HttpContext httpContext,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            var client = httpClientFactory.CreateClient("DownstreamAggregator");

            // Forward user and correlation headers downstream
            if (httpContext.Request.Headers.TryGetValue("X-User-Id", out var userId))
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-User-Id", userId.ToString());
            if (httpContext.Request.Headers.TryGetValue("X-Correlation-Id", out var correlationId))
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-Id", correlationId.ToString());

            var tripBaseUrl = configuration["Downstream:TripService"] ?? "http://localhost:5105";
            var matchingBaseUrl = configuration["Downstream:MatchingService"] ?? "http://localhost:5248";

            var tripUrl = $"{tripBaseUrl}/api/trips/{tripId}";
            using var tripResp = await client.GetAsync(tripUrl, ct);
            if (!tripResp.IsSuccessStatusCode)
            {
                return Results.StatusCode((int)tripResp.StatusCode);
            }

            var tripJson = await tripResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            JsonElement? driverLocation = null;

            if (tripJson.TryGetProperty("data", out var tripData) &&
                tripData.TryGetProperty("driverId", out var driverIdProp) &&
                driverIdProp.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(driverIdProp.GetString()))
            {
                var driverId = driverIdProp.GetString();
                var locUrl = $"{matchingBaseUrl}/api/drivers/{driverId}/location";
                try
                {
                    using var locResp = await client.GetAsync(locUrl, ct);
                    if (locResp.IsSuccessStatusCode)
                    {
                        var locJson = await locResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                        if (locJson.TryGetProperty("data", out var locData))
                            driverLocation = locData;
                    }
                }
                catch
                {
                    // Fallback gracefully if driver location service is unreachable
                }
            }

            return Results.Ok(ApiResponse<object>.Ok(new
            {
                Trip = tripJson.GetProperty("data"),
                DriverLocation = driverLocation
            }, "Aggregated trip overview fetched successfully"));
        }).RequireAuthorization();
    }
}
