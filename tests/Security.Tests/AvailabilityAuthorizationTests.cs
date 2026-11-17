using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using MassTransit;
using MatchingService.Api;
using MatchingService.Application.Services;
using MatchingService.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Security.Tests;

public class AvailabilityAuthorizationTests
{
    [Theory]
    [InlineData("Driver", true, 200)]
    [InlineData("Driver", false, 403)]
    [InlineData("Rider", true, 403)]
    [InlineData("Admin", true, 403)]
    [InlineData("anonymous", true, 401)]
    public async Task Availability_RejectsWrongRoleAndSpoofedHeaders(string role, bool own, int status)
    {
        var repo = new Mock<IDriverRepository>();
        repo.Setup(r => r.UpdateAvailabilityAsync(TestApp.Driver.ToString(), true, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await using var app = await TestApp.StartAsync(services =>
        {
            services.AddSingleton(new DriverMatchingAppService(repo.Object, Mock.Of<IPublishEndpoint>()));
            services.AddTransient<PricingAppService>(_ => throw new InvalidOperationException("Unused pricing service"));
        }, app => app.MapMatchingEndpoints());
        using var client = role == "anonymous" ? app.GetTestClient() : TestApp.Client(app, own ? TestApp.Driver : TestApp.Other, role);
        client.DefaultRequestHeaders.Add("X-User-Id", TestApp.Driver.ToString());
        client.DefaultRequestHeaders.Add("X-User-Role", "Driver");
        var response = await client.PutAsJsonAsync($"/api/drivers/{TestApp.Driver}/availability", new { isAvailable = true });
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        repo.Verify(r => r.UpdateAvailabilityAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            status == 200 ? Times.Once() : Times.Never());
    }
}
