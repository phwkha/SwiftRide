using Microsoft.EntityFrameworkCore;
using TripService.Domain.Entities;
using TripService.Domain.Enums;

namespace TripService.Infrastructure.Data;

public static class TripDbSeeder
{
    public static async Task SeedAsync(
        TripDbContext dbContext,
        CancellationToken ct = default)
    {
        // Tạo database và các bảng nếu chưa tồn tại
        await dbContext.Database.EnsureCreatedAsync(ct);
        await EnsureQuoteAndOfferColumnsAsync(dbContext, ct);

        // Kiểm tra chuyến đi mẫu đã tồn tại chưa
        var tripId = Guid.Parse(
            "f47ac10b-58cc-4372-a567-0e02b2c3d479");

        if (await dbContext.Trips.AnyAsync(t => t.Id == tripId, ct))
        {
            return;
        }

        // Tạo chuyến đi mẫu
        var trip = new Trip
        {
            Id = tripId,
            RiderId = Guid.Parse(
                "11111111-1111-1111-1111-111111111111"),

            Status = TripStatus.Requested,

            PickupLatitude = 10.762622m,
            PickupLongitude = 106.660172m,
            PickupAddress = "Trường Đại học Sài Gòn",

            DropoffLatitude = 10.776530m,
            DropoffLongitude = 106.700981m,
            DropoffAddress = "Chợ Bến Thành",

            FareConfirmed = false,
            VehicleType = "Bike",
            DriverRejectionCount = 0,

            CreatedAt = DateTimeOffset.UtcNow
        };

        // Tạo lịch sử trạng thái ban đầu
        var history = new TripStatusHistory
        {
            TripId = tripId,
            FromStatus = null,
            ToStatus = TripStatus.Requested,
            Reason = "Seed initial trip",
            ChangedAt = DateTimeOffset.UtcNow
        };

        await dbContext.Trips.AddAsync(trip, ct);
        await dbContext.TripStatusHistories.AddAsync(history, ct);

        await dbContext.SaveChangesAsync(ct);
    }

    // EnsureCreated only creates a fresh database. Upgrade existing TripDB volumes in place.
    private static async Task EnsureQuoteAndOfferColumnsAsync(TripDbContext dbContext, CancellationToken ct)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        await dbContext.Database.ExecuteSqlRawAsync("""
            DECLARE @lockResult int;
            EXEC @lockResult = sp_getapplock
                @Resource = N'SwiftRide.TripSchema',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 15000;
            IF @lockResult < 0
                THROW 51000, 'Could not lock TripDB schema for upgrade.', 1;

            IF COL_LENGTH(N'dbo.Trips', N'FareConfirmedAt') IS NULL
            BEGIN
                ALTER TABLE dbo.Trips ADD
                    FareConfirmedAt datetimeoffset NULL,
                    MatchingAttempt int NOT NULL CONSTRAINT DF_Trips_MatchingAttempt DEFAULT (0),
                    MatchingDeadline datetimeoffset NULL,
                    OfferId uniqueidentifier NULL,
                    OfferedDriverId uniqueidentifier NULL,
                    OfferExpiresAt datetimeoffset NULL;

                INSERT INTO dbo.OutboxMessages (Id, EventType, Payload, CreatedAt)
                SELECT NEWID(), N'MatchingAttemptClosedEvent',
                    (SELECT trip.Id AS tripId, 0 AS attempt FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                    SYSDATETIMEOFFSET()
                FROM dbo.Trips AS trip
                WHERE trip.Status = N'Requested' AND trip.FareConfirmed = 1;

                UPDATE dbo.Trips SET FareConfirmed = 0 WHERE Status = N'Requested' AND FareConfirmed = 1;
            END
            """, ct);
        await transaction.CommitAsync(ct);
    }
}
