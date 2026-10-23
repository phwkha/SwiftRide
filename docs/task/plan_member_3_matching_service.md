# 📍 Kế Hoạch Phát Triển Cá Nhân — Thành Viên 3 (Dev BE 3): Matching & Pricing Service (`MatchDB`)

> **Người phụ trách:** Thành viên 3 (Dev BE 3)  
> **Nhánh Git làm việc:** `feat/matching-service`  
> **Phạm vi thư mục sở hữu độc quyền:**  
> - `src/MatchingService/`  
> - `tests/MatchingService.Tests/`  
> **Cổng dịch vụ & CSDL:** Port `:5248` — MongoDB (`localhost:27017`, Database: `MatchDB`)  
> **Sơ đồ trình tự phụ trách:** **SD 09 (Tính giá động Strategy Pattern), SD 10 (Định vị GPS 2dsphere), SD 11 (Ghép tài xế với vòng lặp thử $\le k$ lần)**.

## 1. Hiện Trạng Mã Nguồn Đã Có Sẵn & Những File Cần Tạo Thêm

Trong repo `SwiftRide.slnx`, `src/MatchingService` đã có sẵn 4 Entities (`Driver`, `PricingConfig`, `SurgeZone`, `TripQuote`) và `MongoDbContext.cs` (đã cấu hình tự động tạo đủ 6 chỉ mục `2dsphere` và `TTL`). Bạn cần phát triển tiếp theo cấu trúc sau:

```text
src/MatchingService/
├── Domain/
│   ├── Entities/
│   │   ├── Driver.cs                      # [CẬP NHẬT] Thêm Bearing và Speed
│   │   ├── PricingConfig.cs               # [ĐÃ CÓ]
│   │   ├── SurgeZone.cs                   # [ĐÃ CÓ]
│   │   └── TripQuote.cs                   # [ĐÃ CÓ]
│   └── Pricing/                           # [TẠO MỚI] Chuẩn SRP, OCP, LSP & Strategy Pattern (Mục 6 PDF)
│       ├── IPricingStrategy.cs            # [TẠO MỚI] Interface chiến lược tính giá
│       ├── StandardPricingStrategy.cs     # [TẠO MỚI] Tính cước gốc cd*d + ct*t
│       ├── SurgePricingStrategy.cs        # [TẠO MỚI] Decorator/Strategy nhân hệ số s + phí phụ cm
│       ├── PromoTaxPricingStrategy.cs     # [TẠO MỚI] Nhân khuyến mãi η * (1 + T)
│       └── FareCalculator.cs              # [TẠO MỚI] Lớp tính giá độc lập + Kiểm tra LSP (p >= 0)
├── Application/
│   ├── DTOs/MatchingDtos.cs               # [TẠO MỚI] TripQuoteRequest/Response, NearbyDriverDto...
│   ├── Services/
│   │   ├── PricingAppService.cs           # [TẠO MỚI] Xử lý SD 09 (Quote + $geoIntersects SurgeZone)
│   │   └── DriverMatchingAppService.cs    # [TẠO MỚI] Xử lý SD 10 & SD 11 ($nearSphere + $nin)
│   └── Consumers/
│       ├── TripRequestedConsumer.cs       # [TẠO MỚI] Nhận TripRequestedEvent & RetryMatchingCommand
│       └── DriverAvailabilityConsumer.cs  # [TẠO MỚI] Mở khóa tài xế khi Reject / Cancel / Completed
├── Infrastructure/
│   └── Data/
│       ├── MongoDbContext.cs              # [ĐÃ CÓ]
│       └── MongoDbSeeder.cs               # [TẠO MỚI] Seed 3 biểu phí, 1 vùng Surge Quận 1 & 3 tài xế
└── Api/
    └── MatchingEndpoints.cs               # [TẠO MỚI] 5 REST Endpoints cho Pricing & Driver GPS
```

## 2. Hướng Dẫn Code Chi Tiết Theo Từng Ngày

### Ngày 1 – Ngày 2: Cập Nhật Entity, Viết `MongoDbSeeder` & Strategy Pattern Tính Giá
1. **Bổ sung `Bearing` và `Speed` vào `src/MatchingService/Domain/Entities/Driver.cs`:**
   ```csharp
   [BsonElement("bearing")]
   public double? Bearing { get; set; }

   [BsonElement("speed")]
   public double? Speed { get; set; }
   ```
2. **Viết `MongoDbSeeder.cs` với đúng bộ GUID cố định của dự án:**
   - **Seed `PricingConfigs`:**
     - `Bike`: `BaseDistanceRate = 10000m`, `BaseTimeRate = 1000m`, `BaseTollFee = 0m`, `TaxRate = 0.10m`, `PromoMultiplier = 1.0m`, `IsActive = true`.
     - `Car4`: `BaseDistanceRate = 15000m`, `BaseTimeRate = 15000m`, `BaseTollFee = 0m`, `TaxRate = 0.10m`, `PromoMultiplier = 1.0m`, `IsActive = true`.
     - `Car7`: `BaseDistanceRate = 18000m`, `BaseTimeRate = 1800m`, `BaseTollFee = 0m`, `TaxRate = 0.10m`, `PromoMultiplier = 1.0m`, `IsActive = true`.
   - **Seed `SurgeZones`:**
     - `"Khu vực Chợ Bến Thành - Quận 1"`, `SurgeMultiplier = 1.3m`, `IsActive = true`, `Boundary` là Polygon khép kín `[(106.690, 10.770), (106.705, 10.770), (106.705, 10.785), (106.690, 10.785), (106.690, 10.770)]` *(Lưu ý: GeoJSON trong MongoDB đặt **Longitude trước, Latitude sau**)*.
   - **Seed `Drivers` (3 tài xế mẫu):**
     - `Driver 1`: `DriverId = "22222222-2222-2222-2222-222222222222"`, `Name = "Nguyễn Văn Tài Xế 1"`, `Vehicle.Type = "Bike"`, `IsActive = true`, `IsAvailable = true`, `Location = GeoJson.Point(GeoJson.Geographic(106.6990, 10.7755))`.
     - `Driver 2`: `DriverId = "33333333-3333-3333-3333-333333333333"`, `Name = "Trần Văn Tài Xế 2"`, `Vehicle.Type = "Bike"`, `IsActive = true`, `IsAvailable = true`, `Location = GeoJson.Point(GeoJson.Geographic(106.7025, 10.7782))`.
     - `Driver 3`: `DriverId = "44444444-4444-4444-4444-444444444444"`, `Name = "Lê Văn Tài Xế 3"`, `Vehicle.Type = "Car4"`, `IsActive = true`, `IsAvailable = true`, `Location = GeoJson.Point(GeoJson.Geographic(106.7010, 10.7760))`.
3. **Hiện thực hóa Strategy Pattern & SOLID (Mục 2.3 & Mục 6.1, 6.2, 6.3 PDF):**
   - Công thức bắt buộc:
     $$p = \big((c_d \cdot d + c_t \cdot t) \cdot s + c_m\big) \cdot \eta \cdot (1 + T)$$
   - Thiết kế `IPricingStrategy`, `StandardPricingStrategy`, `SurgePricingStrategy`, `PromoTaxPricingStrategy` và `FareCalculator` theo đúng Listing 1, 2, 3 trong PDF:
     ```csharp
     public record PricingContext(
         decimal DistanceKm,
         int DurationMinutes,
         decimal BaseDistanceRate,
         decimal BaseTimeRate,
         decimal SurgeMultiplier,
         decimal TollFee,
         decimal PromoMultiplier,
         decimal TaxRate);

     public interface IPricingStrategy
     {
         decimal GetPrice(PricingContext context);
     }

     public sealed class StandardPricingStrategy : IPricingStrategy
     {
         public decimal GetPrice(PricingContext ctx)
             => (ctx.BaseDistanceRate * ctx.DistanceKm) + (ctx.BaseTimeRate * ctx.DurationMinutes);
     }

     public sealed class SurgePricingStrategy : IPricingStrategy
     {
         private readonly IPricingStrategy _basePricing;
         public SurgePricingStrategy(IPricingStrategy basePricing) => _basePricing = basePricing;

         public decimal GetPrice(PricingContext ctx)
             => ((_basePricing.GetPrice(ctx) * ctx.SurgeMultiplier) + ctx.TollFee)
                * ctx.PromoMultiplier * (1m + ctx.TaxRate);
     }

     public sealed class FareCalculator
     {
         public decimal Calculate(PricingContext ctx, IPricingStrategy strategy)
         {
             decimal price = Math.Round(strategy.GetPrice(ctx), 0);
             // LSP Postcondition check (Listing 3 PDF)
             if (price < 0) throw new InvalidOperationException("Gia khong hop le (am)");
             return price;
         }
     }
     ```

### Ngày 3 – Ngày 5: Xây Dựng 5 REST APIs Của `MatchingService`
1. **`POST /api/matching/quote` (SD 09):**
   - Tính khoảng cách đường chim bay nhân hệ số đường bộ ($d = \text{Haversine} \times 1.3$ km) và thời gian ước tính ($t = \max(5, \lceil d / 0.4 \rceil)$ phút).
   - Truy vấn `SurgeZones` bằng `Builders<SurgeZone>.Filter.GeoIntersects(z => z.Boundary, pickupPoint)` để lấy `SurgeMultiplier` (mặc định `1.0m` nếu không thuộc vùng Surge).
   - Tính giá qua `FareCalculator`, lưu vào collection `TripQuotes` (hết hạn sau 10 phút) và trả về `ApiResponse<TripQuoteResponse>`.
2. **`POST /api/matching/find-drivers` (SD 11):**
   - Dùng `Builders<Driver>.Filter.NearSphere(d => d.Location, pickupPoint, maxDistanceInMeters)` kết hợp `IsActive == true && IsAvailable == true` để trả về danh sách `NearbyDriverDto`.
3. **`GET /api/drivers/{id}/location` (SD 10):**
   - Tìm tài xế theo `driver_id`, trả về `DriverLocationDto` (`latitude = Location.Coordinates.Latitude`, `longitude = Location.Coordinates.Longitude`, `bearing`, `speed`, `lastLocationUpdatedAt`).
4. **`PUT /api/drivers/{id}/location` (SD 10):**
   - Cập nhật `Location = GeoJson.Point(GeoJson.Geographic(req.Longitude, req.Latitude))`, `Bearing = req.Bearing`, `Speed = req.Speed`, `LastLocationUpdatedAt = DateTime.UtcNow`.
   - Nếu `req.ActiveTripId != null`, publish `DriverLocationUpdatedEvent` lên RabbitMQ.
5. **`PUT /api/drivers/{id}/availability` (SD 10):**
   - Cập nhật `IsAvailable = req.IsAvailable` cho tài xế.

### Ngày 6 – Ngày 8: Xây Dựng Matching Engine Consumers ($\le k$ Lần Thử)
1. **`TripRequestedConsumer` & `RetryMatchingConsumer` (SD 11):**
   - Chuyển danh sách `ExcludedDriverIds` sang `List<string>`.
   - Truy vấn tìm tài xế gần nhất trong bán kính 5km:
     ```csharp
     var point = GeoJson.Point(GeoJson.Geographic((double)msg.PickupLongitude, (double)msg.PickupLatitude));
     var filter = Builders<Driver>.Filter.And(
         Builders<Driver>.Filter.Eq(d => d.IsActive, true),
         Builders<Driver>.Filter.Eq(d => d.IsAvailable, true),
         Builders<Driver>.Filter.Eq(d => d.Vehicle.Type, msg.VehicleType),
         Builders<Driver>.Filter.Nin(d => d.DriverId, excludedIds),
         Builders<Driver>.Filter.NearSphere(d => d.Location, point, 5000)
     );
     var nearestDriver = await _mongoContext.Drivers.Find(filter).FirstOrDefaultAsync();
     ```
   - Nếu `nearestDriver != null`:
     - Tạm khóa tài xế: cập nhật `IsAvailable = false`.
     - Publish `TripOfferDispatchedEvent` (với `CountdownSeconds = 15`) để `TripHub` bật popup trên màn hình tài xế.
   - Nếu `nearestDriver == null`:
     - Publish `MatchingFailedEvent(msg.TripId, "Không tìm thấy tài xế khả dụng quanh khu vực")`.
2. **`DriverAvailabilityConsumer`:**
   - Lắng nghe `DriverRejectedOfferEvent`, `TripCancelledEvent`, `TripCompletedEvent` để cập nhật lại `IsAvailable = true` cho tài xế tương ứng.

### Ngày 9 – Ngày 10: Viết Unit Tests (`tests/MatchingService.Tests`)
- Viết Unit Tests bằng `xUnit` cho:
  1. `StandardPricingStrategy` và `SurgePricingStrategy` khớp chính xác công thức toán học trong PDF Mục 2.3.
  2. `FareCalculator` ném `InvalidOperationException` khi một strategy trả về giá âm (chứng minh tuân thủ LSP - Mục 6.3 PDF).

## 3. Cách Tự Test Độc Lập 100% (Không Cần Chờ TripService)
1. Chạy `docker compose up -d mongodb rabbitmq` và `dotnet run --project src/MatchingService`.
2. Mở `http://localhost:5248/openapi/v1.json` hoặc dùng `MatchingService.http` gọi:
   - `POST http://localhost:5248/api/matching/quote` với tọa độ Chợ Bến Thành `(10.776889, 106.700806)` $\to$ kiểm tra `surgeMultiplier` tự động nhận `1.3` và lưu vào `TripQuotes`.
   - `POST http://localhost:5248/api/matching/find-drivers` $\to$ kiểm tra trả về `Driver 1` và `Driver 2` theo thứ tự khoảng cách tăng dần.
3. Mở RabbitMQ Management UI (`http://localhost:15672`) hoặc tạo 1 endpoint dev `POST /dev/simulate-trip-requested` để bắn thử `TripRequestedEvent` với `ExcludedDriverIds = ["22222222-2222-2222-2222-222222222222"]`, kiểm tra hệ thống tự động bỏ qua `Driver 1` và chọn `Driver 2`!
