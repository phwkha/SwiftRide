using System.Net;
using System.Net.Http.Json;
using PaymentService.Application.Services;
using PaymentService.Domain.Exceptions;
using SwiftRide.Contracts.Common;

namespace Security.Tests;

public class TripSnapshotClientTests
{
    [Fact]
    public async Task Lookup_ForwardsBearerAndUsesTripResponse()
    {
        var tripId = Guid.NewGuid();
        var snapshot = new PaymentTripSnapshot(tripId, TestApp.Rider, TestApp.Driver, 5, 65000m);
        var handler = new Handler(request =>
        {
            Assert.Equal($"http://trip-service/api/trips/{tripId}", request.RequestUri!.ToString());
            Assert.Equal("Bearer caller-jwt", request.Headers.Authorization!.ToString());
            Assert.False(request.Headers.Contains("X-User-Id"));
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(ApiResponse<PaymentTripSnapshot>.Ok(snapshot)) };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://trip-service/") };
        Assert.Equal(snapshot, await new TripSnapshotClient(client).GetAsync(tripId, "Bearer caller-jwt", default));
    }

    [Theory]
    [InlineData(401, 401)]
    [InlineData(403, 403)]
    [InlineData(404, 404)]
    [InlineData(500, 503)]
    [InlineData(302, 503)]
    public async Task Lookup_PropagatesDenialAndFailsClosed(int downstream, int expected)
    {
        using var client = new HttpClient(new Handler(_ => new((HttpStatusCode)downstream))) { BaseAddress = new Uri("http://trip-service/") };
        var ex = await Assert.ThrowsAsync<PaymentException>(() =>
            new TripSnapshotClient(client).GetAsync(Guid.NewGuid(), "Bearer caller-jwt", default));
        Assert.Equal(expected, ex.StatusCode);
    }

    [Fact]
    public async Task Lookup_RejectsWrongTripInResponse()
    {
        using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ApiResponse<PaymentTripSnapshot>.Ok(
                new(Guid.NewGuid(), TestApp.Rider, TestApp.Driver, 5, 65000m)))
        })) { BaseAddress = new Uri("http://trip-service/") };
        var ex = await Assert.ThrowsAsync<PaymentException>(() =>
            new TripSnapshotClient(client).GetAsync(Guid.NewGuid(), "Bearer caller-jwt", default));
        Assert.Equal(503, ex.StatusCode);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(send(request));
    }
}
