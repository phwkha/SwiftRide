using MatchingService.Api;
using Microsoft.AspNetCore.Http;
using Xunit;
using System.Security.Claims;

namespace MatchingService.Tests;

public class DriverRequestAuthorizationTests
{
    [Theory]
    [InlineData("Driver", "22222222-2222-2222-2222-222222222222", null)]
    [InlineData("Rider", "22222222-2222-2222-2222-222222222222", 403)]
    [InlineData("Driver", "33333333-3333-3333-3333-333333333333", 403)]
    [InlineData("Admin", "22222222-2222-2222-2222-222222222222", 403)]
    [InlineData("Driver", "", 401)]
    [InlineData("", "22222222-2222-2222-2222-222222222222", 403)]
    public void RequireDriver_EnforcesRoleAndIdentity(string role, string userId, int? expected)
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", userId), new Claim("role", role)], "Bearer", "name", "role"));
        var result = DriverRequestAuthorization.RequireDriver(http, Guid.Parse("22222222-2222-2222-2222-222222222222"));
        if (expected is null) Assert.Null(result);
        else Assert.Equal(expected, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }
}
