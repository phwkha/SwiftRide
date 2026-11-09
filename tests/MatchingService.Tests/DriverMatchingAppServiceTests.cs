using MassTransit;
using MatchingService.Application.Services;
using MatchingService.Domain.Interfaces;
using Moq;
using Xunit;

namespace MatchingService.Tests;

public sealed class DriverMatchingAppServiceTests
{
    private readonly Mock<IDriverRepository> _driverRepositoryMock = new();
    private readonly Mock<IPublishEndpoint> _publishEndpointMock = new();

    [Fact]
    public async Task UpdateAvailabilityAsync_WhenDriverAvailableAndNotReserved_ReturnsTrue()
    {
        var driverId = "driver-123";
        _driverRepositoryMock
            .Setup(r => r.UpdateAvailabilityAsync(driverId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new DriverMatchingAppService(_driverRepositoryMock.Object, _publishEndpointMock.Object);

        var result = await service.UpdateAvailabilityAsync(driverId, true);

        Assert.True(result);
    }

    [Fact]
    public async Task UpdateAvailabilityAsync_WhenDriverReservedAndTriesAvailable_ThrowsInvalidOperationException()
    {
        var driverId = "driver-123";
        // When driver is reserved, UpdateAvailabilityAsync returns false because ReservedTripId == null does not match
        _driverRepositoryMock
            .Setup(r => r.UpdateAvailabilityAsync(driverId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _driverRepositoryMock
            .Setup(r => r.ExistsAsync(driverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new DriverMatchingAppService(_driverRepositoryMock.Object, _publishEndpointMock.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAvailabilityAsync(driverId, true));

        Assert.Equal("Tài xế đang được giữ cho một chuyến đi khác.", exception.Message);
    }

    [Fact]
    public async Task TryLockDriverForTripAsync_WhenRedisNull_ReturnsTrueByDefault()
    {
        var service = new DriverMatchingAppService(_driverRepositoryMock.Object, _publishEndpointMock.Object);
        var result = await service.TryLockDriverForTripAsync(Guid.NewGuid(), Guid.NewGuid());
        Assert.True(result);
    }

    [Fact]
    public async Task TryLockDriverForTripAsync_WhenRedisConfigured_SetsLockWith15sTTL()
    {
        var redisMock = new Mock<StackExchange.Redis.IConnectionMultiplexer>();
        var dbMock = new Mock<StackExchange.Redis.IDatabase>();
        redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);

        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();

        dbMock
            .Setup(d => d.StringSetAsync(
                It.Is<StackExchange.Redis.RedisKey>(k => k == $"matching:lock:driver:{driverId}"),
                It.Is<StackExchange.Redis.RedisValue>(v => v == tripId.ToString()),
                TimeSpan.FromSeconds(15),
                StackExchange.Redis.When.NotExists))
            .ReturnsAsync(true);

        var service = new DriverMatchingAppService(_driverRepositoryMock.Object, _publishEndpointMock.Object, redisMock.Object);

        var locked = await service.TryLockDriverForTripAsync(driverId, tripId);

        Assert.True(locked);
        dbMock.Verify(d => d.StringSetAsync(
            It.Is<StackExchange.Redis.RedisKey>(k => k == $"matching:lock:driver:{driverId}"),
            It.Is<StackExchange.Redis.RedisValue>(v => v == tripId.ToString()),
            TimeSpan.FromSeconds(15),
            StackExchange.Redis.When.NotExists), Times.Once);
    }
}
