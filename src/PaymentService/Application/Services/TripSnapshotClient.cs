using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PaymentService.Domain.Exceptions;
using SwiftRide.Contracts.Common;

namespace PaymentService.Application.Services;

public sealed record PaymentTripSnapshot(Guid Id, Guid RiderId, Guid? DriverId, int Status, decimal? ActualFare);

public interface ITripSnapshotClient
{
    Task<PaymentTripSnapshot> GetAsync(Guid tripId, string authorization, CancellationToken ct);
}

public sealed class TripSnapshotClient(HttpClient client) : ITripSnapshotClient
{
    public async Task<PaymentTripSnapshot> GetAsync(Guid tripId, string authorization, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/trips/{tripId:D}");
        if (!AuthenticationHeaderValue.TryParse(authorization, out var bearer) ||
            !string.Equals(bearer.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            throw new PaymentException(ErrorCodes.Unauthorized, 401, "Thieu JWT Bearer.");
        request.Headers.Authorization = bearer;
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new PaymentException(ErrorCodes.NotFound, 404, "Khong tim thay chuyen di.");
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new PaymentException(ErrorCodes.Forbidden, 403, "Ban khong co quyen truy cap chuyen di.");
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new PaymentException(ErrorCodes.Unauthorized, 401, "JWT khong hop le.");
            response.EnsureSuccessStatusCode();
            var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<PaymentTripSnapshot>>(ct);
            if (envelope?.Data is not { } trip || trip.Id != tripId || trip.RiderId == Guid.Empty)
                throw new JsonException("Invalid trip snapshot.");
            return trip;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException ||
                                   ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            // Fail closed: never settle money when the authoritative trip cannot be read.
            throw new PaymentException("TRIP_SERVICE_UNAVAILABLE", 503,
                "Chua the xac minh chuyen di. Vui long thu lai sau.");
        }
    }
}
