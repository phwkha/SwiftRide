using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MatchingService.Domain.Entities;

public class PricingConfig
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("vehicle_type")]
    public string VehicleType { get; set; } = "Bike"; // Bike, Car4, Car7

    /// <summary>
    /// c_d: Base distance rate (VND/km)
    /// </summary>
    [BsonElement("base_distance_rate")]
    public decimal BaseDistanceRate { get; set; } = 10000m;

    /// <summary>
    /// c_t: Base time rate (VND/minute)
    /// </summary>
    [BsonElement("base_time_rate")]
    public decimal BaseTimeRate { get; set; } = 1000m;

    /// <summary>
    /// c_m: Base toll/surcharge fee (VND)
    /// </summary>
    [BsonElement("base_toll_fee")]
    public decimal BaseTollFee { get; set; } = 0m;

    /// <summary>
    /// T: Tax rate (e.g. 0.10 for 10% VAT)
    /// </summary>
    [BsonElement("tax_rate")]
    public decimal TaxRate { get; set; } = 0.10m;

    /// <summary>
    /// η: Promo/discount multiplier (e.g. 0.9 for 10% discount, 1.0 for no discount)
    /// </summary>
    [BsonElement("promo_multiplier")]
    public decimal PromoMultiplier { get; set; } = 1.0m;

    [BsonElement("is_active")]
    public bool IsActive { get; set; } = true;

    [BsonElement("effective_from")]
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
}
