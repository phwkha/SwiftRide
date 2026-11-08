using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SwiftRide.Contracts.Common;

namespace ApiGateway.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").RequireRateLimiting("auth");

        // POST /api/auth/login (SD 01)
        group.MapPost("/login", (LoginRequest req, InMemoryIdentityStore store, JwtTokenService jwt) =>
        {
            var user = store.ValidateCredentials(req.Email, req.Password);
            if (user is null)
            {
                return Results.Json(
                    ApiResponse<LoginResponse>.Fail(401, "INVALID_CREDENTIALS", "Email hoặc mật khẩu không chính xác"),
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!string.IsNullOrWhiteSpace(req.Role) &&
                !string.Equals(user.Role, req.Role.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(
                    ApiResponse<LoginResponse>.Fail(403, "FORBIDDEN", $"Tài khoản không có quyền truy cập với vai trò {req.Role}"),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var accessToken = jwt.GenerateAccessToken(user);
            var refreshToken = store.StoreRefreshToken(user.Id);
            var userDto = new UserDto(user.Id, user.Email, user.Role, user.FullName);
            var response = new LoginResponse(accessToken, refreshToken, jwt.AccessTokenExpiresInSeconds, userDto);

            return Results.Ok(ApiResponse<LoginResponse>.Ok(response, "Đăng nhập thành công"));
        }).AllowAnonymous();

        // POST /api/auth/refresh (SD 02)
        group.MapPost("/refresh", (RefreshRequest req, InMemoryIdentityStore store, JwtTokenService jwt) =>
        {
            var (userId, isValid) = store.ValidateRefreshToken(req.RefreshToken);
            if (!isValid)
            {
                return Results.Json(
                    ApiResponse<LoginResponse>.Fail(401, "UNAUTHORIZED", "Phiên đăng nhập đã hết hạn"),
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = store.GetById(userId);
            if (user is null)
            {
                return Results.Json(
                    ApiResponse<LoginResponse>.Fail(401, "UNAUTHORIZED", "Không tìm thấy người dùng"),
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            store.RevokeRefreshToken(req.RefreshToken);
            var newAccessToken = jwt.GenerateAccessToken(user);
            var newRefreshToken = store.StoreRefreshToken(user.Id);
            var userDto = new UserDto(user.Id, user.Email, user.Role, user.FullName);
            var response = new LoginResponse(newAccessToken, newRefreshToken, jwt.AccessTokenExpiresInSeconds, userDto);

            return Results.Ok(ApiResponse<LoginResponse>.Ok(response, "Làm mới token thành công"));
        }).AllowAnonymous();

        // GET /api/auth/me (SD 01)
        group.MapGet("/me", (HttpContext ctx, InMemoryIdentityStore store) =>
        {
            var userIdStr = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? ctx.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                            ?? ctx.User.FindFirstValue("sub");

            if (!Guid.TryParse(userIdStr, out var userId))
            {
                return Results.Json(
                    ApiResponse<UserDto>.Fail(401, "UNAUTHORIZED", "Không xác định được danh tính người dùng"),
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = store.GetById(userId);
            if (user is null)
            {
                return Results.Json(
                    ApiResponse<UserDto>.Fail(404, "USER_NOT_FOUND", "Không tìm thấy thông tin người dùng"),
                    statusCode: StatusCodes.Status404NotFound);
            }

            var userDto = new UserDto(user.Id, user.Email, user.Role, user.FullName);
            return Results.Ok(ApiResponse<UserDto>.Ok(userDto, "Lấy thông tin người dùng thành công"));
        }).RequireAuthorization();
    }
}
