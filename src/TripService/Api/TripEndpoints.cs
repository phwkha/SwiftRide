using SwiftRide.Security;
using TripService.Application.DTOs;
using TripService.Application.Services;

namespace TripService.Api;

public static class TripEndpoints
{
    public static IEndpointRouteBuilder MapTripEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var trips = endpoints.MapGroup("/api/trips").RequireAuthorization();

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
        var user = ReadUserId(httpContext);
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
        var user = ReadUserId(httpContext);
        if (user.Error is not null)
            return user.Error;

        return ToResult(await service.ConfirmQuoteAsync(id, user.Id, ct));
    }

    private static async Task<IResult> GetActiveTripAsync(
        HttpContext httpContext,
        ITripAppService service,
        CancellationToken ct)
    {
        var user = ReadUserId(httpContext);
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
        var user = ReadUserId(httpContext);
        if (user.Error is not null)
            return user.Error;

        return ToResult(await service.GetTripByIdAsync(id, user.Id, ct));
    }

    private static async Task<IResult> AcceptTripAsync(
        Guid id,
        HttpContext httpContext,
        ITripAppService service,
        CancellationToken ct)
    {
        var user = ReadUserId(httpContext);
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
        var user = ReadUserId(httpContext);
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
        var user = ReadUserId(httpContext);
        if (user.Error is not null)
            return user.Error;

        return ToResult(await service.StartRouteAsync(id, user.Id, ct));
    }

    private static async Task<IResult> PickUpAsync(
        Guid id,
        HttpContext httpContext,
        ITripAppService service,
        CancellationToken ct)
    {
        var user = ReadUserId(httpContext);
        if (user.Error is not null)
            return user.Error;

        return ToResult(await service.PickUpAsync(id, user.Id, ct));
    }

    private static async Task<IResult> DropOffAsync(
        Guid id,
        HttpContext httpContext,
        TripActionRequest? request,
        ITripAppService service,
        CancellationToken ct)
    {
        var user = ReadUserId(httpContext);
        if (user.Error is not null)
            return user.Error;

        return ToResult(await service.DropOffAsync(id, user.Id, request?.PaymentMethod ?? "Wallet", ct));
    }

    private static async Task<IResult> CancelTripAsync(
        Guid id,
        HttpContext httpContext,
        TripActionRequest? request,
        ITripAppService service,
        CancellationToken ct)
    {
        var user = ReadUserId(httpContext);
        if (user.Error is not null)
            return user.Error;

        return ToResult(await service.CancelTripAsync(id, user.Id, request?.Reason, ct));
    }

    private static IResult ToResult(ApiResponse<TripDto> response)
        => Results.Json(response, statusCode: response.StatusCode);

    private static (Guid Id, IResult? Error) ReadUserId(HttpContext httpContext)
    {
        var userId = httpContext.User.AuthenticatedUserId();
        return userId is Guid id
            ? (id, null)
            : (Guid.Empty, ToResult(ApiResponse<TripDto>.Fail(
                401, "UNAUTHORIZED", "Missing authenticated user identity.")));
    }
}
