using MassTransit;
using MatchingService.Application.Services;
using MatchingService.Domain.Interfaces;
using Moq;
using SwiftRide.Contracts.Events;
using Xunit;

namespace MatchingService.Tests;

public sealed class DriverMatchingAppServiceTests
{
    private readonly Mock<IDriverRepository> _driverRepositoryMock = new();
    private readonly Mock<IPublishEndpoint> _publishEndpointMock = new();

    [Fact]
    public async Task MatchTripAsync_WhenNoDriver_RecordsFailureAndDuration()
    {
        _driverRepositoryMock.Setup(r => r.GetDriversReservedForTripAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MatchingService.Domain.Entities.Driver>());
        var outcomes = new System.Collections.Concurrent.ConcurrentBag<string>();
        var durations = new System.Collections.Concurrent.ConcurrentBag<double>();
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "SwiftRide.Metrics")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
        {
            if (instrument.Name != "swiftride.matching.results") return;
            foreach (var tag in tags)
                if (tag.Key == "outcome") outcomes.Add(tag.Value!.ToString()!);
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            if (instrument.Name == "swiftride.matching.duration") durations.Add(value);
        });
        listener.Start();
        var service = new DriverMatchingAppService(_driverRepositoryMock.Object, _publishEndpointMock.Object);

        await service.MatchTripAsync(Guid.NewGuid(), 10m, 106m, "pickup", 11m, 106m, "dropoff", "Bike", 5m, 10, 30000m, null);

        Assert.Contains("failure", outcomes);
        Assert.Contains(durations, duration => duration >= 0);
    }

    [Fact]
    public async Task MatchTripAsync_WhenAlreadyReserved_DoesNotReserveOrPublishAgain()
    {
        var tripId = Guid.NewGuid();
        _driverRepositoryMock.Setup(r => r.GetDriversReservedForTripAsync(tripId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new MatchingService.Domain.Entities.Driver() });
        var service = new DriverMatchingAppService(_driverRepositoryMock.Object, _publishEndpointMock.Object);

        await service.MatchTripAsync(tripId, 10m, 106m, "pickup", 11m, 106m, "dropoff", "Bike", 5m, 10, 30000m, null);

        _driverRepositoryMock.Verify(r => r.ReserveNearestDriverAsync(It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_publishEndpointMock.Invocations);
    }

    [Fact]
    public async Task MatchTripAsync_RetryBeforeRejectionRelease_OffersToNextDriver()
    {
        var tripId = Guid.NewGuid();
        var rejected = Guid.NewGuid();
        var next = Guid.NewGuid();
        var released = false;
        _driverRepositoryMock.Setup(r => r.GetDriversReservedForTripAsync(tripId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new() { DriverId = rejected.ToString() }]);
        _driverRepositoryMock.Setup(r => r.ReleaseDriverAsync(rejected.ToString(), tripId.ToString(), It.IsAny<CancellationToken>()))
            .Callback(() => released = true)
            .Returns(Task.CompletedTask);
        _driverRepositoryMock.Setup(r => r.ReserveNearestDriverAsync(10m, 106m, "Bike",
                It.Is<IEnumerable<string>>(ids => ids.Contains(rejected.ToString())),
                tripId.ToString(), It.IsAny<CancellationToken>()))
            .Callback(() => Assert.True(released))
            .ReturnsAsync(new MatchingService.Domain.Entities.Driver { DriverId = next.ToString() });

        var service = new DriverMatchingAppService(_driverRepositoryMock.Object, _publishEndpointMock.Object);
        await service.MatchTripAsync(tripId, 10m, 106m, "pickup", 11m, 106m, "dropoff", "Bike", 5m, 10, 30000m, [rejected]);

        _driverRepositoryMock.Verify(r => r.ReleaseDriverAsync(rejected.ToString(), tripId.ToString(), It.IsAny<CancellationToken>()), Times.Once);
        _publishEndpointMock.Verify(p => p.Publish(It.Is<TripOfferDispatchedEvent>(e => e.TripId == tripId && e.DriverId == next),
            It.IsAny<CancellationToken>()), Times.Once);
    }

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
