using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MatchingService.Domain.Entities;

public class TripQuote
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("rider_id")]
    public string RiderId { get; set; } = string.Empty;

    [BsonElement("vehicle_type")]
    public string VehicleType { get; set; } = "Bike";

    [BsonElement("distance_km")]
    public decimal DistanceKm { get; set; }

    [BsonElement("estimated_duration_minutes")]
    public int EstimatedDurationMinutes { get; set; }

    [BsonElement("pricing_breakdown")]
    public PricingBreakdown Breakdown { get; set; } = new();

    [BsonElement("total_fare")]
    public decimal TotalFare { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("expires_at")]
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(10);
}

public class PricingBreakdown
{
    [BsonElement("distance_cost")]
    public decimal DistanceCost { get; set; }

    [BsonElement("time_cost")]
    public decimal TimeCost { get; set; }

    [BsonElement("surge_multiplier")]
    public decimal SurgeMultiplier { get; set; } = 1.0m;

    [BsonElement("toll_fee")]
    public decimal TollFee { get; set; }

    [BsonElement("discount_rate")]
    public decimal DiscountRate { get; set; } = 1.0m;

    [BsonElement("tax_rate")]
    public decimal TaxRate { get; set; } = 0.10m;
}
