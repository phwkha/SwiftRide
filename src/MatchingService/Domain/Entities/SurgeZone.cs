using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver.GeoJsonObjectModel;

namespace MatchingService.Domain.Entities;

public class SurgeZone
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("zone_name")]
    public string ZoneName { get; set; } = string.Empty;

    /// <summary>
    /// s: Surge multiplier (e.g. 1.2, 1.5, 2.0)
    /// </summary>
    [BsonElement("surge_multiplier")]
    public decimal SurgeMultiplier { get; set; } = 1.0m;

    /// <summary>
    /// GeoJSON Polygon for 2dsphere indexing and spatial containment check
    /// </summary>
    [BsonElement("boundary")]
    public GeoJsonPolygon<GeoJson2DGeographicCoordinates>? Boundary { get; set; }

    [BsonElement("is_active")]
    public bool IsActive { get; set; } = true;

    [BsonElement("expires_at")]
    public DateTime ExpiresAt { get; set; }
}
