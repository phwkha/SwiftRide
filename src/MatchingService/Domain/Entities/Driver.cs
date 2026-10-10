using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver.GeoJsonObjectModel;

namespace MatchingService.Domain.Entities;

public class Driver
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("driver_id")]
    public string DriverId { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("phone")]
    public string Phone { get; set; } = string.Empty;

    [BsonElement("vehicle")]
    public VehicleInfo Vehicle { get; set; } = new();

    [BsonElement("is_active")]
    public bool IsActive { get; set; } = true;

    [BsonElement("is_available")]
    public bool IsAvailable { get; set; } = true;

    [BsonElement("rating")]
    public double Rating { get; set; } = 5.0;

    /// <summary>
    /// GeoJSON Point (Longitude, Latitude) for 2dsphere indexing and spatial queries
    /// </summary>
    [BsonElement("location")]
    public GeoJsonPoint<GeoJson2DGeographicCoordinates>? Location { get; set; }

    [BsonElement("last_location_updated_at")]
    public DateTime LastLocationUpdatedAt { get; set; } = DateTime.UtcNow;
}

public class VehicleInfo
{
    [BsonElement("make")]
    public string Make { get; set; } = string.Empty;

    [BsonElement("model")]
    public string Model { get; set; } = string.Empty;

    [BsonElement("license_plate")]
    public string LicensePlate { get; set; } = string.Empty;

    [BsonElement("type")]
    public string Type { get; set; } = "Bike"; // Bike, Car4, Car7
}
