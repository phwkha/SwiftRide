using MatchingService.Api;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace MatchingService.Tests;

public class DriverRequestAuthorizationTests
{
    [Theory]
    [InlineData("Driver", "22222222-2222-2222-2222-222222222222", null)]
    [InlineData("Rider", "22222222-2222-2222-2222-222222222222", 403)]
    [InlineData("Driver", "33333333-3333-3333-3333-333333333333", 403)]
    [InlineData("Admin", "22222222-2222-2222-2222-222222222222", 403)]
    [InlineData("Driver", "", 401)]
    [InlineData("", "22222222-2222-2222-2222-222222222222", 401)]
    public void RequireDriver_EnforcesRoleAndIdentity(string role, string userId, int? expected)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-User-Id"] = userId;
        http.Request.Headers["X-User-Role"] = role;
        var result = DriverRequestAuthorization.RequireDriver(http, Guid.Parse("22222222-2222-2222-2222-222222222222"));
        if (expected is null) Assert.Null(result);
        else Assert.Equal(expected, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }
}
