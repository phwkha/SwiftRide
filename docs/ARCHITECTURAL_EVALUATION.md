# BÁO CÁO ĐÁNH GIÁ TOÀN DIỆN KIẾN TRÚC VÀ PHÂN TÍCH KHOẢNG CÁCH (GAP ANALYSIS): DỰ ÁN SWIFTRIDE

**Tài liệu tham chiếu**: `docs/RideLite-KienTrucPhanMem.pdf` (VER 1.0, Trường Đại học Sài Gòn - Khoa Công nghệ Thông tin)  
**Mã nguồn đối chiếu**: SwiftRide (Nền tảng Microservices trên .NET 10)  
**File báo cáo**: `docs/ARCHITECTURAL_EVALUATION.md`  

---

## 1. Tóm tắt điều hành (Executive Summary)

Đợt đánh giá kiến trúc và kiểm tra pháp y mã nguồn dự án **SwiftRide** đã được thực hiện toàn diện dựa trên tài liệu đặc tả kiến trúc phần mềm `docs/RideLite-KienTrucPhanMem.pdf`. Quá trình khảo sát bao quát toàn bộ ranh giới dịch vụ, mô hình lưu trữ dữ liệu (polyglot persistence), triển khai API, luồng truyền thông điệp bất đồng bộ (event-driven messaging), cấu hình bảo mật và các ứng dụng tầng giao diện (client applications).

### 1.1 Tổng quan mức độ tuân thủ kiến trúc
- **Mô hình kiến trúc tổng thể**: Kiến trúc Microservices với mô hình Database-per-Service và Lưu trữ Đa hệ quản trị cơ sở dữ liệu (Polyglot Persistence).
- **Điểm tuân thủ tổng thể toàn dự án**: **66.1% (Xếp loại: C+)**
- **Chi tiết mức độ hoàn thiện theo từng thành phần**:
  - **Edge API Gateway (`ApiGateway`)**: **100% Hoàn thành** (YARP reverse proxy, xác thực JWT Bearer, cơ chế khử và tiêm Header định danh phía sau Gateway, giới hạn tần suất truy cập Dual IP Rate Limiting, CORS, kho tài khoản định danh Identity Store mẫu).
  - **Matching & Pricing Service (`MatchingService`)**: **100% Hoàn thành** (MongoDB 7.0 với 8 chỉ mục bao gồm `2dsphere` cho Driver & SurgeZone, công thức định giá động áp dụng mẫu thiết kế Strategy và Decorator, ràng buộc bảo toàn nguyên lý LSP $p \ge 0$, thuật toán tìm tài xế bán kính `$nearSphere` với cơ chế khóa giữ chỗ nguyên tử, 3 MassTransit consumers, hệ thống REST API hoàn chỉnh).
  - **Trip Lifecycle Service (`TripService`)**: **55% Hoàn thành** (SQL Server 2022 qua EF Core 10, kiểm soát tương đồng lạc quan `RowVersion`, enum 10 trạng thái vòng đời cuốc xe, SignalR `TripHub`, tiến trình nền Transactional Outbox Publisher. **LỖ HỔNG NGHIÊM TRỌNG**: Thiếu toàn bộ 8 REST endpoints và 7 MassTransit consumer điều phối SignalR chưa được tạo mã nguồn).
  - **Payment Service (`PaymentService`)**: **50% Hoàn thành** (PostgreSQL 17 qua EF Core 10, mô hình sổ cái kép cân bằng $Debit = Credit$, mã hóa token thanh toán chuẩn PCI-DSS, bảng Idempotency, bảng Outbox. **LỖ HỔNG NGHIÊM TRỌNG**: Thiếu toàn bộ 4 REST endpoints thanh toán, chưa đăng ký MassTransit consumer nào — thiếu `TripDroppedOffConsumer` để kích hoạt quyết toán tự động, thiếu background worker xuất bản outbox).
  - **Hợp đồng dùng chung (`Shared.Contracts`)**: **100% Hoàn thành** (Chuẩn hóa phong bì dữ liệu `ApiResponse<T>` kèm `TraceId`, định nghĩa đầy đủ 13 sự kiện miền và lệnh tích hợp).
  - **Hạ tầng & Đa cơ sở dữ liệu (`docker-compose.yml`)**: **100% Hoàn thành** (Khởi chạy đồng bộ SQL Server 2022, MongoDB 7.0, PostgreSQL 17 và RabbitMQ 3.x Management).
  - **Kiểm thử tự động (Testing)**: **35% Hoàn thành** (Đã có `MatchingService.Tests`; hoàn toàn thiếu vắng `TripService.Tests` và `PaymentService.Tests` theo yêu cầu của tài liệu).
  - **Ứng dụng phía người dùng (Client Applications)**: **0% Hoàn thành** (Chưa có mã nguồn ứng dụng Rider hay Driver trong kho mã nguồn; mới chỉ dừng lại ở cấu hình CORS và định tuyến YARP).

---

## 2. Ma trận đối chiếu kiến trúc chi tiết (Compliance Matrix)

| STT | Thành phần kiến trúc / Yêu cầu | Tham chiếu trong PDF | Triển khai thực tế trong Codebase (File & Dòng) | Trạng thái | Ghi chú đánh giá & Kiểm định mã nguồn |
|:---:|---|---|---|:---:|---|
| **1** | **Mô hình kiến trúc tổng thể** (Microservices với Database-per-Service) | Mục 1 (tr. 2), Mục 3.1 (tr. 3) | `SwiftRide.slnx:1-12`, `docker-compose.yml:1-84` | **Đạt** | Phân tách nghiêm ngặt thành 4 dự án độc lập; không có tình trạng dùng chung cơ sở dữ liệu giữa các dịch vụ. |
| **2** | **Cổng vào duy nhất API Gateway** (Reverse Proxy & Điều hướng) | Mục 3.1 (tr. 3), Mục 3.2 (tr. 4), Mục 8 (tr. 9) | `src/ApiGateway/appsettings.json:28-150`, `Program.cs:122-149` | **Đạt** | YARP 2.3.0 định tuyến chính xác các tiền tố `/api/trips/**`, `/api/matching/**`, `/api/payments/**`, và kết nối WebSocket `/hubs/trips` về các cụm dịch vụ nội bộ. |
| **3** | **Xác thực JWT tại API Gateway** | Mục 3.1 (tr. 3), Mục 8 (tr. 9), Mục 9.3 (tr. 10) | `src/ApiGateway/Auth/AuthEndpoints.cs:15-93`, `InMemoryIdentityStore.cs:18-46` | **Đạt** | Thuật toán HMAC-SHA256, cơ chế xoay vòng access/refresh token, tích hợp sẵn tài khoản thử nghiệm Rider, 3 Driver và Admin. |
| **4** | **Khử và Tiêm Header định danh** (Downstream Header Sanitization & Injection) | Mục 3.1 (tr. 3), Mục 9.3 (tr. 10) | `src/ApiGateway/Middleware/HeaderInjectionMiddleware.cs:17-70` | **Đạt** | Xóa sạch các header `X-User-*` do client gửi lên nhằm chống giả mạo; tiêm các giá trị đã xác thực `X-User-Id`, `X-User-Role`, `X-User-Email` và `X-Correlation-Id`. |
| **5** | **Giới hạn tần suất & CORS tại Gateway** | Mục 3.1 (tr. 3), Mục 9.3 (tr. 10) | `src/ApiGateway/Program.cs:12-34` (CORS), `Program.cs:76-121` (Rate Limit) | **Đạt** | Áp dụng giới hạn theo IP: 100 req/phút cho API chung, 10 req/phút cho Auth; CORS cấp phép cho `localhost:3000`, `5173`, `5174`, `10.0.2.2:5000`. |
| **6** | **Kiến trúc Trip Service & SQL Server** | Mục 2.4 (tr. 3), Mục 3.3 (tr. 4), Mục 5.1 (tr. 6) | `src/TripService/Infrastructure/Data/TripDbContext.cs:8-44` | **Đạt** | SQL Server 2022 (`TripDB`) quản lý qua EF Core 10; tạo chỉ mục đầy đủ trên RiderId, DriverId, Status. |
| **7** | **State Machine vòng đời chuyến đi** (10 trạng thái nghiệp vụ) | Mục 4 (tr. 4-5), Mục 7 (tr. 8-9) | `src/TripService/Domain/Enums/TripStatus.cs:3-15`, `TripStatusHistory.cs:5-15` | **Hoàn thành 1 phần** | Enum mô tả đủ 10 trạng thái (`Requested` đến `Completed`/`Cancelled`/`Failed`) có bảng lưu vết lịch sử. Điểm lệch: Dùng enum thay vì triển khai GoF State Pattern dạng lớp đa hình. |
| **8** | **Kiểm soát tương đồng lạc quan** (`RowVersion`) | Mục 5.1 (tr. 6), Mục 9.2 (tr. 10) | `src/TripService/Domain/Entities/Trip.cs:32`, `TripDbContext.cs:29` | **Đạt** | Cột `RowVersion` (`byte[]` với `IsRowVersion()`) ngăn chặn race condition khi nhiều tài xế cùng tranh chấp nhận một cuốc xe. |
| **9** | **Hệ thống REST API của Trip Service** (`POST /trips`, chấp nhận, đón, trả) | Mục 5.1 (tr. 6) | `src/TripService/Program.cs:55-60` | **Chưa có** | **LỖ HỔNG NGHIÊM TRỌNG**: Chưa định nghĩa endpoint nào trong `TripService`. Hiện chỉ có route kiểm tra sống còn `/`. |
| **10**| **Kiến trúc Matching Service & MongoDB** | Mục 2.4 (tr. 3), Mục 3.3 (tr. 4), Mục 5.2 (tr. 6) | `src/MatchingService/Infrastructure/Data/MongoDbContext.cs:25-72` | **Đạt** | MongoDB 7.0 (`MatchDB`) thiết lập 8 chỉ mục: `2dsphere` trên `Driver.Location` và `SurgeZone.Boundary`, TTL trên báo giá và cuốc xe hủy. |
| **11**| **Công thức tính giá động** | Mục 2.3 (tr. 3), Mục 6.2 (tr. 6), Mục 7 (tr. 8) | `src/MatchingService/Application/Services/PricingAppService.cs:12-89` | **Đạt** | Triển khai chính xác công thức $p = ((c_d \cdot d + c_t \cdot t) \cdot s + c_m) \cdot \eta \cdot (1 + T)$ thông qua mẫu thiết kế Strategy và Decorator. |
| **12**| **Bảo toàn bất biến nguyên lý LSP** | Mục 6.3 (tr. 7) | `src/MatchingService/Application/Services/PricingAppService.cs:78-83` | **Đạt** | Ràng buộc cước phí $p \ge 0$. Bắn ngoại lệ `InvalidOperationException("CALCULATED_FARE_NEGATIVE")` nếu cước âm. |
| **13**| **Ghép tài xế theo không gian địa lý** (`$nearSphere`) | Mục 3.3 (tr. 4), Mục 5.2 (tr. 6) | `src/MatchingService/Application/Services/DriverMatchingAppService.cs:15-115` | **Đạt** | Tìm kiếm tài xế trong bán kính; cơ chế giữ chỗ nguyên tử 15 giây sử dụng `FindOneAndUpdateAsync` của MongoDB. |
| **14**| **Vòng lặp phát cuốc xe ($k$ lần thử lại)** | Mục 2.2 (tr. 2), Mục 4 (tr. 5) | `src/MatchingService/Infrastructure/Consumers/DriverRejectedOfferConsumer.cs:1-42` | **Đạt** | Tự động thử lại khi tài xế từ chối; bắn `MatchingFailedEvent` khi hết danh sách ứng viên hoặc vượt quá số lần thử. |
| **15**| **REST API của Matching Service** | Mục 5.2 (tr. 6) | `src/MatchingService/Api/MatchingEndpoints.cs:14-101` | **Đạt** | Hoàn thiện đầy đủ các API: `/api/matching/quote`, `/api/matching/find-drivers`, `/api/drivers/{id}/location`, `/api/drivers/{id}/availability`. |
| **16**| **Kiến trúc Payment Service & PostgreSQL** | Mục 2.4 (tr. 3), Mục 3.3 (tr. 4), Mục 5.3 (tr. 6) | `src/PaymentService/Infrastructure/Data/PaymentDbContext.cs:6-24` | **Đạt** | PostgreSQL 17 (`PayDB`) quản lý thông qua Npgsql EF Core 10. |
| **17**| **Mô hình sổ cái kép (Double-Entry Ledger)** | Mục 3.3 (tr. 4), Mục 5.3 (tr. 6) | `src/PaymentService/Domain/Entities/LedgerEntry.cs:5-16`, `Account.cs:5-17` | **Đạt (Tầng Model)** | Bảng `ledger_entries` bất biến với ràng buộc CHECK ($Amount > 0$); quản lý tài khoản Rider, Driver và Platform. |
| **18**| **Bảo vệ chống trùng lặp (Idempotency)** | Mục 5.3 (tr. 6), Mục 9.2 (tr. 10) | `src/PaymentService/Domain/Entities/IdempotencyRecord.cs:5-13`, `PaymentDbContext.cs` | **Đạt (Tầng Model)** | Chỉ mục duy nhất trên `transactions(trip_id)` đối với giao dịch thành công; bảng `idempotency_records` lưu hash request và thời hạn sống TTL. |
| **19**| **Token hóa thanh toán chuẩn PCI-DSS** | Mục 5.3 (tr. 6), Mục 9.3 (tr. 10) | `src/PaymentService/Domain/Entities/PaymentToken.cs:3-12` | **Đạt (Tầng Model)** | Chỉ lưu token do cổng thanh toán trả về (`Momo`, `VNPay`, `Stripe`); tuyệt đối không lưu số thẻ nguyên bản hay mã CVV. |
| **20**| **REST API & Consumer của Payment Service** | Mục 5.3 (tr. 6) | `src/PaymentService/Program.cs:18-41` | **Chưa có** | **LỖ HỔNG NGHIÊM TRỌNG**: Chưa có API thanh toán/hoàn tiền; thiếu consumer `TripDroppedOffConsumer` để thực hiện quyết toán. |
| **21**| **Truyền thông điệp bất đồng bộ RabbitMQ & MassTransit** | Mục 3.1 (tr. 3), Mục 8 (tr. 9), Mục 9.2 (tr. 10) | `src/Shared.Contracts/Events/IntegrationEvents.cs:1-95`, `docker-compose.yml:68-84` | **Hoàn thành 1 phần** | Đã cấu hình RabbitMQ và 13 domain events. MatchingService nhận tin tốt; TripService và PaymentService thiếu consumer. |
| **22**| **Mẫu Transactional Outbox** | Mục 7 (tr. 8), Mục 9.2 (tr. 10) | `src/TripService/Infrastructure/BackgroundServices/OutboxPublisherService.cs:1-75` | **Hoàn thành 1 phần** | Hoạt động định kỳ 5s tại `TripService`. Bên `PaymentService` mới có entity bảng outbox nhưng thiếu background worker xuất bản. |
| **23**| **Truyền thông thời gian thực (SignalR / WebSockets)** | Mục 3.1 (tr. 3), Mục 8 (tr. 9) | `src/TripService/Api/Hubs/TripHub.cs:1-38`, `ApiGateway/Program.cs:59-71` | **Hoàn thành 1 phần** | Đã có Hub và Gateway proxy WebSocket; thiếu các lớp consumer lắng nghe sự kiện để bắn dữ liệu ra SignalR Group. |
| **24**| **Bộ kiểm thử tự động (Unit & Integration Tests)** | Mục 8.1 (tr. 9) | `tests/MatchingService.Tests/PricingTests.cs:1-120` | **Hoàn thành 1 phần** | Mới chỉ có `MatchingService.Tests` kiểm tra giá và LSP; hoàn toàn thiếu `TripService.Tests` và `PaymentService.Tests`. |
| **25**| **Ứng dụng phía người dùng (Rider, Driver, Admin)** | Mục 3.1 (tr. 3), Mục 4 (tr. 4-5) | `src/ApiGateway/appsettings.json:16-26` (Mới có cấu hình CORS) | **Chưa có (0%)** | Chưa có bất kỳ dòng code frontend/mobile nào trong kho mã nguồn. |

---

## 3. Danh mục chi tiết các khoảng cách trọng yếu (Gap Analysis)

### `GAP-CORE-001` [NGHIÊM TRỌNG]: Thiếu hệ thống REST Endpoints điều khiển vòng đời chuyến đi trong `TripService`
- **Quy chuẩn trong PDF**: Mục 5.1 (tr. 6), Bảng "Danh sách API cơ bản" (`POST /trips`, `POST /trips/{id}/driver-accept`, `POST /trips/{id}/pickup`, `POST /trips/{id}/dropoff`).
- **Thực tế trong mã nguồn**: Tại `src/TripService/Program.cs` (dòng 55–60), dự án chỉ ánh xạ duy nhất endpoint kiểm tra sức khỏe `app.MapGet("/", ...)`. Hoàn toàn chưa có controller hoặc Minimal API route group nào cho nghiệp vụ chuyến đi.
- **Hệ quả**: Khách hàng không thể tạo chuyến, tài xế không thể bấm nhận chuyến, đón khách hoặc hoàn thành chuyến thông qua giao thức HTTP.

### `GAP-CORE-002` [NGHIÊM TRỌNG]: Thiếu mã nguồn 7 lớp MassTransit Consumer điều phối SignalR trong `TripService`
- **Quy chuẩn trong PDF**: Mục 3.1 (tr. 3), Mục 4 (tr. 5), Mục 8 (tr. 9).
- **Thực tế trong mã nguồn**: Trong `src/TripService/Program.cs` (dòng 25–31), 7 consumer đã được đăng ký vào Dependency Injection Container:
  - `TripOfferDispatchedConsumer`, `TripOfferExpiredConsumer`, `DriverLocationUpdatedConsumer`, `PaymentSettledSignalRConsumer`, `TripAcceptedSignalRConsumer`, `TripCancelledSignalRConsumer`, `MatchingFailedSignalRConsumer`.
  Tuy nhiên, thư mục `src/TripService/Infrastructure/BackgroundServices/` chỉ có duy nhất file `OutboxPublisherService.cs`. Toàn bộ 7 file class của các consumer trên chưa hề được tạo.
- **Hệ quả**: Các sự kiện bắn ra từ RabbitMQ không thể chuyển tiếp theo thời gian thực tới tài xế hoặc hành khách qua SignalR.

### `GAP-CORE-003` [NGHIÊM TRỌNG]: Thiếu Endpoints xử lý thanh toán và luồng quyết toán sổ cái tự động trong `PaymentService`
- **Quy chuẩn trong PDF**: Mục 5.3 (tr. 6), Bảng "Payment Service" (`POST /payments`, `POST /payments/{id}/refund`), Mục 3.3 (tr. 4).
- **Thực tế trong mã nguồn**: `src/PaymentService/Program.cs` (dòng 18–41) cấu hình `PaymentDbContext` và một MassTransit bus rỗng. Không có endpoint HTTP nào, và cũng không có consumer nào được khai báo.
- **Hệ quả**: Sự kiện kết thúc chuyến xe (`TripDroppedOffEvent`) không thể tự động kích hoạt tiến trình cân bằng sổ cái kép ($Debit = Credit$), và hệ thống cũng không có API thanh toán trực tiếp.

### `GAP-CORE-004` [MỨC ĐỘ CAO]: Thiếu tiến trình nền Outbox Publisher trong `PaymentService`
- **Quy chuẩn trong PDF**: Mục 7 (tr. 8), Mục 9.2 (tr. 10) "Mẫu Transactional Outbox".
- **Thực tế trong mã nguồn**: File `src/PaymentService/Domain/Entities/OutboxMessage.cs` đã định nghĩa cấu trúc bảng outbox, nhưng thiếu một `BackgroundService` để đọc các bản ghi này và xuất bản lên RabbitMQ.
- **Hệ quả**: Khi `PaymentService` ghi nhận các sự kiện thanh toán thành công (ví dụ `PaymentSettledEvent`), thông điệp sẽ bị kẹt lại vĩnh viễn trong cơ sở dữ liệu PostgreSQL.

### `GAP-CORE-005` [MỨC ĐỘ CAO]: Thiếu toàn bộ các bộ kiểm thử tự động cho `TripService` và `PaymentService`
- **Quy chuẩn trong PDF**: Mục 8.1 (tr. 9) "Bố cục Solution" bắt buộc phải có `tests/TripService.Tests/`.
- **Thực tế trong mã nguồn**: Toàn bộ solution hiện chỉ có duy nhất `tests/MatchingService.Tests/`.
- **Hệ quả**: Các logic trọng yếu như chuyển đổi trạng thái chuyến đi, khóa chống tranh chấp `RowVersion` và cân bằng sổ cái kép không có kiểm thử tự động để bảo vệ chống lỗi hồi quy (regression).

### `GAP-CORE-006` [MỨC ĐỘ TRUNG BÌNH]: Sai lệch trong cách triển khai mẫu thiết kế State Pattern
- **Quy chuẩn trong PDF**: Mục 7 (tr. 8), Listing 6 "Minh họa State Pattern cho vòng đời Trip".
- **Thực tế trong mã nguồn**: `src/TripService/Domain/Enums/TripStatus.cs` (dòng 3–15) sử dụng `enum` thông thường thay vì xây dựng lớp trừu tượng `TripState` cùng các lớp trạng thái cụ thể (`RequestedState`, `AcceptedState`, v.v.).
- **Hệ quả**: Dù về mặt chức năng vẫn có thể kiểm soát trạng thái hợp lệ, nhưng cách làm này không tuân thủ mẫu GoF State Pattern thuần túy được yêu cầu trong đồ án kiến trúc.

### `GAP-CORE-007` [NGHIÊM TRỌNG]: Hoàn toàn chưa có ứng dụng phía người dùng (Mobile / Web)
- **Quy chuẩn trong PDF**: Mục 3.1 (tr. 3) "Presentation Layer: Rider App (Mobile/Web) và Driver App (Mobile)".
- **Thực tế trong mã nguồn**: Thư mục dự án hoàn toàn không có mã nguồn ứng dụng client (không có `package.json`, React, Flutter, v.v.). Hiện tại mới chỉ có cấu hình CORS trong `src/ApiGateway/appsettings.json:16-26`.
- **Hệ quả**: Toàn bộ luồng trải nghiệm người dùng hiện chỉ có thể thao tác thông qua các công cụ kỹ thuật như Swagger, Postman hoặc cURL.

---

## 4. Đánh giá sự tuân thủ các yêu cầu phi chức năng (NFR)

### 4.1 Bảo mật & Kiểm soát truy cập (Mục 9.3, tr. 10)
- **Xác thực (Authentication)**: **Đạt chuẩn**. Triển khai cơ chế JWT Bearer chuẩn mực tại `ApiGateway/Program.cs:36-75`.
- **Lan truyền định danh (Identity Propagation)**: **Đạt chuẩn**. Middleware `HeaderInjectionMiddleware.cs:17-70` ngăn chặn giả mạo token bằng cách chủ động xóa sạch các header `X-User-*` client tự gửi lên và tiêm các claim đã được ký số hợp lệ.
- **Bảo mật dữ liệu thẻ & PCI-DSS**: **Đạt chuẩn kiến trúc**. Thực thể `PaymentToken.cs:3-12` chỉ lưu token đại diện; tuyệt đối không lưu CVV hay số thẻ ngân hàng thô.
- **Kiểm soát lưu lượng (Rate Limiting)**: **Đạt chuẩn**. Cơ chế phân vùng theo IP (100 req/phút cho dịch vụ chung, 10 req/phút cho auth) bảo vệ tốt trước tấn công vét cạn (brute-force) và DoS.

### 4.2 Độ tin cậy & Khả năng chịu lỗi (Mục 9.2, tr. 10)
- **Chống lỗi ghi kép (Transactional Outbox)**: **Đạt một phần**. Hoạt động tốt tại `TripService` (`OutboxPublisherService.cs:1-75` quét định kỳ 5s); đang thiếu background publisher tại `PaymentService`.
- **Chống tranh chấp tài nguyên (Optimistic Concurrency)**: **Đạt chuẩn** tại `TripService` nhờ thuộc tính `RowVersion` (`Trip.cs:32`), giải quyết triệt để lỗi 2 tài xế cùng nhận một cuốc xe.
- **Toàn vẹn sổ cái tài chính**: **Đạt chuẩn thiết kế**. `LedgerEntry.cs:5-16` bắt buộc các bản ghi bất biến với ràng buộc CHECK ($Amount > 0$) đảm bảo nguyên lý cân bằng kế toán.
- **Tính lũy thừa (Idempotency)**: **Đạt một phần**. Đã có cấu trúc bảng trong `PaymentService`; cần bổ sung middleware hoặc filter để chặn các request trùng lặp ngay tại cổng HTTP.

### 4.3 Khả năng giám sát & Truy vết (Mục 9.3, tr. 10)
- **Mã định danh liên kết (Correlation ID)**: **Đạt chuẩn**. `HeaderInjectionMiddleware.cs:25-36` tự động sinh hoặc kế thừa `X-Correlation-Id` xuyên suốt mọi yêu cầu và phản hồi.
- **Chuẩn hóa phản hồi API**: **Đạt chuẩn**. `Shared.Contracts/Common/ApiResponse.cs:1-48` chuẩn hóa tất cả dữ liệu trả về với `StatusCode`, `Message`, `Data` và `TraceId`.
- **Xuất dữ liệu OpenTelemetry**: **Đạt một phần**. Mã nguồn đã sử dụng `Activity.Current?.Id` tiêu chuẩn của .NET, nhưng chưa cấu hình các exporter OTLP kết nối tới Jaeger/Prometheus/Grafana trong `docker-compose.yml`.

---

## 5. Lộ trình hành động khắc phục cụ thể (Remediation Roadmap)

```
Giai đoạn 1: Khắc phục các rào cản cốt lõi phía Backend (Ngày 1–3)
├── 1.1 Triển khai TripEndpoints.cs trong TripService (8 REST endpoints)
├── 1.2 Viết mã cho 7 lớp SignalR MassTransit consumers trong TripService
├── 1.3 Triển khai TripDroppedOffConsumer trong PaymentService (Quyết toán sổ cái kép)
├── 1.4 Triển khai PaymentEndpoints.cs trong PaymentService (4 REST endpoints)
└── 1.5 Xây dựng OutboxPublisherService trong PaymentService

Giai đoạn 2: Tự động hóa kiểm thử & Xác thực chất lượng (Ngày 4–5)
├── 2.1 Tạo dự án tests/TripService.Tests/ (xUnit test cho chuyển đổi trạng thái & RowVersion)
├── 2.2 Tạo dự án tests/PaymentService.Tests/ (xUnit test cho cân bằng sổ cái & Idempotency)
└── 2.3 Chạy kiểm thử tích hợp đầu-cuối (Báo giá -> Đặt xe -> Ghép tài xế -> Nhận chuyến -> Trả khách -> Quyết toán)

Giai đoạn 3: Phát triển tầng ứng dụng người dùng (Ngày 6–10)
├── 3.1 Xây dựng Rider Web/Mobile App (React/Vite hoặc Flutter trên port 5173 / máy ảo Android)
├── 3.2 Xây dựng Driver Mobile App (Port 5174 / máy ảo Android)
└── 3.3 Tích hợp SignalR WebSocket nhận thông báo cuốc và tọa độ GPS thời gian thực

Giai đoạn 4: Giám sát & Hoàn thiện vận hành sản xuất (Ngày 11–12)
├── 4.1 Tích hợp OpenTelemetry OTLP Exporter vào toàn bộ microservices
└── 4.2 Cấu hình Dead-Letter Queue (DLQ) cho RabbitMQ với cơ chế exponential backoff & jitter
```
