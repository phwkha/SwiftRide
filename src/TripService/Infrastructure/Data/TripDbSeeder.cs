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

            QuoteId = Guid.Parse(
                "6a9e1e3e-4fa0-459f-bb24-9b2f6efc8721"),

            Status = TripStatus.Requested,

            PickupLatitude = 10.762622m,
            PickupLongitude = 106.660172m,
            PickupAddress = "Trường Đại học Sài Gòn",

            DropoffLatitude = 10.776530m,
            DropoffLongitude = 106.700981m,
            DropoffAddress = "Chợ Bến Thành",

            EstimatedFare = 65000m,
            DistanceKm = 4.8m,
            EstimatedDurationMinutes = 14,

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
}