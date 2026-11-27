using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaymentService.Api;
using PaymentService.Application.DTOs;
using PaymentService.Application.Services;
using PaymentService.Domain.Factories;
using PaymentService.Domain.Interfaces;
using PaymentService.Infrastructure.Data;
using PaymentService.Infrastructure.Repositories;

namespace Security.Tests;

public class PaymentAuthorizationTests
{
    private static readonly Guid TripId = Guid.NewGuid();
    private static ProcessPaymentRequest Request => new(TripId, TestApp.Rider, TestApp.Driver, 65000m, "Wallet");
    private static PaymentTripSnapshot Snapshot => new(TripId, TestApp.Rider, TestApp.Driver, 5, 65000m);

    [Theory]
    [InlineData("Rider", true, 200)]
    [InlineData("Driver", false, 403)]
    [InlineData("Rider", false, 403)]
    [InlineData("Admin", false, 200)]
    [InlineData("anonymous", true, 401)]
    public async Task Balance_RequiresOwnerOrAdmin(string role, bool own, int expected)
    {
        await using var env = await PaymentApp.CreateAsync(Snapshot);
        using var client = role == "anonymous" ? env.App.GetTestClient() : TestApp.Client(env.App, own ? TestApp.Rider : TestApp.Other, role);
        client.DefaultRequestHeaders.Add("X-User-Id", TestApp.Rider.ToString());
        client.DefaultRequestHeaders.Add("X-User-Role", "Admin");
        var response = await client.GetAsync("/api/accounts/" + TestApp.Rider);
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
    }

    [Theory]
    [InlineData("Rider", true, 200)]
    [InlineData("Rider", false, 403)]
    [InlineData("Driver", true, 403)]
    [InlineData("Admin", true, 403)]
    [InlineData("anonymous", true, 401)]
    public async Task Process_RequiresRiderOwningTrip(string role, bool owner, int expected)
    {
        await using var env = await PaymentApp.CreateAsync(Snapshot);
        using var client = role == "anonymous" ? env.App.GetTestClient() : TestApp.Client(env.App, owner ? TestApp.Rider : TestApp.Other, role);
        client.DefaultRequestHeaders.Add("X-User-Id", TestApp.Rider.ToString());
        client.DefaultRequestHeaders.Add("X-User-Role", "Rider");
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", "process-owner");
        var response = await client.PostAsJsonAsync("/api/payments/process", Request);
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        Assert.Equal(expected == 200 ? 3 : 0, await env.Db.LedgerEntries.CountAsync());
        Assert.Equal(expected == 200 ? 435000m : 500000m,
            (await env.Db.Accounts.SingleAsync(a => a.OwnerId == TestApp.Rider)).Balance);
        if (expected != 200) Assert.Empty(await env.Db.IdempotencyRecords.ToListAsync());
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("rider")]
    [InlineData("driver")]
    public async Task Process_RejectsTamperedBodyWithoutLedgerChanges(string field)
    {
        await using var env = await PaymentApp.CreateAsync(Snapshot);
        using var client = TestApp.Client(env.App, TestApp.Rider, "Rider");
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", "tampered");
        var request = field switch
        {
            "amount" => Request with { Amount = 1m },
            "rider" => Request with { RiderId = TestApp.Other },
            _ => Request with { DriverId = TestApp.Other }
        };
        var response = await client.PostAsJsonAsync("/api/payments/process", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("PAYMENT_TRIP_MISMATCH", await ErrorCodeAsync(response));
        Assert.Empty(await env.Db.Transactions.ToListAsync());
        Assert.Empty(await env.Db.LedgerEntries.ToListAsync());
        Assert.Empty(await env.Db.IdempotencyRecords.ToListAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public async Task Process_OnlyAllowsPaymentPending(int status)
    {
        await using var env = await PaymentApp.CreateAsync(Snapshot with { Status = status });
        using var client = TestApp.Client(env.App, TestApp.Rider, "Rider");
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", "wrong-state");
        var response = await client.PostAsJsonAsync("/api/payments/process", Request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("PAYMENT_NOT_PENDING", await ErrorCodeAsync(response));
        Assert.Empty(await env.Db.LedgerEntries.ToListAsync());
    }

    [Theory]
    [InlineData("Rider", true, 200)]
    [InlineData("Rider", false, 403)]
    [InlineData("Driver", true, 403)]
    [InlineData("Admin", false, 200)]
    [InlineData("anonymous", true, 401)]
    public async Task Refund_RequiresRiderOwnerOrAdmin(string role, bool owner, int expected)
    {
        await using var env = await PaymentApp.CreateAsync(Snapshot with { Status = 7 });
        var core = new PaymentAppService(new PaymentRepository(env.Db), new PaymentMethodFactory(), NullLogger<PaymentAppService>.Instance);
        var payment = await core.ProcessPaymentAsync(Request, "automatic-internal");
        using var client = role == "anonymous" ? env.App.GetTestClient() : TestApp.Client(env.App, owner ? TestApp.Rider : TestApp.Other, role);
        client.DefaultRequestHeaders.Add("X-User-Id", TestApp.Rider.ToString());
        client.DefaultRequestHeaders.Add("X-User-Role", "Admin");
        var response = await client.PostAsJsonAsync($"/api/payments/{payment.TransactionId}/refund", new { reason = "test" });
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        Assert.Equal(expected == 200 ? 6 : 3, await env.Db.LedgerEntries.CountAsync());
        Assert.Equal(expected == 200 ? 500000m : 435000m,
            (await env.Db.Accounts.SingleAsync(a => a.OwnerId == TestApp.Rider)).Balance);
    }

    [Fact]
    public async Task Process_FailsClosedWhenTripLookupFails()
    {
        await using var env = await PaymentApp.CreateAsync(Snapshot);
        env.Trips.Setup(t => t.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PaymentService.Domain.Exceptions.PaymentException("TRIP_SERVICE_UNAVAILABLE", 503, "unavailable"));
        using var client = TestApp.Client(env.App, TestApp.Rider, "Rider");
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", "trip-unavailable");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/payments/process", Request)).StatusCode);
        Assert.Empty(await env.Db.LedgerEntries.ToListAsync());
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString();
    }

    private sealed class PaymentApp(WebApplication app, PaymentDbContext db, Mock<ITripSnapshotClient> trips) : IAsyncDisposable
    {
        internal WebApplication App => app;
        internal PaymentDbContext Db => db;
        internal Mock<ITripSnapshotClient> Trips => trips;
        internal static async Task<PaymentApp> CreateAsync(PaymentTripSnapshot snapshot)
        {
            var db = new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            await PaymentDbSeeder.SeedAsync(db);
            var trips = new Mock<ITripSnapshotClient>();
            trips.Setup(t => t.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
            var app = await TestApp.StartAsync(services =>
            {
                services.AddExceptionHandler<PaymentExceptionHandler>();
                services.AddProblemDetails();
                services.AddSingleton(trips.Object);
                services.AddScoped<IPaymentRepository>(_ => new PaymentRepository(db));
                services.AddSingleton<PaymentMethodFactory>();
                services.AddScoped<PaymentAppService>();
                services.AddScoped<PaymentRequestAuthorization>();
            }, app => { app.UseExceptionHandler(); app.MapPaymentEndpoints(); });
            return new(app, db, trips);
        }
        public async ValueTask DisposeAsync() { await app.DisposeAsync(); await db.DisposeAsync(); }
    }
}
