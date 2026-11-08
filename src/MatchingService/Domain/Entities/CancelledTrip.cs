using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MatchingService.Domain.Entities;

public sealed class CancelledTrip
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string TripId { get; set; } = string.Empty;

    [BsonElement("expires_at")]
    public DateTime ExpiresAt { get; set; }
}
