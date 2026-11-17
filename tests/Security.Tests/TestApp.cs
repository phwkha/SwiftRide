using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SwiftRide.Security;

namespace Security.Tests;

internal static class TestApp
{
    internal const string Secret = "test-only-swiftride-signing-key-32-bytes-minimum";
    internal static readonly Guid Rider = Guid.Parse("11111111-1111-1111-1111-111111111111");
    internal static readonly Guid Driver = Guid.Parse("22222222-2222-2222-2222-222222222222");
    internal static readonly Guid Other = Guid.Parse("33333333-3333-3333-3333-333333333333");
    internal static readonly Guid Admin = Guid.Parse("99999999-9999-9999-9999-999999999999");

    internal static async Task<WebApplication> StartAsync(
        Action<IServiceCollection> services, Action<WebApplication> map)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = Secret });
        builder.Services.AddSwiftRideAuthentication(builder.Configuration, signalR: true);
        services(builder.Services);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        map(app);
        await app.StartAsync();
        return app;
    }

    internal static string Token(Guid user, string role, string? secret = null, bool expired = false)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken("SwiftRide.Gateway", "SwiftRide.Client",
            [new Claim("sub", user.ToString()), new Claim("role", role)],
            notBefore: now.AddMinutes(-10), expires: expired ? now.AddMinutes(-1) : now.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret ?? Secret)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    internal static HttpClient Client(WebApplication app, Guid user, string role)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(user, role));
        return client;
    }
}
