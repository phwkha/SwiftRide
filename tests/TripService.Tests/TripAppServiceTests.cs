using System.Text.Json;
using Moq;
using SwiftRide.Contracts.Common;
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
        var service = CreateService(writer.Object, ReaderFor(trip).Object);

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
        var service = CreateService(new Mock<ITripWriter>().Object, ReaderFor(trip).Object);
        var result = await service.GetTripByIdAsync(trip.Id, Guid.Parse(caller));
        Assert.Equal(status, result.StatusCode);
        if (status == 403) Assert.Null(result.Data);
    }

    [Fact]
    public async Task ConfirmQuoteAsync_WritesTripRequestedEventToOutbox()
    {
        var trip = CreateTrip(TripStatus.Requested);
        trip.QuoteId = Guid.NewGuid();
        var quote = QuoteFor(trip);
        var writer = new Mock<ITripWriter>();
        var service = CreateService(writer.Object, ReaderFor(trip).Object, quote);
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
        Assert.Equal(quote.TotalFare, response.Data!.EstimatedFare);
        Assert.Equal(200, (await service.ConfirmQuoteAsync(trip.Id, RiderId)).StatusCode);
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
        var trip = CreateOfferedTrip();
        var writer = new Mock<ITripWriter>();
        var service = CreateService(writer.Object, ReaderFor(trip).Object);

        var response = await service.AcceptTripAsync(trip.Id, DriverId);

        Assert.Equal(TripStatus.DriverAccepted, trip.Status);
        Assert.Equal(DriverId, trip.DriverId);
        Assert.Equal(200, response.StatusCode);
        writer.Verify(x => x.UpdateWithOutboxesAsync(
            trip,
            It.Is<TripStatusHistory>(h => h.FromStatus == TripStatus.Requested && h.ToStatus == TripStatus.DriverAccepted),
            It.Is<IReadOnlyCollection<OutboxMessage>>(messages =>
                messages.Any(m => m.EventType == nameof(TripAcceptedEvent))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectTripAsync_BelowLimit_WritesRetryMatchingEvent()
    {
        var trip = CreateOfferedTrip();
        var writer = new Mock<ITripWriter>();
        var service = CreateService(writer.Object, ReaderFor(trip).Object);

        var response = await service.RejectTripAsync(trip.Id, DriverId, "busy");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(TripStatus.Requested, trip.Status);
        writer.Verify(x => x.UpdateWithOutboxesAsync(
            trip,
            It.IsAny<TripStatusHistory>(),
            It.Is<IReadOnlyCollection<OutboxMessage>>(messages =>
                messages.Any(m => m.EventType == nameof(MatchingAttemptClosedEvent)) &&
                messages.Any(m => m.EventType == nameof(RetryMatchingCommand))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelTripAsync_RequestedTrip_WritesCancellationEvent()
    {
        var trip = CreateTrip(TripStatus.Requested);
        var writer = new Mock<ITripWriter>();
        var service = CreateService(writer.Object, ReaderFor(trip).Object);

        var response = await service.CancelTripAsync(trip.Id, RiderId, "changed plans");

        Assert.Equal(TripStatus.Cancelled, trip.Status);
        Assert.Equal(200, response.StatusCode);
        writer.Verify(x => x.UpdateWithOutboxesAsync(
            trip,
            It.Is<TripStatusHistory>(h => h.ToStatus == TripStatus.Cancelled),
            It.Is<IReadOnlyCollection<OutboxMessage>>(messages =>
                messages.Any(m => m.EventType == nameof(TripCancelledEvent))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelTripAsync_WrongCaller_ReturnsForbidden()
    {
        var trip = CreateTrip(TripStatus.Requested);
        var writer = new Mock<ITripWriter>();
        var service = CreateService(writer.Object, ReaderFor(trip).Object);
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
        var service = CreateService(writer.Object, ReaderFor(trip).Object);

        trip.FareConfirmedAt = DateTimeOffset.UtcNow.AddMinutes(-20);
        var response = await service.DropOffAsync(trip.Id, DriverId, "Wallet");

        Assert.Equal(TripStatus.DroppedOff, trip.Status);
        Assert.Equal(trip.EstimatedFare, response.Data!.ActualFare);
        Assert.Equal(200, response.StatusCode);
        writer.Verify(x => x.UpdateWithOutboxesAsync(
            trip,
            It.Is<TripStatusHistory>(h => h.ToStatus == TripStatus.DroppedOff),
            It.Is<IReadOnlyCollection<OutboxMessage>>(messages =>
                messages.Any(m => m.EventType == nameof(TripDroppedOffEvent) &&
                    JsonSerializer.Deserialize<TripDroppedOffEvent>(m.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.ActualFare == trip.EstimatedFare) &&
                messages.Any(m => m.EventType == nameof(TripStatusChangedEvent))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PickupAndStart_PublishCommittedTripStatuses()
    {
        var trip = CreateTrip(TripStatus.DriverAccepted);
        trip.DriverId = DriverId;
        var writer = new Mock<ITripWriter>();
        var events = new List<TripStatusChangedEvent>();
        writer.Setup(x => x.UpdateWithOutboxAsync(trip, It.IsAny<TripStatusHistory>(), It.IsAny<OutboxMessage>(),
                It.IsAny<CancellationToken>()))
            .Callback<Trip, TripStatusHistory, OutboxMessage, CancellationToken>((_, history, message, _) =>
            {
                Assert.Equal(nameof(TripStatusChangedEvent), message.EventType);
                var status = JsonSerializer.Deserialize<TripStatusChangedEvent>(
                    message.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                Assert.Equal(history.ChangedAt, status.Timestamp);
                events.Add(status);
            });
        var service = CreateService(writer.Object, ReaderFor(trip).Object);

        Assert.Equal(200, (await service.StartRouteAsync(trip.Id, DriverId)).StatusCode);
        Assert.Equal(200, (await service.PickUpAsync(trip.Id, DriverId)).StatusCode);

        Assert.Equal(new[] { "EnRoute", "PickedUp" }, events.Select(e => e.NewStatus));
        Assert.All(events, e => Assert.Equal(DriverId, e.DriverId));
    }

    [Fact]
    public async Task DropOffAsync_WrongDriver_ReturnsForbidden()
    {
        var trip = CreateTrip(TripStatus.PickedUp);
        trip.DriverId = DriverId;
        var writer = new Mock<ITripWriter>();
        var service = CreateService(writer.Object, ReaderFor(trip).Object);
        var wrongDriverId = Guid.NewGuid();

        var response = await service.DropOffAsync(trip.Id, wrongDriverId, "Wallet");

        Assert.Equal(403, response.StatusCode);
        Assert.Equal("FORBIDDEN_TRIP_ACCESS", response.Errors?.FirstOrDefault()?.Code);
    }

    [Theory]
    [InlineData("wrong rider", 403, "FORBIDDEN_QUOTE_ACCESS")]
    [InlineData("expired", 409, "QUOTE_EXPIRED")]
    [InlineData("different route", 400, "QUOTE_MISMATCH")]
    [InlineData("different vehicle", 400, "QUOTE_MISMATCH")]
    public async Task ConfirmQuoteAsync_InvalidQuoteDoesNotStartMatching(string change, int status, string code)
    {
        var trip = CreateTrip(TripStatus.Requested);
        trip.QuoteId = Guid.NewGuid();
        var valid = QuoteFor(trip);
        var quote = change switch
        {
            "wrong rider" => valid with { RiderId = Guid.NewGuid() },
            "expired" => valid with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) },
            "different route" => valid with { DropoffLatitude = valid.DropoffLatitude + 1 },
            _ => valid with { VehicleType = "Car4" }
        };
        var writer = new Mock<ITripWriter>();
        var service = CreateService(writer.Object, ReaderFor(trip).Object, quote);

        var response = await service.ConfirmQuoteAsync(trip.Id, RiderId);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, Assert.Single(response.Errors!).Code);
        Assert.Empty(writer.Invocations);
    }

    [Fact]
    public async Task CreateTripAsync_UsesQuoteFieldsOverLegacyClientValues()
    {
        var trip = CreateTrip(TripStatus.Requested);
        trip.QuoteId = Guid.NewGuid();
        var quote = QuoteFor(trip);
        var writer = new Mock<ITripWriter>();
        var service = CreateService(writer.Object, ReaderFor(trip).Object, quote);
        var request = new CreateTripRequest(trip.QuoteId, 0, 0, "A", 0, 0, "B", 1, 1, 1);

        var result = await service.CreateTripAsync(RiderId, request);

        Assert.Equal(201, result.StatusCode);
        Assert.Equal(quote.TotalFare, result.Data!.EstimatedFare);
        Assert.Equal(quote.DistanceKm, result.Data.DistanceKm);
        Assert.Equal(quote.EstimatedDurationMinutes, result.Data.EstimatedDurationMinutes);
        Assert.False(result.Data.FareConfirmed);
    }

    [Theory]
    [InlineData(true, false, 403)]
    [InlineData(false, true, 409)]
    public async Task DriverActions_RequireTheInvitedDriverAndUnexpiredOffer(bool wrongDriver, bool expired, int status)
    {
        var trip = CreateOfferedTrip();
        if (expired)
            trip.OfferExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        var driver = wrongDriver ? Guid.NewGuid() : DriverId;
        var writer = new Mock<ITripWriter>();
        var service = CreateService(writer.Object, ReaderFor(trip).Object);

        Assert.Equal(status, (await service.AcceptTripAsync(trip.Id, driver, offerId: trip.OfferId)).StatusCode);
        Assert.Equal(status, (await service.RejectTripAsync(trip.Id, driver, offerId: trip.OfferId)).StatusCode);
        Assert.Empty(writer.Invocations);
    }

    private static TripAppService CreateService(ITripWriter writer, ITripReader reader, TripQuoteSnapshot? quote = null)
    {
        var quoteClient = new Mock<ITripQuoteClient>();
        quoteClient.Setup(client => client.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(quote);
        return new TripAppService(writer, reader, quoteClient.Object, TimeProvider.System);
    }

    private static TripQuoteSnapshot QuoteFor(Trip trip) => new(
        trip.QuoteId!.Value, trip.RiderId, trip.PickupLatitude, trip.PickupLongitude, trip.DropoffLatitude,
        trip.DropoffLongitude, trip.VehicleType, 3.25m, 12, 42000m, DateTimeOffset.UtcNow.AddMinutes(5));

    private static Trip CreateOfferedTrip()
    {
        var trip = CreateTrip(TripStatus.Requested);
        trip.FareConfirmed = true;
        trip.FareConfirmedAt = DateTimeOffset.UtcNow;
        trip.OfferId = Guid.NewGuid();
        trip.OfferedDriverId = DriverId;
        trip.OfferExpiresAt = DateTimeOffset.UtcNow.AddSeconds(15);
        trip.MatchingAttempt = 1;
        return trip;
    }

    private static Mock<ITripReader> ReaderFor(Trip trip)
    {
        var reader = new Mock<ITripReader>();
        reader.Setup(x => x.GetCurrentByIdAsync(trip.Id, It.IsAny<CancellationToken>())).ReturnsAsync(trip);
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
