using MassTransit;
using MatchingService.Application.Services;
using MatchingService.Domain.Interfaces;
using MatchingService.Domain.Entities;
using Moq;
using SwiftRide.Contracts.Events;
using Xunit;

namespace MatchingService.Tests;

public sealed class DriverMatchingAppServiceTests
{
    private readonly Mock<IDriverRepository> _driverRepositoryMock = new();
    private readonly Mock<ITopicProducer<string, DriverLocationUpdatedEvent>> _locationProducer = new();
    private readonly Mock<ITopicProducer<string, MatchingFailedEvent>> _matchingFailedProducer = new();
    private readonly Mock<ITopicProducer<string, TripOfferProposedEvent>> _tripOfferProducer = new();

    private DriverMatchingAppService CreateService()
        => new(_driverRepositoryMock.Object, _locationProducer.Object, _matchingFailedProducer.Object, _tripOfferProducer.Object);

    [Fact]
    public async Task MatchTripAsync_WhenNoDriver_RecordsFailureAndDuration()
    {
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
        var service = CreateService();

        await service.MatchTripAsync(Guid.NewGuid(), 10m, 106m, "Bike", [], 1, DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Contains("failure", outcomes);
        Assert.Contains(durations, duration => duration >= 0);
    }

    [Fact]
    public async Task MatchTripAsync_RedeliveryProposesTheSameReservation()
    {
        var tripId = Guid.NewGuid();
        var driver = Guid.NewGuid();
        _driverRepositoryMock.Setup(r => r.GetReservedDriverAsync(tripId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Driver { DriverId = driver.ToString(), ReservedAttempt = 1 });
        var service = CreateService();

        await service.MatchTripAsync(tripId, 10m, 106m, "Bike", [], 1, DateTimeOffset.UtcNow.AddMinutes(1));

        _driverRepositoryMock.Verify(r => r.ReserveNearestDriverAsync(It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _tripOfferProducer.Verify(p => p.Produce(tripId.ToString("D"), new TripOfferProposedEvent(tripId, driver, 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MatchTripAsync_RetryClosesEarlierAttemptBeforeReservingNextDriver()
    {
        var tripId = Guid.NewGuid();
        var rejected = Guid.NewGuid();
        var next = Guid.NewGuid();
        var released = false;
        _driverRepositoryMock.Setup(r => r.ReleaseReservationsThroughAsync(tripId.ToString(), 1, It.IsAny<CancellationToken>()))
            .Callback(() => released = true)
            .Returns(Task.CompletedTask);
        _driverRepositoryMock.Setup(r => r.ReserveNearestDriverAsync(10m, 106m, "Bike",
                It.Is<IEnumerable<string>>(ids => ids.Contains(rejected.ToString())),
                tripId.ToString(), 2, It.IsAny<CancellationToken>()))
            .Callback(() => Assert.True(released))
            .ReturnsAsync(new Driver { DriverId = next.ToString(), ReservedAttempt = 2 });

        var service = CreateService();
        await service.MatchTripAsync(tripId, 10m, 106m, "Bike", [rejected], 2, DateTimeOffset.UtcNow.AddMinutes(1));

        _tripOfferProducer.Verify(p => p.Produce(tripId.ToString("D"), new TripOfferProposedEvent(tripId, next, 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAvailabilityAsync_WhenDriverAvailableAndNotReserved_ReturnsTrue()
    {
        var driverId = "driver-123";
        _driverRepositoryMock
            .Setup(r => r.UpdateAvailabilityAsync(driverId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService();

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

        var service = CreateService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAvailabilityAsync(driverId, true));

        Assert.Equal("Tài xế đang được giữ cho một chuyến đi khác.", exception.Message);
    }

    [Fact]
    public async Task MatchTripAsync_WhenClosedConcurrently_ReleasesOnlyThroughTheCurrentAttempt()
    {
        var tripId = Guid.NewGuid();
        var ct = CancellationToken.None;
        _driverRepositoryMock.SetupSequence(r => r.GetClosedThroughAttemptAsync(tripId.ToString(), ct))
            .ReturnsAsync(0)
            .ReturnsAsync(1);
        _driverRepositoryMock.Setup(r => r.ReserveNearestDriverAsync(10m, 106m, "Bike",
                It.IsAny<IEnumerable<string>>(), tripId.ToString(), 1, ct))
            .ReturnsAsync(new Driver { DriverId = Guid.NewGuid().ToString(), ReservedAttempt = 1 });

        var service = CreateService();
        await service.MatchTripAsync(tripId, 10m, 106m, "Bike", [], 1, DateTimeOffset.UtcNow.AddMinutes(1), ct);

        _driverRepositoryMock.Verify(r => r.ReleaseReservationsThroughAsync(tripId.ToString(), 1, ct), Times.Once);
        Assert.Empty(_tripOfferProducer.Invocations);
        Assert.Empty(_matchingFailedProducer.Invocations);
    }
}
