# 🗺️ SwiftRide - Sơ Đồ Phân Rã Chức Năng & Các Sơ Đồ Trình Tự Chi Tiết (Functional Decomposition & Sequence Diagrams)

> **Căn cứ tài liệu kiến trúc gốc:** `RideLite-KienTrucPhanMem.pdf` (Bài toán SwiftRide, Kịch bản end-to-end Mục 2.2, Công thức tính giá Mục 2.3, Flowchart vòng đời chuyến đi Mục 4, Chi tiết 3 microservices Mục 5, Nguyên tắc SOLID & Design Patterns Mục 6–7, Trade-off & Yêu cầu chất lượng Mục 9).  
> **Bộ tài liệu thiết kế đồng bộ:**  
> - `FUNCTIONAL_DECOMPOSITION_AND_SEQUENCE_DIAGRAMS.md` (Phân rã chức năng & 15 Sơ đồ trình tự)  
> - `DATABASE_DESIGN.md` (Thiết kế chi tiết 3 CSDL: SQL Server `TripDB`, MongoDB `MatchDB`, PostgreSQL `PayDB`)  
> - `API_CONTRACT_AND_FE_INTEGRATION.md` (Hợp đồng giao tiếp REST API, YARP Route Alias & SignalR Realtime)

## 📑 Cấu Trúc Tài Liệu
1. **Sơ Đồ Cây Phân Rã Chức Năng Hệ Thống (Functional Decomposition Tree)**
2. **Kiến Trúc Triển Khai SignalR Hub & Mock Identity Store (Quy Ước Chung)**
3. **Chi Tiết 15 Sơ Đồ Trình Tự (Sequence Diagrams) Cho Từng Chức Năng**
   - **Phân Hệ 1: Cổng Vào API Gateway (Edge Routing, WebSocket Proxy & Security)**
     - SD 01: F1.1 - Xác thực & Đăng nhập (Auth & JWT Token Issuance)
     - SD 02: F1.2 - Làm mới phiên đăng nhập (Refresh Token)
   - **Phân Hệ 2: Quản Lý Vòng Đời Chuyến Đi (Trip Service - SQL Server `TripDB`)**
     - SD 03: F2.1 - Khởi tạo yêu cầu chuyến xe ở trạng thái `Requested`
     - SD 04: F2.2 - Khách hàng xác nhận hoặc từ chối báo giá (Kích hoạt Transactional Outbox tìm xe)
     - SD 05: F2.3 - Tài xế chấp nhận cuốc xe (Optimistic Concurrency Control với `RowVersion`)
     - SD 06: F2.4 - Tiến trình đón & trả khách (`EnRoute` -> `PickedUp` -> `DroppedOff` & Outbox Thanh toán)
     - SD 07: F2.5 - Hủy chuyến đi trước khi đón (`Cancelled` & Giải phóng tài xế)
     - SD 08: F2.6 - Chuyển trạng thái `Paid` và đóng chuyến đi hoàn tất `Completed` sau thanh toán
   - **Phân Hệ 3: Tìm Xe, Định Vị & Tính Giá Động (Matching & Pricing Service - MongoDB `MatchDB`)**
     - SD 09: F3.1 - Ước lượng cước phí động (Dynamic Pricing & Strategy Pattern)
     - SD 10: F3.2 - Cập nhật vị trí GPS & Trạng thái tài xế (MongoDB `2dsphere` & Stream GPS)
     - SD 11: F3.3 - Tìm kiếm & Điều phối tài xế với vòng lặp thử giới hạn $\le k$ lần (Loại trừ tài xế đã từ chối)
   - **Phân Hệ 4: Thanh Toán, Sổ Cái Kép & Bồi Thường (Payment Service - PostgreSQL `PayDB`)**
     - SD 12: F4.1 - Tự động trừ tiền & Ghi sổ cái kép (Double-entry Ledger Settlement & Outbox)
     - SD 13: F4.2 - Xử lý lỗi thanh toán tạm thời & Retry có giới hạn với Idempotency Key
     - SD 14: F4.3 - Hoàn tiền & Giao dịch bồi thường Saga (Refund & Compensating Transaction)
     - SD 15: F4.4 - Token hóa phương thức thanh toán (PCI-DSS Payment Tokenization)

## 1. Sơ Đồ Cây Phân Rã Chức Năng Hệ Thống (Functional Decomposition Tree)

Bám sát đặc tả kiến trúc tại tài liệu gốc `RideLite-KienTrucPhanMem.pdf`, hệ thống SwiftRide gồm **1 Cổng vào API Gateway (YARP)** và **3 Microservices cốt lõi** với **3 CSDL độc lập (Database-per-Service)**. Toàn bộ 15 chức năng nghiệp vụ được phân rã theo sơ đồ cây dưới đây:

```mermaid
flowchart TD
    Root["Hệ Thống Đặt Xe SwiftRide Platform"]

    %% Nhóm 1: Gateway
    Root --> G1["1. Cổng Vào API Gateway (YARP :5000)"]
    G1 --> F11["F1.1: Xác thực & Đăng nhập (OAuth2 / JWT Bearer)"]
    G1 --> F12["F1.2: Làm mới phiên đăng nhập (Refresh Token)"]

    %% Nhóm 2: Trip Service
    Root --> G2["2. Quản Lý Vòng Đời Chuyến Đi (Trip Service :5105 - SQL Server TripDB)"]
    G2 --> F21["F2.1: Khởi tạo yêu cầu chuyến xe (Status = Requested)"]
    G2 --> F22["F2.2: Khách hàng xác nhận / từ chối báo giá (Kích hoạt Outbox tìm xe)"]
    G2 --> F23["F2.3: Tài xế chấp nhận cuốc xe (State Pattern & RowVersion)"]
    G2 --> F24["F2.4: Tiến trình đón & trả khách (EnRoute -> PickedUp -> DroppedOff)"]
    G2 --> F25["F2.5: Hủy chuyến đi trước khi đón (Trip Cancellation)"]
    G2 --> F26["F2.6: Xác nhận Paid & Đóng chuyến hoàn tất (Completed)"]

    %% Nhóm 3: Matching & Pricing Service
    Root --> G3["3. Tìm Xe, Định Vị & Định Giá (Matching Service :5248 - MongoDB MatchDB)"]
    G3 --> F31["F3.1: Ước lượng cước phí động (Strategy: Standard / Surge / Promo)"]
    G3 --> F32["F3.2: Cập nhật vị trí GPS & Trạng thái tài xế (MongoDB 2dsphere)"]
    G3 --> F33["F3.3: Tìm kiếm & Điều phối tài xế với vòng lặp thử <= k lần"]

    %% Nhóm 4: Payment Service
    Root --> G4["4. Thanh Toán & Sổ Cái Kép (Payment Service :5114 - PostgreSQL PayDB)"]
    G4 --> F41["F4.1: Tự động trừ tiền & Ghi sổ cái kép (Double-entry Ledger)"]
    G4 --> F42["F4.2: Xử lý lỗi tạm thời & Retry có giới hạn với Idempotency Key"]
    G4 --> F43["F4.3: Hoàn tiền & Giao dịch bồi thường Saga (Compensating Transaction)"]
    G4 --> F44["F4.4: Token hóa phương thức thanh toán (PCI-DSS Tokenization)"]
```

## 2. Kiến Trúc Triển Khai SignalR Hub & Mock Identity Store (Quy Ước Chung)

Để tuân thủ nghiêm ngặt giới hạn **tối đa 3 microservices và 3 CSDL** trong Mục 1 và Mục 8.1 của tài liệu PDF, hai thành phần bổ trợ sau được quy ước rõ ràng:

1. **Vị trí của SignalR Hub (`/hubs/trips`):**
   - `TripHub` được đặt bên trong **`Trip Service (:5105)`** (nơi nắm giữ máy trạng thái chuyến đi).
   - **`API Gateway (YARP :5000)`** chỉ đảm nhận vai trò Reverse Proxy chuyển tiếp kết nối WebSocket từ `http://localhost:5000/hubs/trips` vào `http://localhost:5105/hubs/trips` (Gateway hoàn toàn stateless, không kết nối tới RabbitMQ).
   - Khi `Trip Service` cập nhật trạng thái nội bộ hoặc nhận sự kiện tích hợp từ `Matching Service` / `Payment Service` qua RabbitMQ, `Trip Service` sử dụng `IHubContext<TripHub>` để đẩy thông báo realtime qua YARP tới ứng dụng Client.
2. **Cơ chế Xác thực tại Gateway (Mock Identity Store):**
   - Phục vụ phạm vi bài tập lớn mà không phát sinh microservice thứ 4 hay CSDL thứ 4, `API Gateway` tích hợp sẵn một `InMemoryIdentityStore` lưu tập tài khoản mẫu (`Rider`, `Driver`, `Admin`) và danh sách `RefreshToken` đang hoạt động để cấp phát, xác thực JWT Bearer theo chuẩn Mục 8 & Mục 9.3 của PDF.

## 3. Chi Tiết 15 Sơ Đồ Trình Tự (Sequence Diagrams) Cho Từng Chức Năng

### PHÂN HỆ 1: CỔNG VÀO API GATEWAY (EDGE ROUTING & SECURITY)

#### SD 01: F1.1 - Xác thực & Đăng nhập (Auth & JWT Token Issuance)
*Căn cứ tài liệu kiến trúc:* Mục 3.1, Mục 3.2 & Mục 9.3 (API Gateway YARP xác thực OAuth 2.0 / JWT Bearer, định tuyến và chuyển tiếp ngữ cảnh người dùng).

```mermaid
sequenceDiagram
    autonumber
    actor User as Người dùng (Rider / Driver)
    participant GW as API Gateway (YARP :5000 + Mock Identity Store)
    participant Svc as Microservices Nội Bộ (:5105 / :5248 / :5114)

    User->>GW: POST /api/auth/login { email, password, role }
    GW->>GW: Kiểm tra thông tin đăng nhập trên InMemoryIdentityStore
    GW->>GW: Ký Access Token (JWT 15 phút) chứa Claims: sub, role, email<br/>Sinh Refresh Token (7 ngày) lưu vào InMemoryRefreshTokenStore
    GW-->>User: 200 OK: ApiResponse { accessToken, refreshToken, expiresIn, user }

    Note over User,Svc: Ở các request nghiệp vụ tiếp theo
    User->>GW: GET /api/trips/active (Header: Authorization: Bearer <accessToken>)
    GW->>GW: Validate chữ ký, Issuer, Audience & thời hạn JWT
    GW->>Svc: Forward request + Inject Headers nội bộ:<br/>• X-User-Id: <guid><br/>• X-User-Role: Rider / Driver<br/>• X-User-Email: user@swiftride.vn<br/>• X-Correlation-Id: <traceId>
    Svc->>Svc: Kiểm tra quyền truy cập tài nguyên (Authorization check)
    Svc-->>GW: Response JSON (ApiResponse<T>)
    GW-->>User: 200 OK
```

#### SD 02: F1.2 - Làm mới phiên đăng nhập (Refresh Token)
*Căn cứ tài liệu kiến trúc:* Mục 3.2 & Mục 9.3 (Duy trì phiên làm việc an toàn cho Rider App và Driver App).

```mermaid
sequenceDiagram
    autonumber
    actor Client as Client App (Axios Interceptor)
    participant GW as API Gateway (YARP :5000 + Mock Identity Store)

    Note over Client,GW: Client nhận mã lỗi 401 Unauthorized do Access Token hết hạn
    Client->>GW: POST /api/auth/refresh { refreshToken }
    alt Refresh Token hợp lệ và chưa hết hạn trong InMemoryRefreshTokenStore
        GW->>GW: Thu hồi Refresh Token cũ (Token Rotation)<br/>Tạo cặp Access Token & Refresh Token mới
        GW-->>Client: 200 OK: ApiResponse { accessToken, refreshToken, expiresIn }
        Note over Client: Client lưu token mới và tự động gửi lại request bị gián đoạn
    else Refresh Token không tồn tại hoặc đã quá hạn
        GW-->>Client: 401 Unauthorized: { code: "UNAUTHORIZED", message: "Phiên đăng nhập đã hết hạn" }
        Note over Client: Client xóa storage và điều hướng người dùng về màn hình Login
    end
```

### PHÂN HỆ 2: QUẢN LÝ VÒNG ĐỜI CHUYẾN ĐI (TRIP SERVICE - SQL SERVER `TripDB`)

#### SD 03: F2.1 - Khởi tạo yêu cầu chuyến xe (Trip Creation)
*Căn cứ tài liệu kiến trúc:* Mục 2.2 (Bước 1, 2, 3: Rider chọn điểm đón/đến -> Trip Service tạo chuyến ở trạng thái `Requested` trong SQL Server và lấy báo giá từ Matching & Pricing Service) & Mục 4 (Flowchart).

```mermaid
sequenceDiagram
    autonumber
    actor Rider as Rider App
    participant GW as API Gateway (:5000)
    participant Trip as Trip Service (:5105)
    participant Match as Matching & Pricing Service (:5248)
    participant SQL as SQL Server (TripDB)

    Note over Rider,Match: Rider đã lấy báo giá (SD 09) hoặc truyền thông số điểm đón/đến để khởi tạo chuyến
    Rider->>GW: POST /api/trips (Alias: POST /trips)<br/>{ quoteId, pickupLatitude, pickupLongitude, pickupAddress, dropoffLatitude, dropoffLongitude, dropoffAddress, vehicleType }
    GW->>Trip: Forward Request (kèm X-User-Id = RiderId)
    
    opt Nếu Client chưa truyền sẵn báo giá hoặc cần xác minh Quote hợp lệ
        Trip->>Match: GET/POST kiểm tra thông tin báo giá (quoteId / tọa độ)
        Match-->>Trip: Thông tin cự ly d, thời gian t, tổng cước ước tính p
    end

    Trip->>SQL: BEGIN TRANSACTION
    Trip->>SQL: INSERT INTO Trips (Id, RiderId, QuoteId, VehicleType, Status = 'Requested', FareConfirmed = 0, DriverRejectionCount = 0, EstimatedFare = p, CreatedAt = UtcNow)
    Trip->>SQL: INSERT INTO TripStatusHistories (TripId, FromStatus = NULL, ToStatus = 'Requested', Reason = 'Rider created trip request', ChangedAt = UtcNow)
    Trip->>SQL: COMMIT TRANSACTION

    SQL-->>Trip: Lưu thành công (Chờ Rider xác nhận báo giá ở bước kế tiếp)
    Trip-->>GW: 201 Created: ApiResponse (TripDto { status: 'Requested', fareConfirmed: false, estimatedFare: p })
    GW-->>Rider: 201 Created (Hiển thị màn hình xác nhận giá cước cho Rider)
```

#### SD 04: F2.2 - Khách hàng xác nhận hoặc từ chối báo giá (Fare Quote Confirmation / Rejection & Outbox)
*Căn cứ tài liệu kiến trúc:* Mục 2.2 (Bước 4: Giá hiển thị cho Rider. Nếu từ chối -> hủy chuyến; nếu đồng ý -> chuyển sang bước đề xuất cho tài xế), Mục 4 (Flowchart) & Mục 9.2 (Transactional Outbox).

```mermaid
sequenceDiagram
    autonumber
    actor Rider as Rider App
    participant GW as API Gateway (:5000)
    participant Trip as Trip Service (:5105)
    participant SQL as SQL Server (TripDB)
    participant Worker as Outbox Publisher Worker
    participant Bus as RabbitMQ Event Bus

    Note over Rider: Sau khi xem báo giá hiển thị trên màn hình
    alt Trường hợp 1: Rider ĐỒNG Ý giá cước (Kích hoạt tìm tài xế)
        Rider->>GW: POST /api/trips/{id}/confirm-quote { quoteId }
        GW->>Trip: Forward
        Trip->>SQL: BEGIN TRANSACTION
        Trip->>SQL: UPDATE Trips SET FareConfirmed = 1, UpdatedAt = UtcNow WHERE Id = @id AND Status = 'Requested'
        Trip->>SQL: INSERT INTO TripStatusHistories (TripId, FromStatus = 'Requested', ToStatus = 'Requested', Reason = 'Rider confirmed fare quote', ChangedAt = UtcNow)
        Trip->>SQL: INSERT INTO OutboxMessages (Id, EventType = 'TripRequestedEvent', Payload = JSON(tripId, riderId, pickup, dropoff, vehicleType, estimatedFare), CreatedAt = UtcNow)
        Trip->>SQL: COMMIT TRANSACTION
        Trip-->>GW: 200 OK: ApiResponse (TripDto { status: 'Requested', fareConfirmed: true })
        GW-->>Rider: 200 OK (Chuyển sang màn hình Radar chờ ghép tài xế)

        loop Tiến trình Outbox Worker quét mỗi 2 giây
            Worker->>SQL: SELECT TOP 20 * FROM OutboxMessages WHERE ProcessedAt IS NULL ORDER BY CreatedAt
            SQL-->>Worker: Bản ghi TripRequestedEvent
            Worker->>Bus: Publish TripRequestedEvent (tripId, pickupLat, pickupLng, vehicleType, estimatedFare, excludedDriverIds: [])
            Worker->>SQL: UPDATE OutboxMessages SET ProcessedAt = UtcNow WHERE Id = @msgId
        end
    else Trường hợp 2: Rider TỪ CHỐI giá cước (Kết thúc: Hủy chuyến)
        Rider->>GW: PUT /api/trips/{id}/cancel { reason: "Rider từ chối báo giá" }
        GW->>Trip: Forward
        Trip->>SQL: BEGIN TRANSACTION
        Trip->>SQL: UPDATE Trips SET Status = 'Cancelled', CancellationReason = @reason, UpdatedAt = UtcNow WHERE Id = @id AND Status = 'Requested'
        Trip->>SQL: INSERT INTO TripStatusHistories (TripId, FromStatus = 'Requested', ToStatus = 'Cancelled', Reason = @reason, ChangedAt = UtcNow)
        Trip->>SQL: INSERT INTO OutboxMessages (Id, EventType = 'TripCancelledEvent', Payload = JSON(tripId, riderId, reason), CreatedAt = UtcNow)
        Trip->>SQL: COMMIT TRANSACTION
        Trip-->>GW: 200 OK: ApiResponse (TripDto { status: 'Cancelled' })
        GW-->>Rider: 200 OK (Đóng màn hình đặt xe)
    end
```

#### SD 05: F2.3 - Tài xế chấp nhận cuốc xe (Driver Acceptance & Concurrency Control)
*Căn cứ tài liệu kiến trúc:* Mục 2.2 (Bước 5), Mục 6.5 (DIP), Mục 7 (State Pattern & Transactional Outbox) & Kiểm soát đồng thời lạc quan bằng `RowVersion`.

```mermaid
sequenceDiagram
    autonumber
    actor Driver as Driver App
    participant GW as API Gateway (:5000)
    participant Trip as Trip Service (:5105 + SignalR TripHub)
    participant SQL as SQL Server (TripDB)
    participant Worker as Outbox Publisher Worker
    participant Bus as RabbitMQ Event Bus
    actor Rider as Rider App

    Driver->>GW: PUT /api/trips/{id}/accept (Alias: POST /trips/{id}/driver-accept)
    GW->>Trip: Forward (kèm X-User-Id = DriverId)

    Trip->>SQL: SELECT Status, DriverId, FareConfirmed, RowVersion FROM Trips WHERE Id = @id
    SQL-->>Trip: Record (Status = 'Requested', FareConfirmed = 1, DriverId = NULL, RowVersion = @currentVer)

    alt Chuyến đi còn ở trạng thái Requested và RowVersion khớp
        Trip->>Trip: State Pattern: RequestedState.Accept() -> DriverAcceptedState
        Trip->>SQL: BEGIN TRANSACTION
        Trip->>SQL: UPDATE Trips SET Status = 'DriverAccepted', DriverId = @driverId, AcceptedAt = UtcNow, UpdatedAt = UtcNow<br/>WHERE Id = @id AND RowVersion = @currentVer
        SQL-->>Trip: 1 row affected (Thành công)
        Trip->>SQL: INSERT INTO TripStatusHistories (TripId, FromStatus = 'Requested', ToStatus = 'DriverAccepted', ChangedAt = UtcNow)
        Trip->>SQL: INSERT INTO OutboxMessages (Id, EventType = 'TripAcceptedEvent', Payload = JSON(tripId, driverId, riderId), CreatedAt = UtcNow)
        Trip->>SQL: COMMIT TRANSACTION

        Trip-->>Rider: SignalR TripHub Push to Group trip_{id}: TripStatusChanged(DriverAccepted, driverInfo)
        Trip-->>GW: 200 OK: ApiResponse (TripDto { status: 'DriverAccepted', driverId })
        GW-->>Driver: 200 OK (Chuyển sang màn hình điều hướng đi đón khách)

        Worker->>SQL: Đọc OutboxMessages chưa xử lý
        Worker->>Bus: Publish TripAcceptedEvent (tripId, driverId, riderId)
        Worker->>SQL: UPDATE OutboxMessages SET ProcessedAt = UtcNow
    else Chuyến đi đã được tài xế khác nhận trước (0 rows affected do lệch RowVersion)
        SQL-->>Trip: 0 rows affected (DbUpdateConcurrencyException)
        Trip-->>GW: 409 Conflict: { code: "TRIP_ALREADY_ACCEPTED", message: "Cuốc xe đã được nhận bởi tài xế khác" }
        GW-->>Driver: 409 Conflict (Tự động đóng popup mời cuốc)
    end
```

#### SD 06: F2.4 - Tiến trình đón & trả khách (Journey Progression: EnRoute -> PickedUp -> DroppedOff)
*Căn cứ tài liệu kiến trúc:* Mục 2.2 (Bước 6 & Bước 7), Mục 4 (Flowchart: Tài xế đang đến -> Đã đón khách -> Đã trả khách) & Mục 5.1.

```mermaid
sequenceDiagram
    autonumber
    actor Driver as Driver App
    participant GW as API Gateway (:5000)
    participant Trip as Trip Service (:5105 + SignalR TripHub)
    participant SQL as SQL Server (TripDB)
    participant Worker as Outbox Publisher Worker
    participant Bus as RabbitMQ Event Bus
    actor Rider as Rider App

    %% Mốc 1: Đang đến điểm đón
    Note over Driver,Rider: MỐC 1: Tài xế bắt đầu di chuyển tới điểm đón
    Driver->>GW: PUT /api/trips/{id}/start-route
    GW->>Trip: Forward
    Trip->>Trip: State Machine: DriverAccepted -> EnRoute
    Trip->>SQL: BEGIN TRANSACTION
    Trip->>SQL: UPDATE Trips SET Status = 'EnRoute', UpdatedAt = UtcNow WHERE Id = @id
    Trip->>SQL: INSERT INTO TripStatusHistories (TripId, FromStatus = 'DriverAccepted', ToStatus = 'EnRoute', ChangedAt = UtcNow)
    Trip->>SQL: COMMIT TRANSACTION
    Trip-->>Rider: SignalR TripHub Push to Group trip_{id}: TripStatusChanged(EnRoute)
    Trip-->>GW: 200 OK: TripDto { status: 'EnRoute' }
    GW-->>Driver: 200 OK

    %% Mốc 2: Đã đón khách lên xe
    Note over Driver,Rider: MỐC 2: Tài xế gặp khách và đón lên xe
    Driver->>GW: PUT /api/trips/{id}/pick-up (Alias: POST /trips/{id}/pickup)
    GW->>Trip: Forward
    Trip->>Trip: State Machine: EnRoute -> PickedUp
    Trip->>SQL: BEGIN TRANSACTION
    Trip->>SQL: UPDATE Trips SET Status = 'PickedUp', PickedUpAt = UtcNow, UpdatedAt = UtcNow WHERE Id = @id
    Trip->>SQL: INSERT INTO TripStatusHistories (TripId, FromStatus = 'EnRoute', ToStatus = 'PickedUp', ChangedAt = UtcNow)
    Trip->>SQL: COMMIT TRANSACTION
    Trip-->>Rider: SignalR TripHub Push to Group trip_{id}: TripStatusChanged(PickedUp)
    Trip-->>GW: 200 OK: TripDto { status: 'PickedUp' }
    GW-->>Driver: 200 OK

    %% Mốc 3: Đã trả khách tại đích
    Note over Driver,Rider: MỐC 3: Đã đến điểm đích và trả khách (Kích hoạt Outbox Thanh toán)
    Driver->>GW: PUT /api/trips/{id}/drop-off (Alias: POST /trips/{id}/dropoff)
    GW->>Trip: Forward
    Trip->>Trip: State Machine: PickedUp -> DroppedOff
    Trip->>SQL: BEGIN TRANSACTION
    Trip->>SQL: UPDATE Trips SET Status = 'DroppedOff', ActualFare = EstimatedFare, DroppedOffAt = UtcNow, UpdatedAt = UtcNow WHERE Id = @id
    Trip->>SQL: INSERT INTO TripStatusHistories (TripId, FromStatus = 'PickedUp', ToStatus = 'DroppedOff', ChangedAt = UtcNow)
    Trip->>SQL: INSERT INTO OutboxMessages (Id, EventType = 'TripDroppedOffEvent', Payload = JSON(tripId, riderId, driverId, actualFare, idempotencyKey = 'pay_trip_' + tripId), CreatedAt = UtcNow)
    Trip->>SQL: COMMIT TRANSACTION
    Trip-->>Rider: SignalR TripHub Push to Group trip_{id}: TripStatusChanged(DroppedOff)
    Trip-->>GW: 200 OK: TripDto { status: 'DroppedOff' }
    GW-->>Driver: 200 OK (Chuyển sang màn hình chờ quyết toán tự động)

    Worker->>SQL: Quét OutboxMessages chưa xử lý
    Worker->>Bus: Publish TripDroppedOffEvent (tripId, riderId, driverId, actualFare, idempotencyKey = 'pay_trip_' + tripId)
    Worker->>SQL: UPDATE OutboxMessages SET ProcessedAt = UtcNow
```

#### SD 07: F2.5 - Hủy chuyến đi trước khi đón (Trip Cancellation)
*Căn cứ tài liệu kiến trúc:* Mục 4 (Flowchart: Nhánh kết thúc - Hủy chuyến) & Mục 7 (Listing 6: Vòng đời chuyến đi).

```mermaid
sequenceDiagram
    autonumber
    actor User as Rider App / Driver App
    participant GW as API Gateway (:5000)
    participant Trip as Trip Service (:5105 + SignalR TripHub)
    participant SQL as SQL Server (TripDB)
    participant Worker as Outbox Publisher Worker
    participant Bus as RabbitMQ Event Bus
    participant Match as Matching Service (:5248)
    actor Peer as Bên còn lại (Driver / Rider)

    User->>GW: PUT /api/trips/{id}/cancel { reason: "Có việc bận đột xuất" }
    GW->>Trip: Forward (kèm X-User-Id, X-User-Role)
    
    Trip->>SQL: SELECT Status, DriverId, RiderId FROM Trips WHERE Id = @id
    SQL-->>Trip: Record
    alt Trạng thái hợp lệ để hủy (Status IN 'Requested', 'DriverAccepted', 'EnRoute')
        Trip->>Trip: State Machine: Chuyển sang CancelledState
        Trip->>SQL: BEGIN TRANSACTION
        Trip->>SQL: UPDATE Trips SET Status = 'Cancelled', CancellationReason = @reason, UpdatedAt = UtcNow WHERE Id = @id
        Trip->>SQL: INSERT INTO TripStatusHistories (TripId, FromStatus = @oldStatus, ToStatus = 'Cancelled', Reason = @reason, ChangedAt = UtcNow)
        Trip->>SQL: INSERT INTO OutboxMessages (Id, EventType = 'TripCancelledEvent', Payload = JSON(tripId, driverId, riderId, reason), CreatedAt = UtcNow)
        Trip->>SQL: COMMIT TRANSACTION

        Trip-->>Peer: SignalR TripHub Push to Group trip_{id}: TripStatusChanged(Cancelled, reason)
        Trip-->>GW: 200 OK: ApiResponse (TripDto { status: 'Cancelled' })
        GW-->>User: 200 OK (Đã hủy chuyến)

        Worker->>Bus: Publish TripCancelledEvent (tripId, driverId, riderId, reason)
        Bus->>Match: Consume TripCancelledEvent
        opt Nếu chuyến đi đã có tài xế nhận trước đó (driverId != null)
            Match->>Match: Cập nhật MongoDB Drivers: is_available = true (Sẵn sàng nhận cuốc mới)
        end
    else Không thể hủy sau khi khách đã lên xe (Status IN 'PickedUp', 'DroppedOff', 'Paid', 'Completed')
        Trip-->>GW: 409 Conflict: { code: "TRIP_INVALID_TRANSITION", message: "Không thể hủy chuyến sau khi đã đón khách" }
        GW-->>User: 409 Conflict
    end
```

#### SD 08: F2.6 - Xác nhận `Paid` và đóng chuyến đi hoàn tất `Completed` sau thanh toán
*Căn cứ tài liệu kiến trúc:* Mục 2.2 (Bước 8: Khi thanh toán hoàn tất, chuyến đi đóng lại ở trạng thái cuối) & Mục 7 (Listing 6: Chuỗi trạng thái đầy đủ `DroppedOff` / `PaymentPending` -> `Paid` -> `Completed`).

```mermaid
sequenceDiagram
    autonumber
    participant Bus as RabbitMQ Event Bus
    participant Trip as Trip Service (:5105 + SignalR TripHub)
    participant SQL as SQL Server (TripDB)
    participant Match as Matching Service (:5248)
    actor Rider as Rider App
    actor Driver as Driver App

    Note over Bus,Trip: Payment Service xử lý thanh toán xong và bắn PaymentSettledEvent qua Outbox
    Bus->>Trip: Consume PaymentSettledEvent (tripId, transactionId, totalAmount, paymentMethod, driverCreditAmount)
    
    Trip->>SQL: SELECT Status, DriverId, RiderId FROM Trips WHERE Id = @tripId
    SQL-->>Trip: Status = 'DroppedOff' (hoặc 'PaymentPending')

    Trip->>Trip: State Pattern: DroppedOffState -> PaidState -> CompletedState
    Trip->>SQL: BEGIN TRANSACTION
    Trip->>SQL: INSERT INTO TripStatusHistories (TripId, FromStatus = @oldStatus, ToStatus = 'Paid', Reason = 'Payment settled: ' + @transactionId, ChangedAt = UtcNow)
    Trip->>SQL: UPDATE Trips SET Status = 'Completed', CompletedAt = UtcNow, UpdatedAt = UtcNow WHERE Id = @tripId
    Trip->>SQL: INSERT INTO TripStatusHistories (TripId, FromStatus = 'Paid', ToStatus = 'Completed', Reason = 'Trip closed after settlement', ChangedAt = UtcNow)
    Trip->>SQL: INSERT INTO OutboxMessages (Id, EventType = 'TripCompletedEvent', Payload = JSON(tripId, riderId, driverId, totalAmount, transactionId), CreatedAt = UtcNow)
    Trip->>SQL: COMMIT TRANSACTION

    Trip-->>Rider: SignalR TripHub Push to Group trip_{tripId}:<br/>1. TripStatusChanged(Paid)<br/>2. PaymentCompleted(transactionId, totalAmount)<br/>3. TripStatusChanged(Completed)
    Trip-->>Driver: SignalR TripHub Push to Group trip_{tripId}:<br/>1. PaymentCompleted(transactionId, netEarnings = driverCreditAmount)<br/>2. TripStatusChanged(Completed)

    Note over Trip,Match: Outbox Worker phát TripCompletedEvent để giải phóng trạng thái rảnh cho tài xế
    Trip->>Bus: Outbox Publish TripCompletedEvent (tripId, driverId)
    Bus->>Match: Consume TripCompletedEvent
    Match->>Match: Cập nhật MongoDB Drivers: is_available = true
```

### PHÂN HỆ 3: TÌM XE, ĐỊNH VỊ & TÍNH GIÁ ĐỘNG (MATCHING & PRICING SERVICE - MONGODB `MatchDB`)

#### SD 09: F3.1 - Ước lượng cước phí động (Dynamic Pricing & Strategy Pattern)
*Căn cứ tài liệu kiến trúc:* Mục 2.3 (Công thức tính giá), Mục 5.2, Mục 6.1 (SRP), Mục 6.2 (OCP - Listing 2: `IPricingStrategy`), Mục 6.3 (LSP - Listing 3: Giá không âm) & Mục 7 (Strategy Pattern).

$$\text{Công thức gốc trong PDF: } p = \big((c_d \cdot d + c_t \cdot t) \cdot s + c_m\big) \cdot \eta \cdot (1 + T)$$

```mermaid
sequenceDiagram
    autonumber
    actor Rider as Rider App
    participant GW as API Gateway (:5000)
    participant Match as Matching & Pricing Service (:5248)
    participant Mongo as MongoDB (MatchDB)

    Rider->>GW: POST /api/matching/quote (Alias: POST /quote)<br/>{ pickupLatitude, pickupLongitude, dropoffLatitude, dropoffLongitude, vehicleType }
    GW->>Match: Forward request (kèm X-User-Id = RiderId)
    
    Match->>Match: Tính toán cự ly di chuyển d (km) và thời gian ước lượng t (phút)
    Match->>Mongo: db.SurgeZones.findOne({ is_active: true, boundary: { $geoIntersects: { $geometry: pickupPoint } } })
    Mongo-->>Match: SurgeZone config (hệ số giá động s, mặc định s = 1.0 nếu ngoài vùng cao điểm)
    
    Match->>Mongo: db.PricingConfigs.findOne({ vehicle_type: @type, is_active: true })
    Mongo-->>Match: PricingConfig (giá km cd, giá phút ct, phí phụ toll cm, thuế T, hệ số khuyến mãi η)

    Match->>Match: Áp dụng Strategy Pattern (FareCalculator + IPricingStrategy):<br/>StandardPricing -> SurgePricing -> PromoPricing
    Note over Match: p = ((cd * d + ct * t) * s + cm) * η * (1 + T)
    Match->>Match: Kiểm tra hợp đồng hậu điều kiện LSP (Listing 3): Đảm bảo p >= 0

    Match->>Mongo: db.TripQuotes.insertOne({ _id: quoteId, rider_id, vehicle_type, distance_km: d, estimated_duration_minutes: t, pricing_breakdown, total_fare: p, created_at: UtcNow, expires_at: UtcNow + 10m })
    Mongo-->>Match: Lưu thành công (TTL Index tự động dọn dẹp sau 10 phút)

    Match-->>GW: 200 OK: ApiResponse (TripQuoteResponse { quoteId, distanceKm, estimatedDurationMinutes, pricingBreakdown, totalFare, expiresAt })
    GW-->>Rider: 200 OK (Hiển thị bảng phân rã giá cước dự kiến cho khách)
```

#### SD 10: F3.2 - Cập nhật vị trí GPS & Trạng thái tài xế (Driver GPS & Availability Tracking)
*Căn cứ tài liệu kiến trúc:* Mục 3.3 (MongoDB tối ưu cho dữ liệu vị trí động với geo-spatial index `2dsphere`) & Mục 5.2.

```mermaid
sequenceDiagram
    autonumber
    actor Driver as Driver App (Background Service)
    participant GW as API Gateway (:5000)
    participant Match as Matching Service (:5248)
    participant Mongo as MongoDB (MatchDB)
    participant Bus as RabbitMQ Event Bus
    participant Trip as Trip Service (:5105 + SignalR TripHub)
    actor Rider as Rider App

    loop Chu kỳ mỗi 10 giây (khi tài xế Online / Đang chở khách)
        Driver->>GW: PUT /api/drivers/{id}/location<br/>{ "latitude": 10.762887, "longitude": 106.682145, "bearing": 180, "speed": 35, "activeTripId": "f47ac10b..." }
        GW->>Match: Forward (kèm X-User-Id = DriverId)
        Match->>Mongo: db.Drivers.updateOne(<br/>  { driver_id: @driverId },<br/>  { $set: { location: { type: "Point", coordinates: [lng, lat] }, bearing: @bearing, speed: @speed, last_location_updated_at: UtcNow } }<br/>)
        Mongo-->>Match: Đã cập nhật tọa độ GeoJSON Point
        Match-->>GW: 200 OK: ApiResponse { status: "Success" }
        GW-->>Driver: 200 OK

        opt Nếu tài xế đang thực hiện cuốc xe (activeTripId != null)
            Match->>Bus: Publish DriverLocationUpdatedEvent (tripId, driverId, lat, lng, bearing, speed, updatedAt)
            Bus->>Trip: Consume DriverLocationUpdatedEvent
            Trip-->>Rider: SignalR TripHub Push to Group trip_{tripId}: DriverLocationUpdated(driverId, lat, lng, bearing, speed, updatedAt)
            Note over Rider: Frontend nội suy chuyển động (smooth animation) Marker xe trên bản đồ
        end
    end

    opt Khi tài xế Bật / Tắt trạng thái sẵn sàng nhận cuốc
        Driver->>GW: PUT /api/drivers/{id}/availability { isAvailable: true / false }
        GW->>Match: Forward
        Match->>Mongo: db.Drivers.updateOne({ driver_id: @driverId }, { $set: { is_available: @val } })
        Mongo-->>Match: Đã cập nhật trạng thái
        Match-->>GW: 200 OK
        GW-->>Driver: 200 OK
    end
```

#### SD 11: F3.3 - Tìm kiếm & Điều phối tài xế với vòng lặp thử giới hạn $\le k$ lần (Driver Matching Engine)
*Căn cứ tài liệu kiến trúc:* Mục 2.2 (Bước 5: Nếu tài xế từ chối và chưa quá $k$ lần thử $\Rightarrow$ quay lại tìm tài xế khác; nếu vượt quá $k$ lần $\Rightarrow$ chuyến không thành công), Mục 4 (Flowchart) & Mục 5.2 (Truy vấn `2dsphere` trên MongoDB).

```mermaid
sequenceDiagram
    autonumber
    participant Bus as RabbitMQ Event Bus
    participant Match as Matching Service (:5248)
    participant Mongo as MongoDB (MatchDB)
    participant Trip as Trip Service (:5105 + SignalR TripHub + TripDB)
    participant GW as API Gateway (:5000)
    actor Driver as Driver App
    actor Rider as Rider App

    Note over Bus,Match: Khi khách xác nhận báo giá (SD 04) hoặc khi tài xế trước từ chối nhưng số lần thử <= k
    Bus->>Match: Consume TripRequestedEvent / RetryMatchingCommand<br/>(tripId, pickupCoordinates, vehicleType, estimatedFare, attempt, excludedDriverIds)

    Match->>Mongo: db.Drivers.findOne({<br/>  is_active: true, is_available: true,<br/>  "vehicle.type": @vehicleType,<br/>  driver_id: { $nin: excludedDriverIds },<br/>  location: { $nearSphere: { $geometry: pickupPoint, $maxDistance: 5000 } }<br/>})
    Mongo-->>Match: Tài xế gần nhất chưa nằm trong danh sách loại trừ (excludedDriverIds)
    
    alt Tìm thấy tài xế khả dụng
        Match->>Mongo: Tạm khóa trạng thái tài xế: db.Drivers.updateOne({ driver_id }, { $set: { is_available: false } })
        Match->>Bus: Publish TripOfferDispatchedEvent (tripId, driverId, pickup, dropoff, fare, countdownSeconds = 15)
        Bus->>Trip: Consume TripOfferDispatchedEvent
        Trip-->>Driver: SignalR TripHub Push to Group driver_{driverId}: ReceiveTripOffer (Đếm ngược 15s)
        Note over Driver: Bật Popup mời cuốc: Đếm ngược 15s, hiển thị điểm đón, điểm đến và cước phí

        alt Trường hợp 1: Tài xế BẤM CHẤP NHẬN trong 15s
            Driver->>GW: PUT /api/trips/{id}/accept (Xem chi tiết SD 05)
            GW->>Trip: Cập nhật Status = 'DriverAccepted' & bắn TripAcceptedEvent
            Note over Match,Trip: Hoàn tất ghép xe thành công
        else Trường hợp 2: Tài xế BẤM TỪ CHỐI hoặc HẾT HẠN 15s không phản hồi
            Driver->>GW: PUT /api/trips/{id}/reject { reason: "Không tiện đường" } (hoặc Timeout Worker kích hoạt sau 15s)
            GW->>Trip: Forward tới Trip Service
            Trip->>Trip: Tăng DriverRejectionCount = DriverRejectionCount + 1,<br/>thêm driverId vào ExcludedDriverIds trong TripDB
            Trip->>Bus: Publish DriverRejectedOfferEvent (tripId, driverId) để Matching mở lại is_available = true cho tài xế cũ

            alt Chưa vượt quá k lần thử (DriverRejectionCount <= k, ví dụ k = 3)
                Trip->>Bus: Outbox Publish RetryMatchingCommand (tripId, attempt = DriverRejectionCount + 1, excludedDriverIds)
                Note over Match,Trip: Quay lại đầu kịch bản SD 11 để tìm tài xế tiếp theo (loại trừ các tài xế đã từ chối)
            else Đã vượt quá k lần thử (DriverRejectionCount > k)
                Note over Trip: Vượt quá k lần thử theo quy định Flowchart Mục 4 PDF
                Trip->>Trip: State Machine: Chuyển trạng thái sang Status = 'Failed' (Kết thúc: Không thành công)
                Trip-->>Rider: SignalR TripHub Push to Group trip_{tripId}: TripStatusChanged(Failed, "Các tài xế quanh khu vực hiện đang bận")
            end
        end
    else Không còn tài xế nào khả dụng trong bán kính tìm kiếm
        Match->>Bus: Publish MatchingFailedEvent (tripId, reason: "Không tìm thấy tài xế khả dụng quanh khu vực")
        Bus->>Trip: Consume MatchingFailedEvent
        Trip->>Trip: Cập nhật TripDB: Status = 'Failed', ghi TripStatusHistories
        Trip-->>Rider: SignalR TripHub Push to Group trip_{tripId}: TripStatusChanged(Failed, "Không tìm thấy tài xế quanh khu vực")
    end
```

### PHÂN HỆ 4: THANH TOÁN, SỔ CÁI KÉP & BỒI THƯỜNG (PAYMENT SERVICE - POSTGRESQL `PayDB`)

#### SD 12: F4.1 - Tự động trừ tiền & Ghi sổ cái kép (Double-entry Ledger Settlement)
*Căn cứ tài liệu kiến trúc:* Mục 2.2 (Bước 7), Mục 3.3 (PostgreSQL đảm bảo sổ cái giao dịch kiểu double-entry, kiểm toán và nhất quán tiền bạc), Mục 5.3 (Payment Service bắt buộc) & Mục 9.2 (Transactional Outbox & Idempotent Consumer).

```mermaid
sequenceDiagram
    autonumber
    participant Bus as RabbitMQ Event Bus
    participant Pay as Payment Service (:5114)
    participant Postgres as PostgreSQL (PayDB)
    participant Gateway as Cổng Thanh Toán Đối Tác (Momo / VNPay / Stripe)
    participant Worker as Outbox Publisher Worker

    Bus->>Pay: Consume TripDroppedOffEvent (tripId, riderId, driverId, actualFare, paymentMethod, idempotencyKey = "pay_trip_" + tripId)

    Pay->>Postgres: SELECT * FROM idempotency_records WHERE idempotency_key = @key
    alt Khóa Idempotency đã tồn tại (Message trùng lặp do at-least-once delivery)
        Postgres-->>Pay: Đã xử lý trước đó (status = 'Success')
        Note over Pay: Bỏ qua xử lý lặp, đảm bảo tuyệt đối không trừ tiền 2 lần
    else Khóa mới hoàn toàn (Chưa từng xử lý)
        Pay->>Postgres: INSERT INTO idempotency_records (idempotency_key, status = 'Processing', created_at = now())

        alt Phương thức 1: Thanh toán qua Cổng ngoài đã Token hóa (Momo / VNPay / Stripe)
            Pay->>Postgres: SELECT token FROM payment_tokens WHERE user_id = @riderId AND is_default = true
            Pay->>Gateway: Gửi lệnh trừ tiền qua Token (kèm Idempotency Key)
            Gateway-->>Pay: Trừ tiền cổng ngoài thành công (external_ref_id)
            Note over Pay,Postgres: Tiền thu từ cổng ngoài được ghi Nợ (Debit) vào tài khoản trung gian EscrowHolding
        else Phương thức 2: Thanh toán bằng Ví nội bộ (Wallet)
            Pay->>Postgres: Kiểm tra số dư bảng accounts (owner_id = @riderId, account_type = 'WalletLiability') >= actualFare
            Note over Pay,Postgres: Tiền trừ từ ví nội bộ được ghi Nợ (Debit) trực tiếp vào tài khoản RiderWallet
        end

        Pay->>Postgres: BEGIN TRANSACTION
        Pay->>Postgres: INSERT INTO transactions (id, trip_id, transaction_type = 'RidePayment', total_amount = actualFare, status = 'Success')
        Note over Pay,Postgres: Ghi 3 bút toán Sổ Cái Kép bất biến (Định luật: Tổng Debit = Tổng Credit)
        Pay->>Postgres: 1. INSERT INTO ledger_entries (account_id = DebitAccount [RiderWallet hoặc EscrowHolding], entry_type = 'Debit', amount = actualFare)
        Pay->>Postgres: 2. INSERT INTO ledger_entries (account_id = DriverWallet, entry_type = 'Credit', amount = actualFare * 0.80)
        Pay->>Postgres: 3. INSERT INTO ledger_entries (account_id = PlatformRevenue, entry_type = 'Credit', amount = actualFare * 0.20)
        Pay->>Postgres: UPDATE accounts SET balance = balance - actualFare WHERE id = DebitAccount
        Pay->>Postgres: UPDATE accounts SET balance = balance + (actualFare * 0.80) WHERE id = DriverWallet
        Pay->>Postgres: UPDATE accounts SET balance = balance + (actualFare * 0.20) WHERE id = PlatformRevenue
        Pay->>Postgres: UPDATE idempotency_records SET status = 'Success' WHERE idempotency_key = @key
        Pay->>Postgres: INSERT INTO outbox_messages (id, event_type = 'PaymentSettledEvent', payload = JSON(tripId, transactionId, actualFare, driverCreditAmount))
        Pay->>Postgres: COMMIT TRANSACTION

        Worker->>Postgres: Đọc outbox_messages chưa xử lý
        Worker->>Bus: Publish PaymentSettledEvent (tripId, transactionId, actualFare, driverCreditAmount)
        Worker->>Postgres: UPDATE outbox_messages SET processed_at = now()
    end
```

#### SD 13: F4.2 - Xử lý lỗi thanh toán tạm thời & Retry có giới hạn với Idempotency Key
*Căn cứ tài liệu kiến trúc:* Mục 4 (Flowchart: Thanh toán lỗi / chờ xử lý $\to$ retry có giới hạn với cùng một idempotency key; nếu vẫn lỗi thì giữ trạng thái chờ xử lý thay vì trừ tiền lặp hoặc báo thành công sai) & Mục 9.2 (Exponential backoff + jitter).

```mermaid
sequenceDiagram
    autonumber
    participant Bus as RabbitMQ Event Bus
    participant Pay as Payment Service (:5114)
    participant Gateway as Cổng Thanh Toán / Ví
    participant Postgres as PostgreSQL (PayDB)
    participant Worker as Outbox Publisher Worker
    participant Trip as Trip Service (:5105 + SignalR TripHub)
    actor Rider as Rider App

    Bus->>Pay: Consume TripDroppedOffEvent (tripId, actualFare, idempotencyKey = "pay_trip_" + tripId)
    Pay->>Gateway: Gửi yêu cầu trừ tiền (Header: X-Idempotency-Key = "pay_trip_" + tripId)
    Gateway-->>Pay: Lỗi mạng tạm thời (504 Gateway Timeout / Socket Error)

    Note over Pay,Gateway: Bắt đầu Retry có giới hạn với CÙNG một Idempotency Key (Exponential Backoff + Jitter)
    loop Retry giới hạn (Lần 1: 2s, Lần 2: 4s, Lần 3: 8s)
        Pay->>Gateway: Thử lại trừ tiền (Giữ nguyên X-Idempotency-Key = "pay_trip_" + tripId)
        Gateway-->>Pay: Vẫn gặp lỗi kết nối tạm thời
    end

    Note over Pay,Postgres: Vượt quá số lần thử lại tối đa mà vẫn lỗi
    Pay->>Postgres: BEGIN TRANSACTION
    Pay->>Postgres: INSERT INTO transactions (id, trip_id, transaction_type = 'RidePayment', total_amount = actualFare, status = 'PaymentPending', error_detail = 'Gateway Timeout after 3 retries')
    Pay->>Postgres: UPDATE idempotency_records SET status = 'Failed' WHERE idempotency_key = @key
    Pay->>Postgres: INSERT INTO outbox_messages (id, event_type = 'PaymentFailedEvent', payload = JSON(tripId, status = 'PaymentPending', reason = 'Gateway Timeout'))
    Pay->>Postgres: COMMIT TRANSACTION

    Worker->>Bus: Publish PaymentFailedEvent (tripId, status = 'PaymentPending')
    Bus->>Trip: Consume PaymentFailedEvent
    Trip->>Trip: State Machine: Chuyển trạng thái chuyến đi sang Status = 'PaymentPending' (Lưu TripDB & TripStatusHistories)
    Trip-->>Rider: SignalR TripHub Push to Group trip_{tripId}: TripStatusChanged(PaymentPending)
    Note over Trip,Rider: Chuyến đi giữ ở trạng thái Chờ xử lý (cho phép thanh toán lại qua API POST /api/payments/process).<br/>Tuyệt đối không trừ tiền lặp và không báo thành công sai!
```

#### SD 14: F4.3 - Hoàn tiền & Giao dịch bồi thường Saga (Refund & Compensating Transaction)
*Căn cứ tài liệu kiến trúc:* Mục 5.3 (Payment Service xử lý hoàn tiền và bồi thường khi một Saga thất bại) & Mục 9.2 (Saga điều phối nhiều bước, định nghĩa hành động bồi thường và kết hợp Outbox).

```mermaid
sequenceDiagram
    autonumber
    actor Admin as Quản Trị Viên / Hệ Thống Saga Coordinator
    participant GW as API Gateway (:5000)
    participant Pay as Payment Service (:5114)
    participant Postgres as PostgreSQL (PayDB)
    participant Gateway as Cổng Thanh Toán Đối Tác (Momo/VNPay/Stripe)
    participant Worker as Outbox Publisher Worker
    participant Bus as RabbitMQ Event Bus

    Admin->>GW: POST /api/payments/{transactionId}/refund (Alias: POST /payments/{id}/refund)<br/>{ reason: "Saga bù trừ do chuyến đi gặp sự cố bất khả kháng" }
    GW->>Pay: Forward Request
    
    Pay->>Postgres: SELECT * FROM transactions WHERE id = @id AND status = 'Success'
    Postgres-->>Pay: Thông tin giao dịch gốc (trip_id, total_amount = fare)

    opt Nếu giao dịch gốc thu qua cổng thanh toán ngoài
        Pay->>Gateway: Gửi lệnh hoàn tiền về nguồn thẻ/ví của khách
        Gateway-->>Pay: Hoàn tiền thành công (refund_ref_id)
    end

    Pay->>Postgres: BEGIN TRANSACTION
    Pay->>Postgres: UPDATE transactions SET status = 'Refunded' WHERE id = @id
    Pay->>Postgres: INSERT INTO transactions (id = newRefundTxId, trip_id, transaction_type = 'Refund', total_amount = fare, status = 'Success', description = @reason)
    Note over Pay,Postgres: Ghi 3 Bút toán Đảo Sổ Cái bất biến (Compensating Ledger Entries - Không sửa/xóa dòng cũ)
    Pay->>Postgres: 1. INSERT INTO ledger_entries (transaction_id = newRefundTxId, account_id = CreditAccount [RiderWallet hoặc EscrowHolding], entry_type = 'Credit', amount = fare)
    Pay->>Postgres: 2. INSERT INTO ledger_entries (transaction_id = newRefundTxId, account_id = DriverWallet, entry_type = 'Debit', amount = fare * 0.80)
    Pay->>Postgres: 3. INSERT INTO ledger_entries (transaction_id = newRefundTxId, account_id = PlatformRevenue, entry_type = 'Debit', amount = fare * 0.20)
    Pay->>Postgres: UPDATE accounts SET balance = balance + fare WHERE id = CreditAccount
    Pay->>Postgres: UPDATE accounts SET balance = balance - (fare * 0.80) WHERE id = DriverWallet
    Pay->>Postgres: UPDATE accounts SET balance = balance - (fare * 0.20) WHERE id = PlatformRevenue
    Pay->>Postgres: INSERT INTO outbox_messages (id, event_type = 'PaymentRefundedEvent', payload = JSON(transactionId, tripId, refundAmount = fare))
    Pay->>Postgres: COMMIT TRANSACTION

    Pay-->>GW: 200 OK: ApiResponse { transactionId, refundAmount: fare, status: 'Refunded' }
    GW-->>Admin: 200 OK

    Worker->>Bus: Publish PaymentRefundedEvent (transactionId, tripId, refundAmount)
```

#### SD 15: F4.4 - Token hóa phương thức thanh toán (PCI-DSS Payment Tokenization)
*Căn cứ tài liệu kiến trúc:* Mục 5.3 & Mục 9.3 (Payment Service chỉ lưu token do cổng thanh toán cấp, tuyệt đối không lưu hay log số thẻ/CVV để đạt chuẩn an toàn bảo mật PCI-DSS).

```mermaid
sequenceDiagram
    autonumber
    actor Rider as Rider App
    participant ThirdParty as Cổng Thanh Toán Đối Tác (Momo / VNPay / Stripe)
    participant GW as API Gateway (:5000)
    participant Pay as Payment Service (:5114)
    participant Postgres as PostgreSQL (PayDB)

    Rider->>ThirdParty: Nhập thông tin thẻ ngân hàng / Ủy quyền liên kết ví điện tử
    ThirdParty->>ThirdParty: Mã hóa và lưu trữ thông tin thẻ đạt chuẩn PCI-DSS
    ThirdParty-->>Rider: Trả về Token đại diện (Ví dụ: "tok_momo_88a91b2c3d")
    Note over Rider,ThirdParty: Ứng dụng và Backend SwiftRide không bao giờ lưu số thẻ hay CVV!

    Rider->>GW: POST /api/payment-tokens<br/>{ provider: "Momo", token: "tok_momo_88a91b2c3d", isDefault: true }
    GW->>Pay: Forward (kèm X-User-Id = RiderId)

    Pay->>Pay: Áp dụng Factory Pattern (PaymentMethodFactory) & Tạo Masked Token ("**** 5678")
    Pay->>Postgres: BEGIN TRANSACTION
    opt Nếu isDefault == true
        Pay->>Postgres: UPDATE payment_tokens SET is_default = false WHERE user_id = @userId
    end
    Pay->>Postgres: INSERT INTO payment_tokens (id, user_id, provider, token, masked_token, is_default, created_at)<br/>VALUES (gen_random_uuid(), @userId, 'Momo', @token, '**** 5678', true, now())
    Pay->>Postgres: COMMIT TRANSACTION
    Postgres-->>Pay: Đã lưu token thành công

    Pay-->>GW: 201 Created: ApiResponse (PaymentTokenDto { id, provider: "Momo", maskedToken: "**** 5678", isDefault: true })
    GW-->>Rider: 201 Created (Hiển thị phương thức thanh toán đã liên kết trên màn hình)
```
