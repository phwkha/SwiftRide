# 👑 Kế Hoạch Phát Triển Cá Nhân — Thành Viên 1 (BE Lead): API Gateway, Shared Contracts & SignalR Realtime Hub

> **Người phụ trách:** Thành viên 1 (Backend Team Lead)  
> **Nhánh Git làm việc:** `feat/gateway-and-signalr`  
> **Phạm vi thư mục sở hữu độc quyền:**
>
> - `src/ApiGateway/` (Port `:5000`)
> - `src/TripService/Api/Hubs/` (SignalR `TripHub` tích hợp vào cổng `:5105`)
> - `src/Shared.Contracts/` (hoặc file `Contracts.cs` đồng bộ cho cả 4 projects)  
>   **Mục tiêu cốt lõi:** Mở khóa cho đội Frontend và 3 thành viên Backend phát triển song song ngay từ Ngày 1 mà không bị phụ thuộc lẫn nhau.

## 1. Hiện Trạng Mã Nguồn & Cấu Trúc File Cần Tạo

Trong repo `SwiftRide.slnx` hiện tại, `src/ApiGateway` mới chỉ có cấu hình YARP cơ bản cho 3 route (`/api/trips`, `/api/matching`, `/api/payments`). Bạn cần bổ sung các file theo cấu trúc sau:

```text
src/
├── ApiGateway/
│   ├── Auth/
│   │   ├── InMemoryIdentityStore.cs       # [TẠO MỚI] Lưu 5 user mẫu & RefreshToken
│   │   ├── JwtTokenService.cs             # [TẠO MỚI] Ký & kiểm tra JWT Bearer
│   │   └── AuthEndpoints.cs               # [TẠO MỚI] POST /login, /refresh, GET /me
│   ├── Middleware/
│   │   └── HeaderInjectionMiddleware.cs   # [TẠO MỚI] Bơm X-User-Id, X-User-Role, X-Correlation-Id
│   ├── Common/
│   │   └── ApiResponse.cs                 # [TẠO MỚI] Khung JSON chuẩn dùng chung
│   ├── appsettings.json                   # [CẬP NHẬT] Đầy đủ YARP Routes, Route Aliases & JWT Secret
│   └── Program.cs                         # [CẬP NHẬT] CORS, Auth, RateLimiter, YARP WebSocket
└── TripService/
    └── Api/
        └── Hubs/
            ├── TripHub.cs                 # [TẠO MỚI] Quản lý Group driver_{id} và trip_{id}
            └── SignalRRealtimeConsumers.cs# [TẠO MỚI] Lắng nghe Event & đẩy xuống FE
```

## 2. Việc Ưu Tiên Số 1 (2 Giờ Đầu Ngày 1): Bàn Giao Khung Dùng Chung Cho Cả Team

Để 3 bạn Dev BE còn lại code độc lập mà khi ghép lại khớp 100%, bạn gửi ngay 2 đoạn code chuẩn dưới đây vào nhóm chat (hoặc tạo project `src/Shared.Contracts` và reference vào 4 projects):

### 2.1. Khung phản hồi chuẩn `ApiResponse<T>`

```csharp
namespace SwiftRide.Contracts.Common;

public record ApiError(string Code, string Message, string? Field = null);

public record ApiResponse<T>(
    int StatusCode,
    string Message,
    T? Data,
    object? Meta = null,
    List<ApiError>? Errors = null,
    string? TraceId = null)
{
    public static ApiResponse<T> Ok(T data, string message = "Success", string? traceId = null)
        => new(200, message, data, null, null, traceId);

    public static ApiResponse<T> Created(T data, string message = "Created", string? traceId = null)
        => new(201, message, data, null, null, traceId);

    public static ApiResponse<T> Fail(int statusCode, string code, string message, string? field = null, string? traceId = null)
        => new(statusCode, message, default, null, [new ApiError(code, message, field)], traceId);
}
```

### 2.2. Bộ 12 Integration Event Records cho MassTransit RabbitMQ

Mọi thành viên bắt buộc đặt các record này trong cùng namespace `SwiftRide.Contracts.Events` (MassTransit dựa vào `Namespace + Tên Type` để định tuyến Exchange/Queue giữa các microservices):

```csharp
namespace SwiftRide.Contracts.Events;

// 1. TripService -> MatchingService (khi khách xác nhận báo giá ở SD 04)
public record TripRequestedEvent(
    Guid TripId,
    Guid RiderId,
    decimal PickupLatitude,
    decimal PickupLongitude,
    string PickupAddress,
    decimal DropoffLatitude,
    decimal DropoffLongitude,
    string DropoffAddress,
    string VehicleType,
    decimal DistanceKm,
    int EstimatedDurationMinutes,
    decimal EstimatedFare,
    List<Guid> ExcludedDriverIds);

// 2. TripService -> MatchingService (khi tài xế từ chối nhưng số lần thử <= k ở SD 11)
public record RetryMatchingCommand(
    Guid TripId,
    decimal PickupLatitude,
    decimal PickupLongitude,
    string PickupAddress,
    decimal DropoffLatitude,
    decimal DropoffLongitude,
    string DropoffAddress,
    string VehicleType,
    decimal DistanceKm,
    int EstimatedDurationMinutes,
    decimal EstimatedFare,
    int Attempt,
    List<Guid> ExcludedDriverIds);

// 3. MatchingService -> TripHub (mời tài xế nhận cuốc trong 15s ở SD 11)
public record TripOfferDispatchedEvent(
    Guid TripId,
    Guid DriverId,
    string PickupAddress,
    decimal PickupLatitude,
    decimal PickupLongitude,
    string DropoffAddress,
    decimal DropoffLatitude,
    decimal DropoffLongitude,
    decimal EstimatedFare,
    decimal DistanceKm,
    int DurationMinutes,
    int CountdownSeconds,
    DateTimeOffset ExpiresAt);

// 4. MatchingService -> TripHub (hết 15s tài xế không phản hồi ở SD 11)
public record TripOfferExpiredEvent(Guid TripId, Guid DriverId, string Message);

// 5. TripService -> MatchingService (tài xế bấm từ chối để mở lại is_available = true)
public record DriverRejectedOfferEvent(Guid TripId, Guid DriverId, string? Reason);

// 6. MatchingService -> TripService (không tìm thấy tài xế quanh khu vực ở SD 11)
public record MatchingFailedEvent(Guid TripId, string Reason);

// 7. TripService -> MatchingService (tài xế nhận cuốc thành công ở SD 05)
public record TripAcceptedEvent(Guid TripId, Guid DriverId, Guid RiderId);

// 8. MatchingService -> TripHub (tài xế gửi GPS định kỳ 10s khi đang chở khách ở SD 10)
public record DriverLocationUpdatedEvent(
    Guid? TripId,
    Guid DriverId,
    decimal Latitude,
    decimal Longitude,
    double? Bearing,
    double? Speed,
    DateTimeOffset UpdatedAt);

// 9. TripService -> PaymentService (tài xế trả khách tại đích ở SD 06 & SD 12)
public record TripDroppedOffEvent(
    Guid TripId,
    Guid RiderId,
    Guid DriverId,
    decimal ActualFare,
    string PaymentMethod,
    string IdempotencyKey);

// 10. PaymentService -> TripService (quyết toán sổ cái kép thành công ở SD 08 & SD 12)
public record PaymentSettledEvent(
    Guid TripId,
    Guid TransactionId,
    decimal TotalAmount,
    string PaymentMethod,
    decimal DriverCreditAmount);

// 11. PaymentService -> TripService (lỗi cổng thanh toán sau 3 lần retry ở SD 13)
public record PaymentFailedEvent(Guid TripId, string Status, string Reason);

// 12. TripService -> MatchingService (hủy chuyến hoặc hoàn tất để giải phóng tài xế)
public record TripCancelledEvent(Guid TripId, Guid? DriverId, Guid RiderId, string Reason);
public record TripCompletedEvent(Guid TripId, Guid RiderId, Guid DriverId, decimal TotalAmount, Guid TransactionId);
```

## 3. Kế Hoạch Triển Khai Chi Tiết Theo Từng Mốc

### Mốc 1 (Ngày 1 – Ngày 2): Hoàn Thiện `ApiGateway` (:5000) Bàn Giao Cho Frontend

1. **Xây dựng `InMemoryIdentityStore.cs` với đúng GUID cố định của dự án:**
   - `Rider 1`: `11111111-1111-1111-1111-111111111111` (`rider@swiftride.vn` / `Password123!`, Role: `Rider`)
   - `Driver 1`: `22222222-2222-2222-2222-222222222222` (`driver1@swiftride.vn` / `Password123!`, Role: `Driver`)
   - `Driver 2`: `33333333-3333-3333-3333-333333333333` (`driver2@swiftride.vn` / `Password123!`, Role: `Driver`)
   - `Driver 3`: `44444444-4444-4444-4444-444444444444` (`driver3@swiftride.vn` / `Password123!`, Role: `Driver`)
   - `Admin`: `99999999-9999-9999-9999-999999999999` (`admin@swiftride.vn` / `Password123!`, Role: `Admin`)
2. **Xây dựng 3 API Auth tại Gateway (`AuthEndpoints.cs`):**
   - `POST /api/auth/login` (SD 01): Trả về `accessToken` (hạn 15 phút), `refreshToken` (hạn 7 ngày), `expiresIn: 900` và object `user`.
   - `POST /api/auth/refresh` (SD 02): Kiểm tra và cấp mới cặp token.
   - `GET /api/auth/me` (SD 01): Đọc Claims từ JWT trả về `UserInfo` để FE khôi phục phiên khi F5.
3. **Viết `HeaderInjectionMiddleware.cs`:**
   - Nếu `HttpContext.User.Identity?.IsAuthenticated == true`, trích xuất `sub`, `role`, `email` và gán vào Request Header:
     - `context.Request.Headers["X-User-Id"] = userId;`
     - `context.Request.Headers["X-User-Role"] = role;`
     - `context.Request.Headers["X-User-Email"] = email;`
   - Luôn đảm bảo có `X-Correlation-Id` (nếu Client không gửi thì sinh `Activity.Current?.Id ?? Guid.NewGuid().ToString()`).
4. **Cập nhật `src/ApiGateway/appsettings.json` (Đầy đủ Routes & Route Aliases khớp PDF Mục 5):**
   - Bổ sung routes:
     - `/api/trips/{**catch-all}` & `/hubs/trips/{**catch-all}` $\to$ `trip-cluster` (`http://localhost:5105`)
     - `/api/matching/{**catch-all}` & `/api/drivers/{**catch-all}` $\to$ `matching-cluster` (`http://localhost:5248`)
     - `/api/payments/{**catch-all}`, `/api/payment-tokens/{**catch-all}`, `/api/accounts/{**catch-all}` $\to$ `payment-cluster` (`http://localhost:5114`)
   - Cấu hình YARP Route Transforms cho các alias trong PDF:
     - `/trips` $\to$ `/api/trips`
     - `/trips/{id}/driver-accept` $\to$ `/api/trips/{id}/accept`
     - `/trips/{id}/pickup` $\to$ `/api/trips/{id}/pick-up`
     - `/trips/{id}/dropoff` $\to$ `/api/trips/{id}/drop-off`
     - `/match` $\to$ `/api/matching/find-drivers`
     - `/quote` $\to$ `/api/matching/quote`
     - `/payments` $\to$ `/api/payments/process`
     - `/payments/{id}/refund` $\to$ `/api/payments/{id}/refund`

### Mốc 2 (Ngày 3 – Ngày 6): Xây Dựng SignalR `TripHub` (`/hubs/trips`)

1. Tạo `src/TripService/Api/Hubs/TripHub.cs`:

   ```csharp
   using Microsoft.AspNetCore.SignalR;

   namespace TripService.Api.Hubs;

   public class TripHub : Hub
   {
       public async Task JoinDriverRoom(string driverId)
           => await Groups.AddToGroupAsync(Context.ConnectionId, $"driver_{driverId}");

       public async Task LeaveDriverRoom(string driverId)
           => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"driver_{driverId}");

       public async Task JoinTripRoom(string tripId)
           => await Groups.AddToGroupAsync(Context.ConnectionId, $"trip_{tripId}");

       public async Task LeaveTripRoom(string tripId)
           => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"trip_{tripId}");
   }
   ```

2. Tạo các Consumer đẩy sự kiện SignalR:
   - Khi nhận `TripOfferDispatchedEvent`: gọi `_hubContext.Clients.Group($"driver_{e.DriverId}").SendAsync("ReceiveTripOffer", payload)`.
   - Khi nhận `TripOfferExpiredEvent`: gọi `_hubContext.Clients.Group($"driver_{e.DriverId}").SendAsync("TripOfferExpired", payload)`.
   - Khi nhận `DriverLocationUpdatedEvent`: gọi `_hubContext.Clients.Group($"trip_{e.TripId}").SendAsync("DriverLocationUpdated", payload)`.

### Mốc 3 (Ngày 7 – Ngày 10): Ghép Nối End-to-End, Rate Limit & Nghiệm Thu

1. Cấu hình `builder.Services.AddRateLimiter(...)` tại `ApiGateway` (Mục 3.1 PDF).
2. Kiểm tra luồng WebSocket từ FE qua `:5000/hubs/trips` vào `:5105/hubs/trips`.
3. Chạy kiểm thử toàn bộ 15 Sequence Diagrams cùng 3 thành viên BE và đội FE.

## 4. Hướng Dẫn Tự Kiểm Thử Độc Lập (Không Cần Đợi 3 Dev Kia)

- Chạy `dotnet run --project src/ApiGateway`.
- Dùng Postman / `.http` gọi `POST http://localhost:5000/api/auth/login` với `rider@swiftride.vn` / `Password123!`, lấy `accessToken` và gọi `GET http://localhost:5000/api/auth/me` để xác nhận trả về đúng `11111111-1111-1111-1111-111111111111`.
- Bàn giao ngay cổng `:5000` cho đội Frontend tích hợp màn hình Đăng nhập.
