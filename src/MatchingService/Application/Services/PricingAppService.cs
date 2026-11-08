using MatchingService.Application.DTOs;
using MatchingService.Domain.Entities;
using MatchingService.Domain.Interfaces;
using MatchingService.Domain.Pricing;

namespace MatchingService.Application.Services;

public sealed class PricingAppService(IPricingRepository pricingRepository, FareCalculator fareCalculator, IPricingStrategy standardPricing)
{
    public async Task<TripQuoteResponse> CreateQuoteAsync(TripQuoteRequest request, string riderId, CancellationToken cancellationToken = default)
    {
        ValidateCoordinates(request.PickupLatitude, request.PickupLongitude, "pickup");
        ValidateCoordinates(request.DropoffLatitude, request.DropoffLongitude, "dropoff");
        ValidateVehicleType(request.VehicleType);

        var now = DateTime.UtcNow;
        var pricing = await pricingRepository.GetActivePricingConfigAsync(request.VehicleType, now, cancellationToken);

        if (pricing is null)
        {
            throw new KeyNotFoundException($"Không tìm thấy biểu phí đang hoạt động cho {request.VehicleType}.");
        }

        var surgeZone = await pricingRepository.GetActiveSurgeZoneAsync(request.PickupLatitude, request.PickupLongitude, now, cancellationToken);
        var surgeMultiplier = surgeZone?.SurgeMultiplier ?? 1.0m;

        var distanceKm = CalculateRoadDistanceKm(
            request.PickupLatitude, request.PickupLongitude,
            request.DropoffLatitude, request.DropoffLongitude);
        var durationMinutes = Math.Max(5, (int)Math.Ceiling(distanceKm / 0.4m));

        var context = new PricingContext(
            distanceKm,
            durationMinutes,
            pricing.BaseDistanceRate,
            pricing.BaseTimeRate,
            surgeMultiplier,
            pricing.BaseTollFee,
            pricing.PromoMultiplier,
            pricing.TaxRate);
        IPricingStrategy strategy = new PromoTaxPricingStrategy(new SurgePricingStrategy(standardPricing));
        var totalFare = fareCalculator.Calculate(context, strategy);
        var expiresAt = now.AddMinutes(10);
        var quote = new TripQuote
        {
            Id = Guid.NewGuid().ToString(),
            RiderId = riderId,
            VehicleType = request.VehicleType,
            DistanceKm = distanceKm,
            EstimatedDurationMinutes = durationMinutes,
            Breakdown = new PricingBreakdown
            {
                DistanceCost = pricing.BaseDistanceRate * distanceKm,
                TimeCost = pricing.BaseTimeRate * durationMinutes,
                SurgeMultiplier = surgeMultiplier,
                TollFee = pricing.BaseTollFee,
                DiscountRate = pricing.PromoMultiplier,
                TaxRate = pricing.TaxRate
            },
            TotalFare = totalFare,
            CreatedAt = now,
            ExpiresAt = expiresAt
        };

        await pricingRepository.SaveQuoteAsync(quote, cancellationToken);
        return new TripQuoteResponse(
            quote.Id,
            quote.DistanceKm,
            quote.EstimatedDurationMinutes,
            new PricingBreakdownDto(
                quote.Breakdown.DistanceCost,
                quote.Breakdown.TimeCost,
                quote.Breakdown.SurgeMultiplier,
                quote.Breakdown.TollFee,
                quote.Breakdown.DiscountRate,
                quote.Breakdown.TaxRate),
            quote.TotalFare,
            quote.ExpiresAt);
    }

    public static decimal CalculateRoadDistanceKm(decimal pickupLatitude, decimal pickupLongitude, decimal dropoffLatitude, decimal dropoffLongitude)
        => HaversineKm(pickupLatitude, pickupLongitude, dropoffLatitude, dropoffLongitude) * 1.3m;

    public static decimal HaversineKm(decimal latitude1, decimal longitude1, decimal latitude2, decimal longitude2)
    {
        const double earthRadiusKm = 6371.0;
        var lat1 = DegreesToRadians((double)latitude1);
        var lat2 = DegreesToRadians((double)latitude2);
        var deltaLatitude = DegreesToRadians((double)(latitude2 - latitude1));
        var deltaLongitude = DegreesToRadians((double)(longitude2 - longitude1));
        var a = Math.Pow(Math.Sin(deltaLatitude / 2), 2)
                + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(deltaLongitude / 2), 2);
        var haversine = Math.Clamp(a, 0d, 1d);
        var distance = earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(haversine), Math.Sqrt(1 - haversine));
        return (decimal)distance;
    }

    public static void ValidateCoordinates(decimal latitude, decimal longitude, string fieldPrefix)
    {
        if (latitude is < -90m or > 90m)
        {
            throw new ArgumentException("Vĩ độ phải nằm trong khoảng từ -90 đến 90.", $"{fieldPrefix}Latitude");
        }

        if (longitude is < -180m or > 180m)
        {
            throw new ArgumentException("Kinh độ phải nằm trong khoảng từ -180 đến 180.", $"{fieldPrefix}Longitude");
        }
    }

    public static void ValidateVehicleType(string vehicleType)
    {
        if (vehicleType is not ("Bike" or "Car4" or "Car7"))
        {
            throw new ArgumentException("vehicleType phải là Bike, Car4 hoặc Car7.", nameof(vehicleType));
        }
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}
