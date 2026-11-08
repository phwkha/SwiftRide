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
}
