using MatchingService.Application.DTOs;

namespace MatchingService.Api;

public static class DriverRequestAuthorization
{
    // Gateway strips client-supplied identity headers and forwards verified JWT claims.
    public static IResult? RequireDriver(HttpContext http, Guid driverId)
    {
        if (!Guid.TryParse(http.Request.Headers["X-User-Id"], out var callerId) || callerId == Guid.Empty ||
            string.IsNullOrWhiteSpace(http.Request.Headers["X-User-Role"]))
            return Results.Json(ApiResponse<OperationResponse>.Fail(401, "UNAUTHORIZED",
                "Missing verified user identity.", traceId: http.TraceIdentifier), statusCode: 401);

        if (!string.Equals(http.Request.Headers["X-User-Role"], "Driver", StringComparison.OrdinalIgnoreCase) ||
            callerId != driverId)
            return Results.Json(ApiResponse<OperationResponse>.Fail(403, "FORBIDDEN",
                "Bạn chỉ được cập nhật vị trí của chính mình với vai trò tài xế.", traceId: http.TraceIdentifier), statusCode: 403);

        return null;
    }
}
