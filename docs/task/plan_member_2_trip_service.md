# 🚗 Kế Hoạch Phát Triển Cá Nhân — Thành Viên 2 (Dev BE 2): Trip Service & SQL Server (`TripDB`)

> **Người phụ trách:** Thành viên 2 (Dev BE 2)  
> **Nhánh Git làm việc:** `feat/trip-service`  
> **Phạm vi thư mục sở hữu độc quyền:**  
> - `src/TripService/` (trừ thư mục `Api/Hubs/` do BE Lead phụ trách)  
> - `tests/TripService.Tests/`  
> **Cổng dịch vụ & CSDL:** Port `:5105` — SQL Server (`localhost:1433`, Database: `TripDB`, User: `sa`, Pass: `Password123!`)  
> **Sơ đồ trình tự phụ trách:** **SD 03, SD 04, SD 05, SD 06, SD 07, SD 08** và phối hợp **SD 11, SD 13**.

## 1. Hiện Trạng Mã Nguồn Đã Có Sẵn & Những File Cần Tạo Thêm

Trong repo `SwiftRide.slnx`, `src/TripService` đã được khởi tạo sẵn tầng Domain và Data Context. Dưới đây là bản đồ công việc của bạn:

```text
src/TripService/
├── Domain/
│   ├── Enums/TripStatus.cs                # [ĐÃ CÓ] 10 trạng thái (Requested..Failed)
│   ├── Entities/
│   │   ├── Trip.cs                        # [CẬP NHẬT] Thêm QuoteId, VehicleType, ExcludedDriverIds
│   │   ├── TripStatusHistory.cs           # [ĐÃ CÓ] Lưu lịch sử chuyển trạng thái
│   │   └── OutboxMessage.cs               # [ĐÃ CÓ] Lưu sự kiện Transactional Outbox
│   ├── States/                            # [CẬP NHẬT NHẸ] Bổ sung Complete() ở PaidState
│   └── Interfaces/
│       ├── ITripReader.cs                 # [TẠO MỚI] Chuẩn ISP (Mục 6.4 PDF - Listing 4)
│       └── ITripWriter.cs                 # [TẠO MỚI] Chuẩn ISP & DIP (Mục 6.4, 6.5 PDF)
├── Application/
│   ├── DTOs/TripDtos.cs                   # [TẠO MỚI] CreateTripRequest, TripDto, ConfirmQuoteRequest...
│   ├── Services/TripAppService.cs         # [TẠO MỚI] Xử lý 10 nghiệp vụ chuyến đi + ghi Outbox
│   └── Consumers/
│       ├── MatchingFailedConsumer.cs      # [TẠO MỚI] Nhận MatchingFailedEvent -> Đổi sang Failed
│       ├── PaymentSettledConsumer.cs      # [TẠO MỚI] Nhận PaymentSettledEvent -> Paid -> Completed
│       └── PaymentFailedConsumer.cs       # [TẠO MỚI] Nhận PaymentFailedEvent -> PaymentPending
├── Infrastructure/
│   ├── Data/
│   │   ├── TripDbContext.cs               # [ĐÃ CÓ]
│   │   ├── TripDbSeeder.cs                # [TẠO MỚI] Auto-Migrate + Seed chuyến xe mẫu
│   │   └── Configurations/                # [CẬP NHẬT] TripConfiguration.cs
│   ├── Repositories/TripRepository.cs     # [TẠO MỚI] Triển khai ITripReader & ITripWriter
│   └── BackgroundServices/
│       └── OutboxPublisherService.cs      # [CẬP NHẬT] Publish đúng kiểu Event sang MassTransit
└── Api/
    └── TripEndpoints.cs                   # [TẠO MỚI] 10 REST Endpoints chuẩn ApiResponse<T>
```

## 2. Hướng Dẫn Code Chi Tiết Theo Từng Ngày

### Ngày 1 – Ngày 2: Hoàn Thiện Domain, ISP/DIP Repositories & Seed Data
1. **Bổ sung 3 trường còn thiếu vào `src/TripService/Domain/Entities/Trip.cs`:**
   ```csharp
   public Guid? QuoteId { get; set; }
   public string VehicleType { get; set; } = "Bike"; // Bike, Car4, Car7
   public string? ExcludedDriverIds { get; set; } // Lưu chuỗi JSON: ["guid1", "guid2"]
   ```
   *(Cập nhật tương ứng trong `TripConfiguration.cs`: `builder.Property(t => t.VehicleType).HasMaxLength(20).HasDefaultValue("Bike");`).*
2. **Hoàn thiện State Pattern trong `src/TripService/Domain/States/` (Bám sát Listing 6 PDF & SD 08):**
   - Trong `TripState.cs`, thêm phương thức ảo:
     ```csharp
     public virtual TripState Complete()
         => throw new InvalidOperationException($"Khong the hoan tat chuyen di o trang thai {Status}");
     ```
   - Trong `DroppedOffState.cs`, bổ sung `public override TripState ConfirmPayment() => new PaidState();` (để hỗ trợ chuyển sang `Paid` ngay khi nhận `PaymentSettledEvent`).
   - Trong `PaidState.cs`, override:
     ```csharp
     public override TripState Complete() => new CompletedState();
     ```
3. **Tạo `ITripReader.cs`, `ITripWriter.cs` và `TripRepository.cs` (Đúng Listing 1, 4, 5 trong PDF Mục 6):**
   ```csharp
   namespace TripService.Domain.Interfaces;

   public interface ITripReader
   {
       Task<Trip?> GetByIdAsync(Guid id, CancellationToken ct = default);
       Task<Trip?> GetActiveTripByUserAsync(Guid userId, CancellationToken ct = default);
   }

   public interface ITripWriter
   {
       Task AddAsync(Trip trip, TripStatusHistory history, OutboxMessage? outbox = null, CancellationToken ct = default);
       Task UpdateWithOutboxAsync(Trip trip, TripStatusHistory history, OutboxMessage? outbox = null, CancellationToken ct = default);
   }
   ```
4. **Viết `TripDbSeeder.cs` với GUID cố định để khớp với các thành viên khác:**
   - Gọi `await dbContext.Database.EnsureCreatedAsync()` (hoặc `MigrateAsync()`).
   - Seed 1 chuyến đi mẫu đã hoàn tất hoặc đang ở trạng thái `Requested` với:
     - `Id = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479")`
     - `RiderId = Guid.Parse("11111111-1111-1111-1111-111111111111")`
     - `QuoteId = Guid.Parse("6a9e1e3e-4fa0-459f-bb24-9b2f6efc8721")`
     - `EstimatedFare = 65000m`, `DistanceKm = 4.8m`, `EstimatedDurationMinutes = 14`.

### Ngày 3 – Ngày 5: Xây Dựng 10 REST APIs Của `TripService`
Đảm bảo mọi API đều đọc Header `X-User-Id` (nếu chạy local không qua Gateway thì fallback về `11111111-1111-1111-1111-111111111111` cho Rider hoặc `22222222-2222-2222-2222-222222222222` cho Driver) và trả về `ApiResponse<TripDto>`:

| STT | Method & Route | Logic Xử Lý Cốt Lõi & State Transition |
| :-: | :--- | :--- |
| **1** | `POST /api/trips` | Tạo `Trip` mới ở `Status = TripStatus.Requested`, `FareConfirmed = false`. Lưu `Trips` + `TripStatusHistories`. **Chưa** bắn `TripRequestedEvent`. Trả về `201 Created`. |
| **2** | `POST /api/trips/{id}/confirm-quote` | Kiểm tra `Status == Requested`. Đổi `FareConfirmed = true`, ghi `TripStatusHistories` và ghi **`TripRequestedEvent`** vào bảng `OutboxMessages` trong cùng 1 transaction (SD 04). |
| **3** | `GET /api/trips/active` | Tìm chuyến đi của `X-User-Id` (là `RiderId` hoặc `DriverId`) có `Status` thuộc `(Requested, DriverAccepted, EnRoute, PickedUp, DroppedOff, PaymentPending)`. |
| **4** | `GET /api/trips/{id}` | Trả về `TripDto` theo `id` (nếu không có trả `404 TRIP_NOT_FOUND`). |
| **5** | `PUT /api/trips/{id}/accept` | Gọi `TripState.FromStatus(trip.Status).Accept()`. Gán `DriverId = userId`, `AcceptedAt = UtcNow`, ghi `TripAcceptedEvent` vào Outbox. Bắt `DbUpdateConcurrencyException` (do `RowVersion`) để trả về `409 Conflict` (`TRIP_ALREADY_ACCEPTED`) (SD 05). |
| **6** | `PUT /api/trips/{id}/reject` | Tăng `DriverRejectionCount += 1`, thêm `driverId` vào mảng JSON `ExcludedDriverIds`, ghi `DriverRejectedOfferEvent` vào Outbox. Nếu `DriverRejectionCount <= 3` ($k=3$): ghi thêm `RetryMatchingCommand` vào Outbox; nếu `> 3`: gọi `.Fail()` chuyển sang `Status = Failed` (SD 11). |
| **7** | `PUT /api/trips/{id}/start-route` | Gọi `.StartRoute()` chuyển `DriverAccepted -> EnRoute`, lưu `TripStatusHistories` (SD 06). |
| **8** | `PUT /api/trips/{id}/pick-up` | Gọi `.PickUp()` chuyển `EnRoute -> PickedUp`, gán `PickedUpAt = UtcNow` (SD 06). |
| **9** | `PUT /api/trips/{id}/drop-off` | Gọi `.DropOff()` chuyển `PickedUp -> DroppedOff`, gán `DroppedOffAt = UtcNow`, `ActualFare = EstimatedFare`, và ghi **`TripDroppedOffEvent`** (kèm `IdempotencyKey = $"pay_trip_{trip.Id}"`) vào `OutboxMessages` (SD 06). |
| **10** | `PUT /api/trips/{id}/cancel` | Gọi `.Cancel(reason)` (chỉ hợp lệ khi `Requested`, `DriverAccepted`, `EnRoute`; nếu `PickedUp` trở đi sẽ ném `InvalidOperationException` -> trả `409 TRIP_INVALID_TRANSITION`). Ghi `TripCancelledEvent` vào Outbox (SD 07). |

### Ngày 6 – Ngày 8: Hoàn Thiện Consumers & `OutboxPublisherService`
1. **Cập nhật `OutboxPublisherService.cs`:**
   - Dựa vào `message.EventType`, deserialize `message.Payload` về đúng kiểu record trong `SwiftRide.Contracts.Events` (ví dụ `TripRequestedEvent`, `TripDroppedOffEvent`...) rồi gọi `await publishEndpoint.Publish(typedEvent, stoppingToken)` để MassTransit gửi đúng queue cho `MatchingService` và `PaymentService`.
2. **Viết 3 Consumers trong `src/TripService/Application/Consumers/`:**
   - `PaymentSettledConsumer` (SD 08): Khi nhận `PaymentSettledEvent`, chuyển trạng thái `DroppedOff` (hoặc `PaymentPending`) $\to$ `Paid` (lưu `TripStatusHistories`) $\to$ `Completed` (`CompletedAt = UtcNow`, lưu `TripStatusHistories` và ghi `TripCompletedEvent` vào Outbox).
   - `PaymentFailedConsumer` (SD 13): Khi nhận `PaymentFailedEvent`, chuyển `DroppedOff` $\to$ `PaymentPending`.
   - `MatchingFailedConsumer` (SD 11): Khi nhận `MatchingFailedEvent`, chuyển `Requested` $\to$ `Failed`.

### Ngày 9 – Ngày 10: Viết Unit Tests (`tests/TripService.Tests`)
- Khởi tạo project xUnit `tests/TripService.Tests/TripService.Tests.csproj` (tham chiếu `Moq`, `xunit`).
- Viết ít nhất 8 Unit Tests:
  1. Test `RequestedState.Accept()` trả về `DriverAcceptedState`.
  2. Test `RequestedState.PickUp()` ném `InvalidOperationException` (không cho nhảy cóc trạng thái).
  3. Test `PickedUpState.Cancel()` ném `InvalidOperationException` (không cho hủy sau khi đã lên xe).
  4. Test `PaidState.Complete()` trả về `CompletedState`.
  5. Dùng `Moq` mock `ITripWriter` kiểm tra `ConfirmQuoteAsync` có tạo bản ghi `OutboxMessage` chứa `TripRequestedEvent` hay không.

## 3. Cách Tự Test Độc Lập 100% (Không Cần Chờ Matching Hay Payment Service)
1. Chạy `docker compose up -d sqlserver rabbitmq` và `dotnet run --project src/TripService`.
2. Mở trình duyệt hoặc dùng `src/TripService/TripService.http` gọi lần lượt:
   - `POST http://localhost:5105/api/trips` $\to$ `POST /confirm-quote` $\to$ `PUT /accept` $\to$ `PUT /start-route` $\to$ `PUT /pick-up` $\to$ `PUT /drop-off`.
3. Kiểm tra bảng `OutboxMessages` trong SQL Server thấy các dòng `TripRequestedEvent`, `TripDroppedOffEvent` đã có `ProcessedAt != null`.
4. Để test bước đóng chuyến (SD 08) khi Dev BE 4 chưa code xong `PaymentService`: Mở thêm 1 endpoint dev `POST /dev/simulate-payment-settled/{tripId}` (chỉ bật trong `app.Environment.IsDevelopment()`) để tự kích hoạt `PaymentSettledConsumer` và kiểm tra chuyến đi chuyển sang `Completed`!
