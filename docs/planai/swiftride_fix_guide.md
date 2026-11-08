# Kế Hoạch & Hướng Dẫn Khắc Phục Các Điểm Lệch Kiến Trúc — Dự Án SwiftRide

Tài liệu này cung cấp hướng dẫn chi tiết từng bước (kèm mã nguồn thay thế trực tiếp) nhằm khắc phục triệt để các lỗi logic và khoảng trống kiến trúc của dự án **SwiftRide** so với tài liệu đặc tả `docs/RideLite-KienTrucPhanMem.pdf`.

| Mã hạng mục | Vấn đề phát hiện | Mức độ ưu tiên | Mục tham chiếu trong PDF | Các dịch vụ bị ảnh hưởng |
| :--- | :--- | :--- | :--- | :--- |
| **FIX-01** | Lệch giá trị mặc định `PaymentMethod = "Cash"` gây lỗi `422` khi trả khách | **Cao (Bắt buộc)** | Mục 2.2 (Bước 7), Mục 4, Mục 5.3 | `TripService`, `PaymentService`, `TripService.Tests` |
| **FIX-02** | Thiếu lớp trừu tượng Repository và DIP ở `MatchingService` & `PaymentService` | **Trung bình (Nên làm)** | Mục 6.4 (ISP), Mục 6.5 (DIP), Mục 7 | `MatchingService`, `PaymentService` |
| **FIX-03** | Chưa bật xác thực bắt buộc trên YARP, fallback User ID cứng và lộ Secret trong config | **Trung bình (Nên làm)** | Mục 3.1, Mục 9.3 | `ApiGateway`, `TripService`, `MatchingService`, `docker-compose.yml` |
| **FIX-04** | Chưa tích hợp OpenTelemetry (Distributed Tracing & Metrics) | **Mở rộng (Điểm cộng)** | Mục 8, Mục 9.3 | `ApiGateway`, `TripService`, `MatchingService`, `PaymentService` |
| **FIX-05** | Thiếu cấu hình Timeout mạng, API Aggregation tại Gateway và Dockerfile cho các app .NET | **Mở rộng (Điểm cộng)** | Mục 3.1, Mục 8.1, Mục 9.2 | `ApiGateway`, `docker-compose.yml` |

## 1. FIX-01: Đồng bộ phương thức thanh toán mặc định giữa TripService và PaymentService

### 1.1. Nguyên nhân gốc rễ
* Trong `TripService`, khi tài xế hoàn tất trả khách (`PUT /api/trips/{id}/drop-off` hoặc `POST /trips/{id}/dropoff`) mà request body không truyền `paymentMethod`, hệ thống đang gán mặc định là `"Cash"`.
* Tuy nhiên, trong `src/PaymentService/Domain/Factories/PaymentMethodFactory.cs`, danh sách phương thức thanh toán được hỗ trợ chỉ bao gồm `["Wallet", "Momo", "VNPay", "Stripe"]`.
* Khi `TripDroppedOffConsumer` bên `PaymentService` nhận sự kiện `TripDroppedOffEvent` có `PaymentMethod = "Cash"`, hàm `PaymentMethods.Normalize("Cash")` trả về `null`, ném ngoại lệ `PaymentException (422 ValidationError)` và đẩy chuyến đi sang trạng thái lỗi `PaymentPending`.

### 1.2. Giải pháp khắc phục
Đổi giá trị mặc định tại `TripService` từ `"Cash"` sang `"Wallet"` để khớp với cơ chế trừ ví nội bộ và sổ cái kép (Double-entry Ledger) trong `PaymentService`.

#### Bước 1: Cập nhật `src/TripService/Application/DTOs/TripDtos.cs`
Thay đổi giá trị mặc định của `TripActionRequest`:

```csharp
// Trước khi sửa (dòng 44-46):
public record TripActionRequest(
    string? Reason = null,
    string PaymentMethod = "Cash");

// Sau khi sửa:
public record TripActionRequest(
    string? Reason = null,
    string PaymentMethod = "Wallet");
```

#### Bước 2: Cập nhật `src/TripService/Application/Services/ITripAppService.cs`
Thay đổi tham số mặc định của phương thức `DropOffAsync`:

```csharp
// Trước khi sửa (dòng 19-22):
Task<ApiResponse<TripDto>> DropOffAsync(
    Guid tripId,
    string paymentMethod = "Cash",
    CancellationToken ct = default);

// Sau khi sửa:
Task<ApiResponse<TripDto>> DropOffAsync(
    Guid tripId,
    string paymentMethod = "Wallet",
    CancellationToken ct = default);
```

#### Bước 3: Cập nhật `src/TripService/Application/Services/TripAppService.cs`
Thay đổi tham số mặc định trong phần triển khai `DropOffAsync`:

```csharp
// Trước khi sửa (dòng 285-288):
public async Task<ApiResponse<TripDto>> DropOffAsync(
    Guid tripId,
    string paymentMethod = "Cash",
    CancellationToken ct = default)

// Sau khi sửa:
public async Task<ApiResponse<TripDto>> DropOffAsync(
    Guid tripId,
    string paymentMethod = "Wallet",
    CancellationToken ct = default)
```

#### Bước 4: Cập nhật `src/TripService/Api/TripEndpoints.cs`
Thay đổi giá trị fallback khi gọi `DropOffAsync`:

```csharp
// Trước khi sửa (dòng 147):
return ToResult(await service.DropOffAsync(id, request?.PaymentMethod ?? "Cash", ct));

// Sau khi sửa:
return ToResult(await service.DropOffAsync(id, request?.PaymentMethod ?? "Wallet", ct));
```

#### Bước 5: Cập nhật Unit Test trong `tests/TripService.Tests/TripAppServiceTests.cs`
Đồng bộ lại test case `DropOffAsync_PickedUpTrip_WritesPaymentEvent`:

```csharp
// Trước khi sửa (dòng 119):
var response = await service.DropOffAsync(trip.Id, "Cash");

// Sau khi sửa:
var response = await service.DropOffAsync(trip.Id, "Wallet");
```

## 2. FIX-02: Bổ sung Repository Pattern và DIP cho MatchingService & PaymentService

### 2.1. Nguyên nhân gốc rễ
* Mục 6.4 (ISP), Mục 6.5 (DIP) và Mục 7 (Design Patterns) trong tài liệu đặc tả yêu cầu: *"Module cấp cao phụ thuộc trừu tượng, không phụ thuộc chi tiết"* và sử dụng **Repository Pattern** để *"Trừu tượng hóa truy cập CSDL cho mỗi service"*.
* Hiện tại chỉ có `TripService` tách `ITripReader`, `ITripWriter` và `TripRepository`. Trong khi đó, `MatchingService` (`PricingAppService`, `DriverMatchingAppService`) và `PaymentService` (`PaymentAppService`) vẫn đang phụ thuộc trực tiếp vào lớp cụ thể `MongoDbContext` và `PaymentDbContext`.

### 2.2. Giải pháp cho `MatchingService`

#### Bước 1: Tạo các interface trừu tượng trong `src/MatchingService/Domain/Interfaces/`
Tạo file `src/MatchingService/Domain/Interfaces/IPricingRepository.cs`:

```csharp
using MatchingService.Domain.Entities;

namespace MatchingService.Domain.Interfaces;

public interface IPricingRepository
{
    Task<PricingConfig?> GetActivePricingConfigAsync(string vehicleType, DateTime asOfUtc, CancellationToken ct = default);
    Task<SurgeZone?> GetActiveSurgeZoneAsync(decimal latitude, decimal longitude, DateTime asOfUtc, CancellationToken ct = default);
    Task SaveQuoteAsync(TripQuote quote, CancellationToken ct = default);
}
```

Tạo file `src/MatchingService/Domain/Interfaces/IDriverRepository.cs`:

```csharp
using MatchingService.Domain.Entities;

namespace MatchingService.Domain.Interfaces;

public interface IDriverRepository
{
    Task<IReadOnlyList<Driver>> FindNearbyAvailableDriversAsync(
        decimal pickupLatitude,
        decimal pickupLongitude,
        double radiusKm,
        int limit,
        string? vehicleType,
        CancellationToken ct = default);

    Task<Driver?> GetByDriverIdAsync(string driverId, CancellationToken ct = default);
    Task<bool> UpdateLocationAsync(string driverId, decimal latitude, decimal longitude, double? bearing, double? speed, DateTime updatedAtUtc, CancellationToken ct = default);
    Task<bool> UpdateAvailabilityAsync(string driverId, bool isAvailable, CancellationToken ct = default);
    Task<bool> ExistsAsync(string driverId, CancellationToken ct = default);
    Task<Driver?> ReserveNearestDriverAsync(
        decimal pickupLatitude,
        decimal pickupLongitude,
        string vehicleType,
        IEnumerable<string> excludedDriverIds,
        string tripId,
        CancellationToken ct = default);
    Task ReleaseDriverAsync(string driverId, string tripId, CancellationToken ct = default);
    Task ReleaseTripReservationAsync(string tripId, CancellationToken ct = default);
    Task<IReadOnlyList<Driver>> GetDriversReservedForTripAsync(string tripId, CancellationToken ct = default);
    Task UpsertCancelledTripAsync(CancelledTrip cancelledTrip, CancellationToken ct = default);
    Task<bool> IsTripCancelledAsync(string tripId, CancellationToken ct = default);
}
```

#### Bước 2: Tạo lớp triển khai Repository trong `src/MatchingService/Infrastructure/Repositories/`
Tạo file `src/MatchingService/Infrastructure/Repositories/PricingRepository.cs`:

```csharp
using MatchingService.Domain.Entities;
using MatchingService.Domain.Interfaces;
using MatchingService.Infrastructure.Data;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;

namespace MatchingService.Infrastructure.Repositories;

public sealed class PricingRepository(MongoDbContext mongoContext) : IPricingRepository
{
    public async Task<PricingConfig?> GetActivePricingConfigAsync(string vehicleType, DateTime asOfUtc, CancellationToken ct = default)
        => await mongoContext.PricingConfigs
            .Find(p => p.VehicleType == vehicleType && p.IsActive && p.EffectiveFrom <= asOfUtc)
            .SortByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

    public async Task<SurgeZone?> GetActiveSurgeZoneAsync(decimal latitude, decimal longitude, DateTime asOfUtc, CancellationToken ct = default)
    {
        var pickupPoint = GeoJson.Point(GeoJson.Geographic((double)longitude, (double)latitude));
        var zoneFilter = Builders<SurgeZone>.Filter.And(
            Builders<SurgeZone>.Filter.Eq(zone => zone.IsActive, true),
            Builders<SurgeZone>.Filter.Gt(zone => zone.ExpiresAt, asOfUtc),
            Builders<SurgeZone>.Filter.GeoIntersects(zone => zone.Boundary, pickupPoint));

        return await mongoContext.SurgeZones.Find(zoneFilter).FirstOrDefaultAsync(ct);
    }

    public Task SaveQuoteAsync(TripQuote quote, CancellationToken ct = default)
        => mongoContext.TripQuotes.InsertOneAsync(quote, cancellationToken: ct);
}
```

#### Bước 3: Đăng ký DI trong `src/MatchingService/Program.cs`
Thêm đăng ký Repository và inject `IPricingRepository` vào `PricingAppService`:

```csharp
builder.Services.AddScoped<IPricingRepository, PricingRepository>();
builder.Services.AddScoped<IDriverRepository, DriverRepository>();
```

### 2.3. Giải pháp cho `PaymentService`

#### Bước 1: Tạo interface `IPaymentRepository` (hoặc tách `IPaymentReader` / `IPaymentWriter` theo ISP)
Tạo file `src/PaymentService/Domain/Interfaces/IPaymentRepository.cs`:

```csharp
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Domain.Interfaces;

public interface IPaymentRepository
{
    Task<IdempotencyRecord?> GetIdempotencyRecordAsync(string key, CancellationToken ct = default);
    void AddIdempotencyRecord(IdempotencyRecord record);
    Task<bool> IsTripAlreadyPaidAsync(Guid tripId, CancellationToken ct = default);
    Task<Transaction?> GetPendingRideTransactionAsync(Guid tripId, CancellationToken ct = default);
    Task<Transaction?> GetTransactionWithEntriesAsync(Guid transactionId, CancellationToken ct = default);
    Task<Account?> GetAccountAsync(Guid ownerId, AccountType type, CancellationToken ct = default);
    Task<Account?> GetPrimaryAccountByOwnerAsync(Guid ownerId, CancellationToken ct = default);
    Task<Dictionary<Guid, Account>> GetAccountsByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<PaymentToken?> ResolveTokenAsync(Guid riderId, string provider, Guid? tokenId, CancellationToken ct = default);
    Task<List<PaymentToken>> GetUserTokensAsync(Guid userId, bool asNoTracking = false, CancellationToken ct = default);
    void AddPaymentToken(PaymentToken token);
    void AddTransaction(Transaction transaction);
    void AddLedgerEntries(IEnumerable<LedgerEntry> entries);
    void AddOutboxMessage(OutboxMessage message);
    void ClearTracking();
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

#### Bước 2: Triển khai `PaymentRepository` trong `src/PaymentService/Infrastructure/Repositories/PaymentRepository.cs`
Bọc các thao tác truy vấn `PaymentDbContext` hiện có trong `PaymentAppService` vào `PaymentRepository` và đăng ký trong `src/PaymentService/Program.cs`:

```csharp
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
```

## 3. FIX-03: Tăng cường bảo mật xác thực tại Gateway, kiểm tra quyền sở hữu tài nguyên và tách Secret

### 3.1. Nguyên nhân gốc rễ
* **Mục 9.3** yêu cầu:
  1. *"Gateway xác thực, service vẫn phải kiểm tra quyền truy cập đối với tài nguyên."*
  2. *"Không log token, số thẻ hoặc dữ liệu cá nhân nhạy cảm; secret không đặt trực tiếp trong source code."*
* Hiện tại:
  * Các route YARP trong `src/ApiGateway/appsettings.json` chưa khai báo `"AuthorizationPolicy": "default"`, dẫn đến YARP cho phép request không có JWT đi qua.
  * Khi thiếu header `X-User-Id`, `TripEndpoints.cs` và `MatchingEndpoints.cs` tự động gán `DefaultRiderId` (`11111111-...`) hoặc `DefaultDriverId` (`22222222-...`).
  * `TripAppService` chưa kiểm tra xem người gọi lệnh hủy chuyến, đón khách hay trả khách có thực sự là `RiderId` hoặc `DriverId` được gán cho chuyến đi đó hay không.
  * Khóa `Jwt:Secret` và mật khẩu cơ sở dữ liệu đang lưu trực tiếp trong `appsettings.json`.

### 3.2. Giải pháp khắc phục

#### Bước 1: Bật xác thực bắt buộc trên các Route YARP trong `src/ApiGateway/appsettings.json`
Thêm `"AuthorizationPolicy": "default"` vào các route nghiệp vụ trong `ReverseProxy:Routes`:

```json
"trip-root-route": {
  "ClusterId": "trip-cluster",
  "AuthorizationPolicy": "default",
  "RateLimiterPolicy": "general",
  "Match": {
    "Path": "/api/trips"
  }
},
"trip-route": {
  "ClusterId": "trip-cluster",
  "AuthorizationPolicy": "default",
  "RateLimiterPolicy": "general",
  "Match": {
    "Path": "/api/trips/{**catch-all}"
  }
}
```

*(Áp dụng tương tự cho `matching-root-route`, `matching-route`, `drivers-root-route`, `drivers-route`, `payment-root-route`, `payment-route`, `payment-tokens-root-route`, `payment-tokens-route`, `accounts-root-route`, `accounts-route` và các route alias).*

#### Bước 2: Chỉ cho phép Fallback User ID trong môi trường Development tại `src/TripService/Api/TripEndpoints.cs`
Sửa hàm `ReadUserId` trong `TripEndpoints.cs` để bắt buộc có `X-User-Id` hợp lệ khi không phải môi trường Development:

```csharp
private static (Guid Id, IResult? Error) ReadUserId(
    HttpContext httpContext,
    Guid devFallback)
{
    if (!httpContext.Request.Headers.TryGetValue("X-User-Id", out var value) ||
        string.IsNullOrWhiteSpace(value))
    {
        var env = httpContext.RequestServices.GetRequiredService<IHostEnvironment>();
        if (env.IsDevelopment())
            return (devFallback, null);

        return (Guid.Empty, ToResult(ApiResponse<TripDto>.Fail(
            401,
            "UNAUTHORIZED",
            "Missing X-User-Id header.")));
    }

    return Guid.TryParse(value, out var userId) && userId != Guid.Empty
        ? (userId, null)
        : (Guid.Empty, ToResult(ApiResponse<TripDto>.Fail(
            400,
            "INVALID_USER_ID",
            "X-User-Id must be a non-empty GUID.")));
}
```

#### Bước 3: Kiểm tra quyền sở hữu tài nguyên (Resource Ownership) trong `src/TripService/Application/Services/TripAppService.cs`
Trong các thao tác của tài xế (`StartRouteAsync`, `PickUpAsync`, `DropOffAsync`), truyền thêm `Guid driverId` từ header `X-User-Id` và kiểm tra:

```csharp
if (trip.DriverId != driverId)
{
    return ApiResponse<TripDto>.Fail(
        403,
        "FORBIDDEN_TRIP_ACCESS",
        "Bạn không phải là tài xế được phân công cho chuyến đi này.");
}
```

Và trong `CancelTripAsync(Guid tripId, Guid callerUserId, string? reason, CancellationToken ct)`:

```csharp
if (trip.RiderId != callerUserId && trip.DriverId != callerUserId)
{
    return ApiResponse<TripDto>.Fail(
        403,
        "FORBIDDEN_TRIP_ACCESS",
        "Bạn không có quyền hủy chuyến đi của người dùng khác.");
}
```

#### Bước 4: Tách Secret ra khỏi `appsettings.json` và `docker-compose.yml`
1. Xóa giá trị thật của `Jwt:Secret` và mật khẩu trong `appsettings.json` (để chuỗi rỗng `""`) và đọc qua biến môi trường:
   * `Jwt__Secret`
   * `ConnectionStrings__TripDbConnection`
   * `ConnectionStrings__MongoDbConnection`
   * `ConnectionStrings__PaymentDbConnection`
2. Tạo file `.env.example` ở thư mục gốc của dự án (và thêm `.env` vào `.gitignore`):

```ini
MSSQL_SA_PASSWORD=ChangeMe_StrongPassword123!
POSTGRES_PASSWORD=ChangeMe_PostgresPassword123!
RABBITMQ_DEFAULT_USER=guest
RABBITMQ_DEFAULT_PASS=guest
SWIFTRIDE_JWT_SECRET=ChangeMe_SuperSecretKey_AtLeast32Bytes!
```

## 4. FIX-04: Tích hợp OpenTelemetry (Logging, Metrics, Distributed Tracing)

### 4.1. Nguyên nhân gốc rễ
* **Mục 8** và **Mục 9.3** đề xuất dùng **OpenTelemetry** để liên kết log, metric và distributed trace qua `CorrelationId` / trace context, đồng thời theo dõi các chỉ số nghiệp vụ (tỉ lệ đặt xe thành công, thời gian matching, độ trễ API, tỉ lệ thanh toán lỗi).
* Hiện tại dự án đã có `X-Correlation-Id` tại `HeaderInjectionMiddleware` nhưng chưa cài đặt OpenTelemetry SDK.

### 4.2. Giải pháp khắc phục

#### Bước 1: Thêm gói NuGet OpenTelemetry vào 4 project (`ApiGateway`, `TripService`, `MatchingService`, `PaymentService`)
Thêm các `PackageReference` sau vào từng file `.csproj`:

```xml
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.11.2" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.11.1" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.11.1" />
<PackageReference Include="OpenTelemetry.Exporter.Console" Version="1.11.2" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.11.2" />
```

#### Bước 2: Tạo Extension Method dùng chung trong `src/Shared.Contracts/Common/ObservabilityExtensions.cs`
Hoặc cấu hình trực tiếp trong `Program.cs` của mỗi service:

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Thêm vào Program.cs của từng Service (thay đổi serviceName tương ứng)
var serviceName = builder.Environment.ApplicationName;

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
            })
            .AddHttpClientInstrumentation()
            .AddSource("MassTransit") // Tự động nối trace qua RabbitMQ (MassTransit hỗ trợ sẵn DiagnosticSource)
            .AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter("MassTransit")
            .AddMeter("SwiftRide.Metrics")
            .AddOtlpExporter();
    });
```

> **Lưu ý:** MassTransit v8+ tích hợp sẵn `DiagnosticSource` mang tên `"MassTransit"` và `Meter` tên `"MassTransit"`. Chỉ cần gọi `.AddSource("MassTransit")` và `.AddMeter("MassTransit")`, toàn bộ trace context sẽ tự động được truyền qua message header của RabbitMQ giữa `TripService`, `MatchingService` và `PaymentService`.

## 5. FIX-05: Bổ sung Timeout mạng, API Aggregation tại Gateway và Container hóa toàn bộ Solution

### 5.1. Cấu hình Request Timeout cho YARP Cluster (Mục 9.2: *"Mọi lời gọi mạng cần timeout"*)
Trong `src/ApiGateway/appsettings.json`, thêm cấu hình `HttpRequest` với `ActivityTimeout` (ví dụ 10 giây) cho cả 3 cluster để tránh treo kết nối khi một microservice bị chậm:

```json
"Clusters": {
  "trip-cluster": {
    "HttpRequest": {
      "ActivityTimeout": "00:00:10"
    },
    "Destinations": {
      "destination1": {
        "Address": "http://localhost:5105"
      }
    }
  },
  "matching-cluster": {
    "HttpRequest": {
      "ActivityTimeout": "00:00:10"
    },
    "Destinations": {
      "destination1": {
        "Address": "http://localhost:5248"
      }
    }
  },
  "payment-cluster": {
    "HttpRequest": {
      "ActivityTimeout": "00:00:10"
    },
    "Destinations": {
      "destination1": {
        "Address": "http://localhost:5114"
      }
    }
  }
}
```

### 5.2. Bổ sung Endpoint Aggregation tại `ApiGateway` (Mục 3.1: *"API Gateway đảm nhận... aggregation"*)
Tạo file `src/ApiGateway/Aggregation/TripAggregationEndpoints.cs` để minh họa rõ vai trò **Gateway Aggregation** (gom thông tin chuyến đi từ `TripService` + vị trí tài xế từ `MatchingService` trong 1 lần gọi cho màn hình theo dõi chuyến đi của Rider):

```csharp
using System.Text.Json;
using SwiftRide.Contracts.Common;

namespace ApiGateway.Aggregation;

public static class TripAggregationEndpoints
{
    public static void MapTripAggregationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/trips/{tripId:guid}/overview", async (
            Guid tripId,
            HttpContext httpContext,
            IHttpClientFactory httpClientFactory,
            CancellationToken ct) =>
        {
            var client = httpClientFactory.CreateClient("DownstreamAggregator");

            // Chuyển tiếp header X-User-Id và X-Correlation-Id xuống downstream
            if (httpContext.Request.Headers.TryGetValue("X-User-Id", out var userId))
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-User-Id", userId.ToString());
            if (httpContext.Request.Headers.TryGetValue("X-Correlation-Id", out var correlationId))
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-Id", correlationId.ToString());

            var tripUrl = $"http://localhost:5105/api/trips/{tripId}";
            using var tripResp = await client.GetAsync(tripUrl, ct);
            if (!tripResp.IsSuccessStatusCode)
            {
                return Results.StatusCode((int)tripResp.StatusCode);
            }

            var tripJson = await tripResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            JsonElement? driverLocation = null;

            if (tripJson.TryGetProperty("data", out var tripData) &&
                tripData.TryGetProperty("driverId", out var driverIdProp) &&
                driverIdProp.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(driverIdProp.GetString()))
            {
                var driverId = driverIdProp.GetString();
                var locUrl = $"http://localhost:5248/api/drivers/{driverId}/location";
                using var locResp = await client.GetAsync(locUrl, ct);
                if (locResp.IsSuccessStatusCode)
                {
                    var locJson = await locResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                    if (locJson.TryGetProperty("data", out var locData))
                        driverLocation = locData;
                }
            }

            return Results.Ok(ApiResponse<object>.Ok(new
            {
                Trip = tripJson.GetProperty("data"),
                DriverLocation = driverLocation
            }, "Aggregated trip overview fetched successfully"));
        }).RequireAuthorization();
    }
}
```

Đăng ký `HttpClient` kèm timeout 5 giây trong `src/ApiGateway/Program.cs`:

```csharp
builder.Services.AddHttpClient("DownstreamAggregator", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
```

## 6. Quy trình kiểm tra sau khi áp dụng các bản vá

Sau khi thực hiện các thay đổi trên (đặc biệt là **FIX-01** bắt buộc và **FIX-02/FIX-03**), chạy các lệnh sau tại thư mục gốc của dự án để xác nhận toàn bộ solution biên dịch thành công và vượt qua tất cả các bài kiểm thử đơn vị:

```bash
# 1. Kiểm tra biên dịch toàn bộ Solution
dotnet build SwiftRide.slnx --configuration Release

# 2. Chạy toàn bộ Unit Tests của cả 3 Microservices
dotnet test SwiftRide.slnx --no-build --configuration Release --verbosity normal
```
