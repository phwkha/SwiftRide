using MatchingService.Domain.Entities;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;

namespace MatchingService.Infrastructure.Data;

public sealed class MongoDbSeeder(MongoDbContext mongoContext)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedPricingConfigsAsync(cancellationToken);
        await SeedSurgeZoneAsync(cancellationToken);
        await SeedDriversAsync(cancellationToken);
    }

    private async Task SeedPricingConfigsAsync(CancellationToken cancellationToken)
    {
        var pricing = new[]
        {
            new PricingConfig { VehicleType = "Bike", BaseDistanceRate = 10000m, BaseTimeRate = 1000m, BaseTollFee = 0m, TaxRate = 0.10m, PromoMultiplier = 1.0m, IsActive = true },
            new PricingConfig { VehicleType = "Car4", BaseDistanceRate = 15000m, BaseTimeRate = 15000m, BaseTollFee = 0m, TaxRate = 0.10m, PromoMultiplier = 1.0m, IsActive = true },
            new PricingConfig { VehicleType = "Car7", BaseDistanceRate = 18000m, BaseTimeRate = 1800m, BaseTollFee = 0m, TaxRate = 0.10m, PromoMultiplier = 1.0m, IsActive = true }
        };

        foreach (var config in pricing)
        {
            config.EffectiveFrom = DateTime.UtcNow;
            var update = Builders<PricingConfig>.Update
                .SetOnInsert(p => p.BaseDistanceRate, config.BaseDistanceRate)
                .SetOnInsert(p => p.BaseTimeRate, config.BaseTimeRate)
                .SetOnInsert(p => p.BaseTollFee, config.BaseTollFee)
                .SetOnInsert(p => p.TaxRate, config.TaxRate)
                .SetOnInsert(p => p.PromoMultiplier, config.PromoMultiplier)
                .SetOnInsert(p => p.IsActive, config.IsActive)
                .SetOnInsert(p => p.EffectiveFrom, config.EffectiveFrom);

            await mongoContext.PricingConfigs.UpdateOneAsync(
                p => p.VehicleType == config.VehicleType,
                update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken);
        }
    }

    private async Task SeedSurgeZoneAsync(CancellationToken cancellationToken)
    {
        var coordinates = new[]
        {
            new GeoJson2DGeographicCoordinates(106.690, 10.770),
            new GeoJson2DGeographicCoordinates(106.705, 10.770),
            new GeoJson2DGeographicCoordinates(106.705, 10.785),
            new GeoJson2DGeographicCoordinates(106.690, 10.785),
            new GeoJson2DGeographicCoordinates(106.690, 10.770)
        };
        var polygon = GeoJson.Polygon<GeoJson2DGeographicCoordinates>(coordinates);
        var zone = new SurgeZone
        {
            ZoneName = "Khu vực Chợ Bến Thành - Quận 1",
            SurgeMultiplier = 1.3m,
            IsActive = true,
            Boundary = polygon,
            ExpiresAt = DateTime.UtcNow.AddYears(10)
        };
        var update = Builders<SurgeZone>.Update
            .SetOnInsert(z => z.SurgeMultiplier, zone.SurgeMultiplier)
            .SetOnInsert(z => z.IsActive, zone.IsActive)
            .SetOnInsert(z => z.Boundary, zone.Boundary)
            .SetOnInsert(z => z.ExpiresAt, zone.ExpiresAt);

        await mongoContext.SurgeZones.UpdateOneAsync(
            z => z.ZoneName == zone.ZoneName,
            update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken);
    }

    private async Task SeedDriversAsync(CancellationToken cancellationToken)
    {
        var drivers = new[]
        {
            new Driver { DriverId = "22222222-2222-2222-2222-222222222222", Name = "Nguyễn Văn Tài Xế 1", Vehicle = new VehicleInfo { Type = "Bike" }, IsActive = true, IsAvailable = true, Location = Point(106.6990, 10.7755) },
            new Driver { DriverId = "33333333-3333-3333-3333-333333333333", Name = "Trần Văn Tài Xế 2", Vehicle = new VehicleInfo { Type = "Bike" }, IsActive = true, IsAvailable = true, Location = Point(106.7025, 10.7782) },
            new Driver { DriverId = "44444444-4444-4444-4444-444444444444", Name = "Lê Văn Tài Xế 3", Vehicle = new VehicleInfo { Type = "Car4" }, IsActive = true, IsAvailable = true, Location = Point(106.7010, 10.7760) }
        };

        foreach (var driver in drivers)
        {
            var update = Builders<Driver>.Update
                .Set(d => d.Name, driver.Name)
                .Set(d => d.Vehicle, driver.Vehicle)
                .SetOnInsert(d => d.IsActive, driver.IsActive)
                .SetOnInsert(d => d.IsAvailable, driver.IsAvailable)
                .SetOnInsert(d => d.ReservedTripId, null)
                .SetOnInsert(d => d.Location, driver.Location)
                .SetOnInsert(d => d.LastLocationUpdatedAt, DateTime.UtcNow)
                .SetOnInsert(d => d.Bearing, driver.Bearing)
                .SetOnInsert(d => d.Speed, driver.Speed);
            await mongoContext.Drivers.UpdateOneAsync(
                d => d.DriverId == driver.DriverId,
                update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken);
        }
    }

    private static GeoJsonPoint<GeoJson2DGeographicCoordinates> Point(double longitude, double latitude)
        => GeoJson.Point(GeoJson.Geographic(longitude, latitude));
}
