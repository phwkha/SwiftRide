using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MatchingService.Domain.Entities;

public sealed class MatchingClosure
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string TripId { get; set; } = string.Empty;

    [BsonElement("closed_through_attempt")]
    public int ClosedThroughAttempt { get; set; }

    [BsonElement("expires_at")]
    public DateTime ExpiresAt { get; set; }
}
