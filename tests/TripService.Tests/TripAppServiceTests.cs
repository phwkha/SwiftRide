using System.Text.Json;
using Moq;
using SwiftRide.Contracts.Events;
using TripService.Application.DTOs;
using TripService.Application.Services;
using TripService.Domain.Entities;
using TripService.Domain.Enums;
using TripService.Domain.Interfaces;
using Xunit;

namespace TripService.Tests;

public class TripAppServiceTests
{
    private static readonly Guid RiderId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DriverId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task ConfirmQuoteAsync_UnrelatedUser_CannotWriteOrPublish()
    {
        var trip = CreateTrip(TripStatus.Requested);
        var writer = new Mock<ITripWriter>();
        var service = new TripAppService(writer.Object, ReaderFor(trip).Object);

        var result = await service.ConfirmQuoteAsync(trip.Id, Guid.NewGuid());

        Assert.Equal(403, result.StatusCode);
        Assert.False(trip.FareConfirmed);
        Assert.Empty(writer.Invocations);
    }

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111", 200)]
    [InlineData("22222222-2222-2222-2222-222222222222", 200)]
    [InlineData("33333333-3333-3333-3333-333333333333", 403)]
    [InlineData("00000000-0000-0000-0000-000000000000", 403)]
    public async Task GetTripByIdAsync_OnlyParticipantsCanRead(string caller, int status)
    {
        var trip = CreateTrip(TripStatus.DriverAccepted);
        trip.DriverId = DriverId;
        var service = new TripAppService(new Mock<ITripWriter>().Object, ReaderFor(trip).Object);
        var result = await service.GetTripByIdAsync(trip.Id, Guid.Parse(caller));
        Assert.Equal(status, result.StatusCode);
        if (status == 403) Assert.Null(result.Data);
    }

    [Fact]
    public async Task ConfirmQuoteAsync_WritesTripRequestedEventToOutbox()
    {
        var trip = CreateTrip(TripStatus.Requested);
        var writer = new Mock<ITripWriter>();
        var reader = new Mock<ITripReader>();
        reader.Setup(x => x.GetByIdAsync(trip.Id, It.IsAny<CancellationToken>())).ReturnsAsync(trip);
        var service = new TripAppService(writer.Object, reader.Object);
        OutboxMessage? capturedOutbox = null;
        writer
            .Setup(x => x.UpdateWithOutboxAsync(
                trip,
                It.IsAny<TripStatusHistory>(),
                It.IsAny<OutboxMessage>(),
                It.IsAny<CancellationToken>()))
            .Callback<Trip, TripStatusHistory, OutboxMessage, CancellationToken>(
                (_, _, outbox, _) => capturedOutbox = outbox);

        var response = await service.ConfirmQuoteAsync(trip.Id, RiderId);

        Assert.Equal(200, response.StatusCode);
        writer.Verify(x => x.UpdateWithOutboxAsync(
            trip,
            It.Is<TripStatusHistory>(h => h.ToStatus == TripStatus.Requested),
            It.IsAny<OutboxMessage>(),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(capturedOutbox);
        Assert.Equal(nameof(TripRequestedEvent), capturedOutbox.EventType);
        Assert.Equal(
            trip.Id,
            JsonSerializer.Deserialize<TripRequestedEvent>(
                capturedOutbox.Payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!.TripId);
    }

    [Fact]
    public async Task AcceptTripAsync_TransitionsTripAndWritesEvent()
    {
        var trip = CreateTrip(TripStatus.Requested);
        var writer = new Mock<ITripWriter>();
        var reader = ReaderFor(trip);
        var service = new TripAppService(writer.Object, reader.Object);

        var response = await service.AcceptTripAsync(trip.Id, DriverId);

        Assert.Equal(TripStatus.DriverAccepted, trip.Status);
        Assert.Equal(DriverId, trip.DriverId);
        Assert.Equal(200, response.StatusCode);
        writer.Verify(x => x.UpdateWithOutboxAsync(
            trip,
            It.Is<TripStatusHistory>(h => h.FromStatus == TripStatus.Requested && h.ToStatus == TripStatus.DriverAccepted),
            It.Is<OutboxMessage>(m => m.EventType == nameof(TripAcceptedEvent)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectTripAsync_BelowLimit_WritesRetryMatchingEvent()
    {
        var trip = CreateTrip(TripStatus.Requested);
        var writer = new Mock<ITripWriter>();
        var service = new TripAppService(writer.Object, ReaderFor(trip).Object);

        var response = await service.RejectTripAsync(trip.Id, DriverId, "busy");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(TripStatus.Requested, trip.Status);
        writer.Verify(x => x.UpdateWithOutboxesAsync(
            trip,
            It.IsAny<TripStatusHistory>(),
            It.Is<IReadOnlyCollection<OutboxMessage>>(messages =>
                messages.Any(m => m.EventType == nameof(DriverRejectedOfferEvent)) &&
                messages.Any(m => m.EventType == nameof(RetryMatchingCommand))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelTripAsync_RequestedTrip_WritesCancellationEvent()
    {
        var trip = CreateTrip(TripStatus.Requested);
        var writer = new Mock<ITripWriter>();
        var service = new TripAppService(writer.Object, ReaderFor(trip).Object);

        var response = await service.CancelTripAsync(trip.Id, RiderId, "changed plans");

        Assert.Equal(TripStatus.Cancelled, trip.Status);
        Assert.Equal(200, response.StatusCode);
        writer.Verify(x => x.UpdateWithOutboxAsync(
            trip,
            It.Is<TripStatusHistory>(h => h.ToStatus == TripStatus.Cancelled),
            It.Is<OutboxMessage>(m => m.EventType == nameof(TripCancelledEvent)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelTripAsync_WrongCaller_ReturnsForbidden()
    {
        var trip = CreateTrip(TripStatus.Requested);
        var writer = new Mock<ITripWriter>();
        var service = new TripAppService(writer.Object, ReaderFor(trip).Object);
        var intruderId = Guid.NewGuid();

        var response = await service.CancelTripAsync(trip.Id, intruderId, "malicious cancel");

        Assert.Equal(403, response.StatusCode);
        Assert.Equal("FORBIDDEN_TRIP_ACCESS", response.Errors?.FirstOrDefault()?.Code);
        Assert.Equal(TripStatus.Requested, trip.Status);
    }

    [Fact]
    public async Task DropOffAsync_PickedUpTrip_WritesPaymentEvent()
    {
        var trip = CreateTrip(TripStatus.PickedUp);
        trip.DriverId = DriverId;
        var writer = new Mock<ITripWriter>();
        var service = new TripAppService(writer.Object, ReaderFor(trip).Object);

        var response = await service.DropOffAsync(trip.Id, DriverId, "Wallet");

        Assert.Equal(TripStatus.DroppedOff, trip.Status);
        Assert.Equal(200, response.StatusCode);
        writer.Verify(x => x.UpdateWithOutboxAsync(
            trip,
            It.Is<TripStatusHistory>(h => h.ToStatus == TripStatus.DroppedOff),
            It.Is<OutboxMessage>(m => m.EventType == nameof(TripDroppedOffEvent)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DropOffAsync_WrongDriver_ReturnsForbidden()
    {
        var trip = CreateTrip(TripStatus.PickedUp);
        trip.DriverId = DriverId;
        var writer = new Mock<ITripWriter>();
        var service = new TripAppService(writer.Object, ReaderFor(trip).Object);
        var wrongDriverId = Guid.NewGuid();

        var response = await service.DropOffAsync(trip.Id, wrongDriverId, "Wallet");

        Assert.Equal(403, response.StatusCode);
        Assert.Equal("FORBIDDEN_TRIP_ACCESS", response.Errors?.FirstOrDefault()?.Code);
    }

    private static Mock<ITripReader> ReaderFor(Trip trip)
    {
        var reader = new Mock<ITripReader>();
        reader.Setup(x => x.GetByIdAsync(trip.Id, It.IsAny<CancellationToken>())).ReturnsAsync(trip);
        return reader;
    }

    private static Trip CreateTrip(TripStatus status)
        => new()
        {
            RiderId = RiderId,
            Status = status,
            PickupAddress = "A",
            DropoffAddress = "B",
            EstimatedFare = 100
        };
}
