using MatchingService.Domain.Entities;
using MongoDB.Driver;

namespace MatchingService.Infrastructure.Data;

public class MongoDbContext
{
    private readonly IMongoDatabase _database;

    public MongoDbContext(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MongoDbConnection")
            ?? configuration["MongoDbSettings:ConnectionString"]
            ?? "mongodb://localhost:27017";

        var databaseName = configuration["MongoDbSettings:DatabaseName"] ?? "MatchDB";

        var client = new MongoClient(connectionString);
        _database = client.GetDatabase(databaseName);
    }

    public IMongoDatabase Database => _database;

    public IMongoCollection<Driver> Drivers => _database.GetCollection<Driver>("Drivers");
    public IMongoCollection<PricingConfig> PricingConfigs => _database.GetCollection<PricingConfig>("PricingConfigs");
    public IMongoCollection<SurgeZone> SurgeZones => _database.GetCollection<SurgeZone>("SurgeZones");
    public IMongoCollection<TripQuote> TripQuotes => _database.GetCollection<TripQuote>("TripQuotes");
    public IMongoCollection<CancelledTrip> CancelledTrips => _database.GetCollection<CancelledTrip>("CancelledTrips");

    /// <summary>
    /// Ensures geospatial, lookup, and TTL indexes exist on collections.
    /// </summary>
    public async Task InitializeIndexesAsync()
    {
        // 1. Driver 2dsphere index on location
        var driverLocationIndex = Builders<Driver>.IndexKeys.Geo2DSphere(d => d.Location);
        await Drivers.Indexes.CreateOneAsync(new CreateIndexModel<Driver>(driverLocationIndex));

        // 2. Driver unique index on driver_id
        var driverIdIndex = Builders<Driver>.IndexKeys.Ascending(d => d.DriverId);
        await Drivers.Indexes.CreateOneAsync(new CreateIndexModel<Driver>(
            driverIdIndex, new CreateIndexOptions { Unique = true }));

        // 3. Driver compound index for active & available search
        var driverAvailIndex = Builders<Driver>.IndexKeys
            .Ascending(d => d.IsActive)
            .Ascending(d => d.IsAvailable)
            .Ascending(d => d.Vehicle.Type);
        await Drivers.Indexes.CreateOneAsync(new CreateIndexModel<Driver>(driverAvailIndex));

        // 4. Driver reservation lookup for cancellation/completion events without a driver id
        var driverReservationIndex = Builders<Driver>.IndexKeys.Ascending(d => d.ReservedTripId);
        await Drivers.Indexes.CreateOneAsync(new CreateIndexModel<Driver>(driverReservationIndex));

        // 5. SurgeZones 2dsphere index on boundary
        var zoneIndex = Builders<SurgeZone>.IndexKeys.Geo2DSphere(z => z.Boundary);
        await SurgeZones.Indexes.CreateOneAsync(new CreateIndexModel<SurgeZone>(zoneIndex));

        // 6. TripQuotes TTL index for auto-cleanup
        var quoteTtlIndex = Builders<TripQuote>.IndexKeys.Ascending(q => q.ExpiresAt);
        await TripQuotes.Indexes.CreateOneAsync(new CreateIndexModel<TripQuote>(
            quoteTtlIndex, new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }));

        // 7. PricingConfigs compound index for active vehicle type search
        var pricingIndex = Builders<PricingConfig>.IndexKeys
            .Ascending(p => p.VehicleType)
            .Ascending(p => p.IsActive);
        await PricingConfigs.Indexes.CreateOneAsync(new CreateIndexModel<PricingConfig>(pricingIndex));

        // 8. Cancelled-trip tombstones prevent delayed matching events from dispatching cancelled trips
        var cancelledTripTtlIndex = Builders<CancelledTrip>.IndexKeys.Ascending(trip => trip.ExpiresAt);
        await CancelledTrips.Indexes.CreateOneAsync(new CreateIndexModel<CancelledTrip>(
            cancelledTripTtlIndex, new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }));
    }
}
