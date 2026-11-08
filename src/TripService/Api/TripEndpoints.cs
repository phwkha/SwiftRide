using TripService.Application.DTOs;
using TripService.Application.Services;

namespace TripService.Api;

public static class TripEndpoints
{
    private static readonly Guid DefaultRiderId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid DefaultDriverId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static IEndpointRouteBuilder MapTripEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var trips = endpoints.MapGroup("/api/trips");

        trips.MapPost("", CreateTripAsync);
        trips.MapPost("/{id:guid}/confirm-quote", ConfirmQuoteAsync);
        trips.MapGet("/active", GetActiveTripAsync);
        trips.MapGet("/{id:guid}", GetTripByIdAsync);
        trips.MapPut("/{id:guid}/accept", AcceptTripAsync);
        trips.MapPut("/{id:guid}/reject", RejectTripAsync);
        trips.MapPut("/{id:guid}/start-route", StartRouteAsync);
        trips.MapPut("/{id:guid}/pick-up", PickUpAsync);
        trips.MapPut("/{id:guid}/drop-off", DropOffAsync);
        trips.MapPut("/{id:guid}/cancel", CancelTripAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateTripAsync(
        HttpContext httpContext,
        CreateTripRequest request,
        ITripAppService service,
        CancellationToken ct)
    {
        var user = ReadUserId(httpContext, DefaultRiderId);
        if (user.Error is not null)
            return user.Error;

        return ToResult(await service.CreateTripAsync(user.Id, request, ct));
    }

    private static async Task<IResult> ConfirmQuoteAsync(
        Guid id,
        HttpContext httpContext,
        ITripAppService service,
        CancellationToken ct)
    {
        var user = ReadUserId(httpContext, DefaultRiderId);
        if (user.Error is not null)
            return user.Error;

        return ToResult(await service.ConfirmQuoteAsync(id, ct));
    }

    private static async Task<IResult> GetActiveTripAsync(
        HttpContext httpContext,
        ITripAppService service,
        CancellationToken ct)
    {
        var user = ReadUserId(httpContext, DefaultRiderId);
        if (user.Error is not null)
            return user.Error;

        return ToResult(await service.GetActiveTripAsync(user.Id, ct));
    }

    private static async Task<IResult> GetTripByIdAsync(
        Guid id,
        HttpContext httpContext,
        ITripAppService service,
        CancellationToken ct)
    {
        var user = ReadUserId(httpContext, DefaultRiderId);
        if (user.Error is not null)
            return user.Error;

        return ToResult(await service.GetTripByIdAsync(id, ct));
    }

    private static async Task<IResult> AcceptTripAsync(
        Guid id,
        HttpContext httpContext,
        ITripAppService service,
        CancellationToken ct)
    {
        var user = ReadUserId(httpContext, DefaultDriverId);
        if (user.Error is not null)
            return user.Error;

        return ToResult(await service.AcceptTripAsync(id, user.Id, ct));
    }

    private static async Task<IResult> RejectTripAsync(
        Guid id,
        HttpContext httpContext,
        TripActionRequest? request,
        ITripAppService service,
        CancellationToken ct)
    {
        var user = ReadUserId(httpContext, DefaultDriverId);
        if (user.Error is not null)
            return user.Error;

        return ToResult(await service.RejectTripAsync(id, user.Id, request?.Reason, ct));
    }

    private static async Task<IResult> StartRouteAsync(
        Guid id,
        HttpContext httpContext,
        ITripAppService service,
        CancellationToken ct)
    {
        var user = ReadUserId(httpContext, DefaultDriverId);
        if (user.Error is not null)
            return user.Error;

        return ToResult(await service.StartRouteAsync(id, ct));
    }

    private static async Task<IResult> PickUpAsync(
        Guid id,
        HttpContext httpContext,
        ITripAppService service,
        CancellationToken ct)
    {
        var user = ReadUserId(httpContext, DefaultDriverId);
        if (user.Error is not null)
            return user.Error;

        return ToResult(await service.PickUpAsync(id, ct));
    }

    private static async Task<IResult> DropOffAsync(
        Guid id,
        HttpContext httpContext,
        TripActionRequest? request,
        ITripAppService service,
        CancellationToken ct)
    {
        var user = ReadUserId(httpContext, DefaultDriverId);
        if (user.Error is not null)
            return user.Error;

        return ToResult(await service.DropOffAsync(id, request?.PaymentMethod ?? "Cash", ct));
    }

    private static async Task<IResult> CancelTripAsync(
        Guid id,
        HttpContext httpContext,
        TripActionRequest? request,
        ITripAppService service,
        CancellationToken ct)
    {
        var user = ReadUserId(httpContext, DefaultRiderId);
        if (user.Error is not null)
            return user.Error;

        return ToResult(await service.CancelTripAsync(id, request?.Reason, ct));
    }

    private static IResult ToResult(ApiResponse<TripDto> response)
        => Results.Json(response, statusCode: response.StatusCode);

    private static (Guid Id, IResult? Error) ReadUserId(
        HttpContext httpContext,
        Guid fallback)
    {
        if (!httpContext.Request.Headers.TryGetValue("X-User-Id", out var value) ||
            string.IsNullOrWhiteSpace(value))
            return (fallback, null);

        return Guid.TryParse(value, out var userId) && userId != Guid.Empty
            ? (userId, null)
            : (Guid.Empty, ToResult(ApiResponse<TripDto>.Fail(
                400,
                "INVALID_USER_ID",
                "X-User-Id must be a non-empty GUID.")));
    }
}
