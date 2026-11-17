using MatchingService.Application.DTOs;
using SwiftRide.Security;

namespace MatchingService.Api;

public static class DriverRequestAuthorization
{
    // Use the locally validated JWT, never client-supplied identity headers.
    public static IResult? RequireDriver(HttpContext http, Guid driverId)
    {
        var callerId = http.User.AuthenticatedUserId();
        if (callerId is null)
            return Results.Json(ApiResponse<OperationResponse>.Fail(401, "UNAUTHORIZED",
                "Missing verified user identity.", traceId: http.TraceIdentifier), statusCode: 401);

        if (!http.User.IsInRole("Driver") ||
            callerId != driverId)
            return Results.Json(ApiResponse<OperationResponse>.Fail(403, "FORBIDDEN",
                "Bạn chỉ được cập nhật thông tin của chính mình với vai trò tài xế.", traceId: http.TraceIdentifier), statusCode: 403);

        return null;
    }
}
