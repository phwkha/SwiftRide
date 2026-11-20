using System.Text;
using System.Threading.RateLimiting;
using System.Net;
using System.Security.Claims;
using ApiGateway.Aggregation;
using ApiGateway.Auth;
using ApiGateway.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SwiftRide.Contracts.Common;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry Observability
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(options => options.RecordException = true)
            .AddHttpClientInstrumentation()
            .AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter("SwiftRide.Metrics")
            .AddOtlpExporter();
    });

// 1. CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration
            .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length > 0)
        {
            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
        else
        {
            policy.SetIsOriginAllowed(_ => true)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

// 2. JWT Authentication
var jwtSection = builder.Configuration.GetSection("Jwt");
var secret = JwtSecretConfiguration.GetRequiredSecret(jwtSection);
var key = Encoding.UTF8.GetBytes(secret);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"] ?? "SwiftRide.Gateway",
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"] ?? "SwiftRide.Client",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ClockSkew = TimeSpan.Zero,
            RoleClaimType = "role",
            NameClaimType = "name"
        };

        // Support JWT in WebSocket query string for SignalR
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Trust forwarded client addresses only from configured proxy peers.
var trustedProxies = builder.Configuration.GetSection("ForwardedHeaders:TrustedProxies").Get<string[]>() ?? [];
if (trustedProxies.Length > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var proxy in trustedProxies)
            options.KnownProxies.Add(IPAddress.Parse(proxy));
    });
}

// 3. Rate limiting by authenticated user or remote address.
static string GetPartitionKey(HttpContext httpContext)
{
    var userId = httpContext.User.Identity?.IsAuthenticated == true
        ? httpContext.User.FindFirstValue("sub") ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
        : null;

    return Guid.TryParse(userId, out var id) && id != Guid.Empty
        ? $"user:{id:D}"
        : $"ip:{httpContext.Connection.RemoteIpAddress}";
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        var response = ApiResponse<object>.Fail(429, "RATE_LIMIT_EXCEEDED", "Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau.");
        await context.HttpContext.Response.WriteAsJsonAsync(response, cancellationToken: token);
    };

    options.AddPolicy("general", httpContext =>
    {
        var key = GetPartitionKey(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("auth", httpContext =>
    {
        var key = GetPartitionKey(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

// 4. YARP Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// 5. Auth services (Singleton for InMemory store)
builder.Services.AddSingleton<InMemoryIdentityStore>();
builder.Services.AddSingleton<JwtTokenService>(sp =>
    new JwtTokenService(builder.Configuration.GetSection("Jwt")));

// 6. Aggregation HttpClient (5-second activity timeout)
builder.Services.AddHttpClient("DownstreamAggregator", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});

var app = builder.Build();

// Middleware pipeline order
if (trustedProxies.Length > 0)
    app.UseForwardedHeaders();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<HeaderInjectionMiddleware>();

// Auth endpoints
app.MapAuthEndpoints();

// Aggregation endpoints
app.MapTripAggregationEndpoints();

// YARP reverse proxy
app.MapReverseProxy();

// Health check
app.MapGet("/", () => Results.Ok(new { Service = "ApiGateway", Status = "Running" }));

app.Run();
