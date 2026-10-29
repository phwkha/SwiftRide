using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Middleware;

public class HeaderInjectionMiddleware
{
    private readonly RequestDelegate _next;

    public HeaderInjectionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 1. Sanitize untrusted user headers sent from the outside client
        context.Request.Headers.Remove("X-User-Id");
        context.Request.Headers.Remove("X-User-Role");
        context.Request.Headers.Remove("X-User-Email");

        // 2. Always ensure X-Correlation-Id exists on the request
        if (!context.Request.Headers.TryGetValue("X-Correlation-Id", out var correlationHeader)
            || string.IsNullOrWhiteSpace(correlationHeader))
        {
            var generatedId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
            context.Request.Headers["X-Correlation-Id"] = generatedId;
        }

        var correlationId = context.Request.Headers["X-Correlation-Id"].ToString();

        // Echo X-Correlation-Id back to the caller in response headers
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Correlation-Id"] = correlationId;
            return Task.CompletedTask;
        });

        // 3. If authenticated, inject verified user headers for downstream microservices
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                         ?? context.User.FindFirstValue("sub");
            var role = context.User.FindFirstValue(ClaimTypes.Role)
                       ?? context.User.FindFirstValue("role");
            var email = context.User.FindFirstValue(ClaimTypes.Email)
                        ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Email)
                        ?? context.User.FindFirstValue("email");

            if (!string.IsNullOrEmpty(userId))
            {
                context.Request.Headers["X-User-Id"] = userId;
            }

            if (!string.IsNullOrEmpty(role))
            {
                context.Request.Headers["X-User-Role"] = role;
            }

            if (!string.IsNullOrEmpty(email))
            {
                context.Request.Headers["X-User-Email"] = email;
            }
        }

        await _next(context);
    }
}
