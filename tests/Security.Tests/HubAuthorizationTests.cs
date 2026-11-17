using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripService.Api.Hubs;
using TripService.Domain.Entities;
using TripService.Domain.Interfaces;

namespace Security.Tests;

public class HubAuthorizationTests
{
    [Theory]
    [InlineData("anonymous")]
    [InlineData("forged")]
    [InlineData("expired")]
    public async Task Negotiate_RejectsUntrustedIdentity(string mode)
    {
        await using var app = await StartAsync(new Trip());
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-User-Id", TestApp.Driver.ToString());
        client.DefaultRequestHeaders.Add("X-User-Role", "Driver");
        if (mode != "anonymous")
            client.DefaultRequestHeaders.Authorization = new("Bearer", TestApp.Token(TestApp.Driver, "Driver",
                secret: mode == "forged" ? TestApp.Secret + "forged" : null, expired: mode == "expired"));
        var response = await client.PostAsync("/hubs/trips/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Negotiate_AcceptsValidJwt()
    {
        await using var app = await StartAsync(new Trip());
        using var client = TestApp.Client(app, TestApp.Rider, "Rider");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/hubs/trips/negotiate?negotiateVersion=1", null)).StatusCode);
    }

    [Theory]
    [InlineData("Driver", true, true)]
    [InlineData("Driver", false, false)]
    [InlineData("Rider", true, false)]
    [InlineData("Admin", true, false)]
    public async Task DriverRoom_RequiresOwnDriverIdentity(string role, bool own, bool allowed)
    {
        await using var app = await StartAsync(new Trip());
        var room = own ? TestApp.Driver : TestApp.Other;
        await InvokeAsync(app, TestApp.Driver, role, "JoinDriverRoom", room.ToString().ToUpperInvariant(), allowed);
        await InvokeAsync(app, TestApp.Driver, role, "LeaveDriverRoom", room.ToString(), allowed);
    }

    [Theory]
    [InlineData("Rider", true, false, true)]
    [InlineData("Driver", false, true, true)]
    [InlineData("Rider", false, false, false)]
    [InlineData("Driver", false, false, false)]
    [InlineData("Driver", true, false, false)]
    [InlineData("Admin", true, true, false)]
    public async Task TripRoom_RequiresOwnerOrAssignedDriver(string role, bool owner, bool assigned, bool allowed)
    {
        var trip = new Trip { RiderId = owner ? TestApp.Other : TestApp.Rider, DriverId = assigned ? TestApp.Other : TestApp.Driver };
        await using var app = await StartAsync(trip);
        await InvokeAsync(app, TestApp.Other, role, "JoinTripRoom", trip.Id.ToString(), allowed);
    }

    [Fact]
    public async Task TripRoom_RejectsUnknownAndMalformedIds()
    {
        await using var app = await StartAsync(new Trip());
        foreach (var id in new[] { "not-a-guid", Guid.Empty.ToString(), Guid.NewGuid().ToString() })
            await InvokeAsync(app, TestApp.Rider, "Rider", "JoinTripRoom", id, false);
    }

    private static Task<WebApplication> StartAsync(Trip trip)
    {
        var reader = new Mock<ITripReader>();
        reader.Setup(r => r.GetByIdAsync(trip.Id, It.IsAny<CancellationToken>())).ReturnsAsync(trip);
        return TestApp.StartAsync(services =>
        {
            services.AddSignalR();
            services.AddSingleton(reader.Object);
        }, app => app.MapHub<TripHub>("/hubs/trips", o => o.CloseOnAuthenticationExpiration = true).RequireAuthorization());
    }

    private static async Task InvokeAsync(WebApplication app, Guid user, string role, string method, string id, bool allowed)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ct = timeout.Token;
        using var ws = await app.GetTestServer().CreateWebSocketClient().ConnectAsync(
            new Uri("ws://localhost/hubs/trips?access_token=" + TestApp.Token(user, role)), ct);
        await SendAsync(ws, "{\"protocol\":\"json\",\"version\":1}", ct);
        await ReadAsync(ws, ct);
        await SendAsync(ws, JsonSerializer.Serialize(new { type = 1, invocationId = "1", target = method, arguments = new[] { id } }), ct);
        var completion = await ReadAsync(ws, ct);
        Assert.Equal(3, completion.GetProperty("type").GetInt32());
        Assert.Equal(!allowed, completion.TryGetProperty("error", out _));
        if (allowed && method == "JoinTripRoom")
        {
            await SendAsync(ws, JsonSerializer.Serialize(new { type = 1, invocationId = "2", target = "LeaveTripRoom", arguments = new[] { id } }), ct);
            Assert.False((await ReadAsync(ws, ct)).TryGetProperty("error", out _));
        }
    }

    private static Task SendAsync(WebSocket ws, string json, CancellationToken ct)
        => ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json + '\u001e')), WebSocketMessageType.Text, true, ct);

    private static async Task<JsonElement> ReadAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var text = new StringBuilder();
        do
        {
            var received = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            text.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));
        } while (!text.ToString().Contains('\u001e'));
        return JsonDocument.Parse(text.ToString().Split('\u001e')[0]).RootElement.Clone();
    }
}
