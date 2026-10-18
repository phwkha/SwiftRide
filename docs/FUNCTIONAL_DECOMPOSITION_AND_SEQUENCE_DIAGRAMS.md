# 🗺️ SwiftRide - Sơ Đồ Phân Rã Chức Năng & Các Sơ Đồ Trình Tự Chi Tiết (Functional Decomposition & Sequence Diagrams)

> **Căn cứ tài liệu kiến trúc:** [`docs/RideLite-KienTrucPhanMem.pdf`](RideLite-KienTrucPhanMem.pdf) (Tài liệu gốc: Bài toán SwiftRide, Kịch bản end-to-end Mục 2.2, Công thức tính giá Mục 2.3, Flowchart vòng đời chuyến đi Mục 4, Chi tiết 3 microservices Mục 5).  
> **Tài liệu liên quan:**  
> - Bảng điều phối tổng quan: [`docs/README.md`](README.md)  
> - Thiết kế CSDL chi tiết: [`docs/DATABASE_DESIGN.md`](DATABASE_DESIGN.md)  
> - Hợp đồng API & Bàn giao Frontend: [`docs/API_CONTRACT_AND_FE_INTEGRATION.md`](API_CONTRACT_AND_FE_INTEGRATION.md)  
> - Kế hoạch triển khai Sprint 10 ngày: [`docs/DEVELOPMENT_PLAN.md`](DEVELOPMENT_PLAN.md)  

---

## 📑 MỤC LỤC
1. [Sơ Đồ Cây Phân Rã Chức Năng Hệ Thống (Functional Decomposition Tree)](#1-sơ-đồ-cây-phân-rã-chức-năng-hệ-thống-functional-decomposition-tree)
2. [Chi Tiết 15 Sơ Đồ Trình Tự (Sequence Diagrams) Cho Từng Chức Năng](#2-chi-tiết-15-sơ-đồ-trình-tự-sequence-diagrams-cho-từng-chức-năng)
   - **Phân Hệ 1: Cổng Vào API Gateway (Edge Routing & Security)**
     - [SD 01: F1.1 - Xác thực & Đăng nhập (Auth & JWT Token Issuance)](#sd-01-f11---xác-thực--đăng-nhập-auth--jwt-token-issuance)
     - [SD 02: F1.2 - Làm mới phiên đăng nhập (Refresh Token)](#sd-02-f12---làm-mới-phiên-đăng-nhập-refresh-token)
   - **Phân Hệ 2: Quản Lý Vòng Đời Chuyến Đi (Trip Service - SQL Server `TripDB`)**
     - [SD 03: F2.1 - Khởi tạo yêu cầu chuyến xe (Trip Creation & Transactional Outbox)](#sd-03-f21---khởi-tạo-yêu-cầu-chuyến-xe-trip-creation--transactional-outbox)
     - [SD 04: F2.2 - Khách hàng xác nhận hoặc từ chối báo giá (Fare Quote Confirmation / Rejection)](#sd-04-f22---khách-hàng-xác-nhận-hoặc-từ-chối-báo-giá-fare-quote-confirmation--rejection)
     - [SD 05: F2.3 - Tài xế chấp nhận cuốc xe (Driver Acceptance & Concurrency Control)](#sd-05-f23---tài-xế-chấp-nhận-cuốc-xe-driver-acceptance--concurrency-control)
     - [SD 06: F2.4 - Tiến trình đón & trả khách (Journey Progression: EnRoute -> PickedUp -> DroppedOff)](#sd-06-f24---tiến-trình-đón--trả-khách-journey-progression-enroute---pickedup---droppedoff)
     - [SD 07: F2.5 - Hủy chuyến đi trước khi đón (Trip Cancellation)](#sd-07-f25---hủy-chuyến-đi-trước-khi-đón-trip-cancellation)
     - [SD 08: F2.6 - Đóng chuyến đi hoàn tất sau thanh toán (Trip Completion)](#sd-08-f26---đóng-chuyến-đi-hoàn-tất-sau-thanh-toán-trip-completion)
   - **Phân Hệ 3: Tìm Xe, Định Vị & Tính Giá Động (Matching & Pricing Service - MongoDB `MatchDB`)**
     - [SD 09: F3.1 - Ước lượng cước phí động (Dynamic Pricing & Strategy Pattern)](#sd-09-f31---ước-lượng-cước-phí-động-dynamic-pricing--strategy-pattern)
     - [SD 10: F3.2 - Cập nhật vị trí GPS & Trạng thái tài xế (Driver GPS & Availability Tracking)](#sd-10-f32---cập-nhật-vị-trí-gps--trạng-thái-tài-xế-driver-gps--availability-tracking)
     - [SD 11: F3.3 - Tìm kiếm & Điều phối tài xế với vòng lặp thử giới hạn <= k lần (Driver Matching Engine)](#sd-11-f33---tìm-kiếm--điều-phối-tài-xế-với-vòng-lặp-thử-giới-hạn--k-lần-driver-matching-engine)
   - **Phân Hệ 4: Thanh Toán, Sổ Cái Kép & Bồi Thường (Payment Service - PostgreSQL `PayDB`)**
     - [SD 12: F4.1 - Tự động trừ tiền & Ghi sổ cái kép (Double-entry Ledger Settlement)](#sd-12-f41---tự-động-trừ-tiền--ghi-sổ-cái-kép-double-entry-ledger-settlement)
     - [SD 13: F4.2 - Xử lý lỗi thanh toán tạm thời & Retry có giới hạn với Idempotency Key](#sd-13-f42---xử-lý-lỗi-thanh-toán-tạm-thời--retry-có-giới-hạn-với-idempotency-key)
     - [SD 14: F4.3 - Hoàn tiền & Giao dịch bồi thường Saga (Refund & Compensating Transaction)](#sd-14-f43---hoàn-tiền--giao-dịch-bồi-thường-saga-refund--compensating-transaction)
     - [SD 15: F4.4 - Token hóa phương thức thanh toán (PCI-DSS Payment Tokenization)](#sd-15-f44---token-hóa-phương-thức-thanh-toán-pci-dss-payment-tokenization)

---

## 1. Sơ Đồ Cây Phân Rã Chức Năng Hệ Thống (Functional Decomposition Tree)

Bám sát đặc tả kiến trúc tại tài liệu gốc [`docs/RideLite-KienTrucPhanMem.pdf`](RideLite-KienTrucPhanMem.pdf), hệ thống SwiftRide gồm **1 Cổng vào API Gateway (YARP)** và **3 Microservices cốt lõi**. Toàn bộ 15 chức năng nghiệp vụ được phân rã rành mạch theo sơ đồ cây dưới đây:

```mermaid
flowchart TD
    Root["Hệ Thống Đặt Xe SwiftRide Platform"]

    %% Nhóm 1: Gateway
    Root --> G1["1. Cổng Vào API Gateway (YARP)"]
    G1 --> F11["F1.1: Xác thực & Đăng nhập (Auth & JWT Token)"]
    G1 --> F12["F1.2: Làm mới phiên đăng nhập (Refresh Token)"]

    %% Nhóm 2: Trip Service
    Root --> G2["2. Quản Lý Vòng Đời Chuyến Đi (Trip Service - SQL Server)"]
    G2 --> F21["F2.1: Khởi tạo yêu cầu chuyến xe (Trip Creation & Outbox)"]
    G2 --> F22["F2.2: Khách hàng xác nhận / từ chối báo giá (Quote Confirm/Reject)"]
    G2 --> F23["F2.3: Tài xế chấp nhận cuốc xe (Driver Acceptance & RowVersion)"]
    G2 --> F24["F2.4: Tiến trình hành trình đón & trả khách (EnRoute -> PickedUp -> DroppedOff)"]
    G2 --> F25["F2.5: Hủy chuyến đi trước khi đón (Trip Cancellation)"]
    G2 --> F26["F2.6: Đóng chuyến đi hoàn tất sau thanh toán (Trip Completion)"]

    %% Nhóm 3: Matching & Pricing Service
    Root --> G3["3. Tìm Xe, Định Vị & Định Giá (Matching Service - MongoDB)"]
    G3 --> F31["F3.1: Ước lượng cước phí động (Dynamic Pricing: Standard / Surge / Promo)"]
    G3 --> F32["F3.2: Cập nhật vị trí GPS & Trạng thái tài xế (MongoDB 2dsphere)"]
    G3 --> F33["F3.3: Tìm kiếm & Điều phối tài xế với vòng lặp thử <= k lần"]

    %% Nhóm 4: Payment Service
    Root --> G4["4. Thanh Toán & Sổ Cái Kép (Payment Service - PostgreSQL)"]
    G4 --> F41["F4.1: Tự động trừ tiền & Ghi sổ cái kép (Double-entry Ledger)"]
    G4 --> F42["F4.2: Xử lý lỗi tạm thời & Retry với Idempotency Key"]
    G4 --> F43["F4.3: Hoàn tiền & Giao dịch bồi thường Saga (Compensating Transaction)"]
    G4 --> F44["F4.4: Token hóa phương thức thanh toán (PCI-DSS Tokenization)"]
```

---

## 2. Chi Tiết 15 Sơ Đồ Trình Tự (Sequence Diagrams) Cho Từng Chức Năng

Mỗi chức năng trong cây phân rã trên đều có một sơ đồ trình tự tương ứng thể hiện chính xác luồng tương tác giữa Client, API Gateway, Microservice nghiệp vụ, CSDL riêng biệt và RabbitMQ Event Bus.

---

### PHÂN HỆ 1: CỔNG VÀO API GATEWAY (EDGE ROUTING & SECURITY)

#### SD 01: F1.1 - Xác thực & Đăng nhập (Auth & JWT Token Issuance)
*Căn cứ tài liệu kiến trúc:* Mục 3.1 & Mục 3.2 (API Gateway YARP đảm nhận xác thực JWT và chuyển tiếp định tuyến).

```mermaid
sequenceDiagram
    autonumber
    actor User as Người dùng (Rider / Driver)
    participant GW as API Gateway (YARP :5000)
    participant Svc as Microservices Nội Bộ (:5105 / :5248 / :5114)

    User->>GW: POST /api/auth/login { email, password, role }
    GW->>GW: Xác thực thông tin đăng nhập
    GW->>GW: Tạo Access Token (JWT 15p) kèm Claims: id, role, email<br/>Tạo Refresh Token (7 ngày)
    GW-->>User: 200 OK: ApiResponse { accessToken, refreshToken, expiresIn, user }

    Note over User,Svc: Ở các request nghiệp vụ tiếp theo
    User->>GW: GET /api/trips/active (Header: Authorization: Bearer <accessToken>)
    GW->>GW: Validate chữ ký & thời hạn JWT
    GW->>Svc: Forward request + Inject Headers nội bộ:<br/>• X-User-Id: <guid><br/>• X-User-Role: Rider / Driver<br/>• X-User-Email: user@example.com
    Svc-->>GW: Response JSON (ApiResponse<T>)
    GW-->>User: 200 OK
```

---

#### SD 02: F1.2 - Làm mới phiên đăng nhập (Refresh Token)
*Căn cứ tài liệu kiến trúc:* Mục 3.2 (Duy trì phiên làm việc liên tục cho Rider App và Driver App).

```mermaid
sequenceDiagram
    autonumber
    actor Client as Client App (Axios Interceptor)
    participant GW as API Gateway (YARP :5000)

    Note over Client,GW: Client nhận mã lỗi 401 Unauthorized do Access Token hết hạn
    Client->>GW: POST /api/auth/refresh { refreshToken }
    alt Refresh Token hợp lệ và chưa hết hạn
        GW->>GW: Tạo cặp Access Token & Refresh Token mới
        GW-->>Client: 200 OK: ApiResponse { accessToken, refreshToken, expiresIn }
        Note over Client: Client lưu token mới và tự động gửi lại request bị lỗi trước đó
    else Refresh Token không hợp lệ hoặc đã quá hạn
        GW-->>Client: 401 Unauthorized: { code: "UNAUTHORIZED", message: "Phiên đăng nhập đã hết hạn" }
        Note over Client: Client xóa storage và điều hướng người dùng về màn hình Login
    end
```

---

### PHÂN HỆ 2: QUẢN LÝ VÒNG ĐỜI CHUYẾN ĐI (TRIP SERVICE - SQL SERVER `TripDB`)

#### SD 03: F2.1 - Khởi tạo yêu cầu chuyến xe (Trip Creation & Transactional Outbox)
*Căn cứ tài liệu kiến trúc:* Mục 2.2 (Bước 1 & 2), Mục 3.3 (SQL Server đảm bảo ACID) & Mục 9.2 (Mẫu Transactional Outbox ghi dữ liệu và event tin cậy trong cùng transaction).

```mermaid
sequenceDiagram
    autonumber
    actor Rider as Rider App
    participant GW as API Gateway (:5000)
    participant Trip as Trip Service (:5105)
    participant SQL as SQL Server (TripDB)
    participant Worker as Outbox Publisher Worker
    participant Bus as RabbitMQ Event Bus

    Rider->>GW: POST /api/trips<br/>{ pickupLatitude, pickupLongitude, dropoffLatitude, dropoffLongitude, estimatedFare }
    GW->>Trip: Forward Request (kèm X-User-Id = RiderId)
    
    Trip->>SQL: BEGIN TRANSACTION
    Trip->>SQL: INSERT INTO Trips (Id, RiderId, Status = 'Requested', Pickup, Dropoff, Fare, CreatedAt)
    Trip->>SQL: INSERT INTO TripStatusHistories (TripId, ToStatus = 'Requested', ChangedAt)
    Trip->>SQL: INSERT INTO OutboxMessages (EventType = 'TripRequestedEvent', Payload = JSON, ProcessedAt = NULL)
    Trip->>SQL: COMMIT TRANSACTION

    SQL-->>Trip: Lưu thành công
    Trip-->>GW: 201 Created: ApiResponse (TripDto { status: 'Requested' })
    GW-->>Rider: 201 Created

    Note over Worker,Bus: Tiến trình chạy ngầm quét bảng Outbox mỗi 2 giây
    loop Quét định kỳ
        Worker->>SQL: SELECT TOP 20 FROM OutboxMessages WHERE ProcessedAt IS NULL ORDER BY CreatedAt
        SQL-->>Worker: Danh sách messages
        Worker->>Bus: Publish TripRequestedEvent (tripId, riderId, pickup, dropoff)
        Worker->>SQL: UPDATE OutboxMessages SET ProcessedAt = UtcNow WHERE Id = @id
    end
```

---

#### SD 04: F2.2 - Khách hàng xác nhận hoặc từ chối báo giá (Fare Quote Confirmation / Rejection)
*Căn cứ tài liệu kiến trúc:* Mục 2.2 (Bước 4) & Mục 4 (Flowchart vòng đời chuyến đi: Rider đồng ý -> chuyển bước kế; Rider từ chối -> Hủy chuyến).

```mermaid
sequenceDiagram
    autonumber
    actor Rider as Rider App
    participant GW as API Gateway (:5000)
    participant Trip as Trip Service (:5105)
    participant SQL as SQL Server (TripDB)
    participant Bus as RabbitMQ Event Bus

    Note over Rider: Sau khi nhận được báo giá hiển thị trên màn hình
    alt Trường hợp 1: Rider ĐỒNG Ý giá cước
        Rider->>GW: POST /api/trips/{id}/confirm-quote { quoteId }
        GW->>Trip: Forward
        Trip->>SQL: UPDATE Trips SET FareConfirmed = true WHERE Id = @id AND Status = 'Requested'
        Trip-->>GW: 200 OK: ApiResponse { status: "Requested", message: "Bắt đầu tìm tài xế" }
        GW-->>Rider: 200 OK (Chuyển sang màn hình radar chờ tài xế)
    else Trường hợp 2: Rider TỪ CHỐI giá cước
        Rider->>GW: PUT /api/trips/{id}/cancel { reason: "Giá cước quá cao" }
        GW->>Trip: Forward
        Trip->>SQL: UPDATE Trips SET Status = 'Cancelled', CancellationReason = @reason WHERE Id = @id
        Trip->>SQL: INSERT INTO TripStatusHistories (TripId, ToStatus = 'Cancelled')
        Trip->>Bus: Publish TripCancelledEvent (tripId, riderId)
        Trip-->>GW: 200 OK: ApiResponse { status: "Cancelled", message: "Đã hủy cuốc xe" }
        GW-->>Rider: 200 OK (Đóng màn hình đặt xe)
    end
```

---

#### SD 05: F2.3 - Tài xế chấp nhận cuốc xe (Driver Acceptance & Concurrency Control)
*Căn cứ tài liệu kiến trúc:* Mục 2.2 (Bước 5), Mục 6.5 (DIP), Mục 7 (State Pattern) & Xử lý Optimistic Locking bằng `RowVersion` chống tranh chấp 1 cuốc xe giữa nhiều tài xế.

```mermaid
sequenceDiagram
    autonumber
    actor Driver as Driver App
    participant GW as API Gateway (:5000)
    participant Hub as SignalR Hub (/hubs/trips)
    participant Trip as Trip Service (:5105)
    participant SQL as SQL Server (TripDB)
    participant Bus as RabbitMQ Event Bus
    actor Rider as Rider App

    Driver->>GW: PUT /api/trips/{id}/accept (DriverId trong Header JWT)
    GW->>Trip: Forward

    Trip->>SQL: SELECT Status, DriverId, RowVersion FROM Trips WHERE Id = @id
    SQL-->>Trip: Record (Status = 'Requested', DriverId = NULL, RowVersion = @currentVer)

    alt Chuyến đi còn rảnh và RowVersion khớp
        Trip->>Trip: State Pattern: RequestedState.Accept() → DriverAcceptedState
        Trip->>SQL: UPDATE Trips SET Status = 'DriverAccepted', DriverId = @driverId, AcceptedAt = UtcNow<br/>WHERE Id = @id AND RowVersion = @currentVer
        SQL-->>Trip: 1 row affected (Thành công)
        Trip->>SQL: INSERT INTO TripStatusHistories (TripId, ToStatus = 'DriverAccepted', ChangedAt)
        Trip->>Bus: Publish TripAcceptedEvent (tripId, driverId, riderId)
        Bus->>Hub: Consume TripAcceptedEvent
        Hub-->>Rider: SignalR Push to Group trip_{id}: TripStatusChanged(DriverAccepted, driverInfo)
        Trip-->>GW: 200 OK: ApiResponse (TripDto { status: 'DriverAccepted' })
        GW-->>Driver: 200 OK (Chuyển sang màn hình đi đón khách)
    else Chuyến đi đã bị tài xế khác nhận trước (RowVersion thay đổi)
        SQL-->>Trip: 0 rows affected (Concurrency conflict)
        Trip-->>GW: 409 Conflict: { code: "TRIP_ALREADY_ACCEPTED", message: "Cuốc xe đã được nhận bởi tài xế khác" }
        GW-->>Driver: 409 Conflict (Tự động ẩn popup mời cuốc)
    end
```

---

#### SD 06: F2.4 - Tiến trình đón & trả khách (Journey Progression: EnRoute -> PickedUp -> DroppedOff)
*Căn cứ tài liệu kiến trúc:* Mục 2.2 (Bước 6) & Mục 4 (Flowchart: Tài xế đang đến -> Đã đón khách -> Đã trả khách; State Pattern quản lý chuyển đổi trạng thái hợp lệ).

```mermaid
sequenceDiagram
    autonumber
    actor Driver as Driver App
    participant GW as API Gateway (:5000)
    participant Hub as SignalR Hub (/hubs/trips)
    participant Trip as Trip Service (:5105)
    participant SQL as SQL Server (TripDB)
    participant Bus as RabbitMQ Event Bus
    actor Rider as Rider App

    %% Mốc 1: Đang đến điểm đón
    Note over Driver,Rider: MỐC 1: Bắt đầu di chuyển tới điểm đón
    Driver->>GW: PUT /api/trips/{id}/start-route
    GW->>Trip: Forward
    Trip->>Trip: State Machine: DriverAccepted → EnRoute
    Trip->>SQL: UPDATE Trips SET Status = 'EnRoute' WHERE Id = @id
    Trip->>Bus: Publish TripStatusChangedEvent (tripId, status = 'EnRoute')
    Bus->>Hub: Consume TripStatusChangedEvent
    Hub-->>Rider: SignalR Push: TripStatusChanged(EnRoute)
    Trip-->>GW: 200 OK: TripDto { status: 'EnRoute' }
    GW-->>Driver: 200 OK

    %% Mốc 2: Đã đón khách lên xe
    Note over Driver,Rider: MỐC 2: Tài xế gặp khách và đón lên xe
    Driver->>GW: PUT /api/trips/{id}/pick-up
    GW->>Trip: Forward
    Trip->>Trip: State Machine: EnRoute → PickedUp
    Trip->>SQL: UPDATE Trips SET Status = 'PickedUp', PickedUpAt = UtcNow WHERE Id = @id
    Trip->>Bus: Publish TripPickedUpEvent (tripId)
    Bus->>Hub: Consume TripPickedUpEvent
    Hub-->>Rider: SignalR Push: TripStatusChanged(PickedUp)
    Trip-->>GW: 200 OK: TripDto { status: 'PickedUp' }
    GW-->>Driver: 200 OK

    %% Mốc 3: Đã trả khách tại đích
    Note over Driver,Rider: MỐC 3: Đã đến điểm đích và trả khách
    Driver->>GW: PUT /api/trips/{id}/drop-off
    GW->>Trip: Forward
    Trip->>Trip: State Machine: PickedUp → DroppedOff
    Trip->>SQL: BEGIN TRANSACTION
    Trip->>SQL: UPDATE Trips SET Status = 'DroppedOff', DroppedOffAt = UtcNow WHERE Id = @id
    Trip->>SQL: INSERT INTO OutboxMessages (EventType = 'TripDroppedOffEvent', Payload = JSON)
    Trip->>SQL: COMMIT TRANSACTION
    Trip->>Bus: Outbox Worker Publish TripDroppedOffEvent (tripId, riderId, driverId, fare)
    Bus->>Hub: Consume TripDroppedOffEvent
    Hub-->>Rider: SignalR Push: TripStatusChanged(DroppedOff)
    Trip-->>GW: 200 OK: TripDto { status: 'DroppedOff' }
    GW-->>Driver: 200 OK (Chuyển sang màn hình chờ thanh toán)
```

---

#### SD 07: F2.5 - Hủy chuyến đi trước khi đón (Trip Cancellation)
*Căn cứ tài liệu kiến trúc:* Mục 4 (Flowchart: Nhánh kết thúc - Hủy chuyến) & Mục 7 (Listing 6: Vòng đời chuyến đi).

```mermaid
sequenceDiagram
    autonumber
    actor User as Rider App / Driver App
    participant GW as API Gateway (:5000)
    participant Hub as SignalR Hub (/hubs/trips)
    participant Trip as Trip Service (:5105)
    participant SQL as SQL Server (TripDB)
    participant Bus as RabbitMQ Event Bus
    participant Match as Matching Service (:5248)
    actor Peer as Bên còn lại (Driver / Rider)

    User->>GW: PUT /api/trips/{id}/cancel { reason: "Có việc bận đột xuất" }
    GW->>Trip: Forward (kèm X-User-Id, X-User-Role)
    
    Trip->>SQL: SELECT Status, DriverId, RiderId FROM Trips WHERE Id = @id
    SQL-->>Trip: Record
    alt Trạng thái hợp lệ để hủy (Status IN 'Requested', 'DriverAccepted', 'EnRoute')
        Trip->>Trip: State Machine: Chuyển sang CancelledState
        Trip->>SQL: UPDATE Trips SET Status = 'Cancelled', CancellationReason = @reason WHERE Id = @id
        Trip->>SQL: INSERT INTO TripStatusHistories (TripId, ToStatus = 'Cancelled')
        Trip->>Bus: Publish TripCancelledEvent (tripId, driverId, riderId, reason)
        Bus->>Hub: Consume TripCancelledEvent
        Hub-->>Peer: SignalR Push to Group trip_{id}: TripStatusChanged(Cancelled, reason)
        Bus->>Match: Consume TripCancelledEvent
        opt Nếu chuyến đi đã có tài xế nhận trước đó
            Match->>Match: Cập nhật tài xế: is_available = true (Sẵn sàng nhận cuốc mới)
        end
        Trip-->>GW: 200 OK: ApiResponse { status: 'Cancelled' }
        GW-->>User: 200 OK (Đã hủy chuyến)
    else Không thể hủy (Status IN 'PickedUp', 'DroppedOff', 'Completed')
        Trip-->>GW: 409 Conflict: { code: "TRIP_INVALID_TRANSITION", message: "Không thể hủy chuyến sau khi đã đón khách" }
        GW-->>User: 409 Conflict
    end
```

---

#### SD 08: F2.6 - Đóng chuyến đi hoàn tất sau thanh toán (Trip Completion)
*Căn cứ tài liệu kiến trúc:* Mục 2.2 (Bước 8: Khi thanh toán hoàn tất, chuyến đi đóng lại ở trạng thái cuối) & Mục 7 (Listing 6: `CompletedState`).

```mermaid
sequenceDiagram
    autonumber
    participant Bus as RabbitMQ Event Bus
    participant Hub as SignalR Hub (/hubs/trips)
    participant Trip as Trip Service (:5105)
    participant SQL as SQL Server (TripDB)
    actor Rider as Rider App
    actor Driver as Driver App

    Note over Bus,Trip: Payment Service xử lý thanh toán xong và bắn Event
    Bus->>Trip: Consume PaymentSettled / PaymentCompletedEvent (tripId, transactionId, amount)
    
    Trip->>SQL: SELECT Status FROM Trips WHERE Id = @tripId
    SQL-->>Trip: Status = 'DroppedOff' (hoặc 'PaymentPending')

    Trip->>Trip: State Pattern: DroppedOffState.Complete() → CompletedState
    Trip->>SQL: UPDATE Trips SET Status = 'Completed', CompletedAt = UtcNow WHERE Id = @tripId
    Trip->>SQL: INSERT INTO TripStatusHistories (TripId, ToStatus = 'Completed', ChangedAt = UtcNow)
    Trip->>Bus: Publish TripCompletedEvent (tripId, riderId, driverId, amount, transactionId)
    SQL-->>Trip: Cập nhật thành công

    Bus->>Hub: Consume TripCompletedEvent
    Hub-->>Rider: SignalR Push to Group trip_{tripId}: PaymentCompleted & TripStatusChanged(Completed)
    Hub-->>Driver: SignalR Push to Group trip_{tripId}: PaymentCompleted & TripStatusChanged(Completed)
    Note over Rider: Hiển thị Màn hình Hóa đơn chi tiết & Đóng chuyến
    Note over Driver: Hiển thị Màn hình Chúc mừng & Doanh thu cuốc (+80%)
```

---

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

    Rider->>GW: POST /api/matching/quote<br/>{ pickupLatitude, pickupLongitude, dropoffLatitude, dropoffLongitude, vehicleType }
    GW->>Match: Forward request
    
    Match->>Match: Tính toán cự ly d (km) và thời gian dự kiến t (phút)
    Match->>Mongo: db.SurgeZones.findOne({ coordinates: { $geoIntersects: { $geometry: pickupPoint } } })
    Mongo-->>Match: SurgeZone config (hệ số surge s, phí toll cm)
    
    Match->>Mongo: db.PricingConfigs.findOne({ vehicleType: @type, isActive: true })
    Mongo-->>Match: PricingConfig (giá km cd, giá phút ct, thuế T, chiết khấu η)

    Match->>Match: Áp dụng Strategy Pattern: IPricingStrategy (StandardPricing → SurgePricing → PromoPricing)
    Note over Match: p = ((cd*d + ct*t)*s + cm) * η * (1 + T)
    Match->>Match: LSP Check: Đảm bảo p >= 0

    Match->>Mongo: db.TripQuotes.insertOne({ quoteId, fare: p, expiresAt: UtcNow + 10m })
    Mongo-->>Match: Lưu thành công (TTL Index tự động xóa sau 10 phút)

    Match-->>GW: 200 OK: ApiResponse (TripQuoteResponse { quoteId, distanceKm, totalFare, expiresAt })
    GW-->>Rider: 200 OK (Hiển thị giá cước dự kiến cho khách)
```

---

#### SD 10: F3.2 - Cập nhật vị trí GPS & Trạng thái tài xế (Driver GPS & Availability Tracking)
*Căn cứ tài liệu kiến trúc:* Mục 3.3 (MongoDB tối ưu cho dữ liệu vị trí động với geo-spatial index `2dsphere`) & Mục 5.2.

```mermaid
sequenceDiagram
    autonumber
    actor Driver as Driver App (Background Service)
    participant GW as API Gateway (:5000)
    participant Hub as SignalR Hub (/hubs/trips)
    participant Match as Matching Service (:5248)
    participant Mongo as MongoDB (MatchDB)
    participant Bus as RabbitMQ Event Bus
    actor Rider as Rider App

    loop Chu kỳ mỗi 10 giây (khi tài xế Online / Đang chở khách)
        Driver->>GW: PUT /api/drivers/{id}/location<br/>{ "latitude": 10.762887, "longitude": 106.682145, "bearing": 180, "speed": 35 }
        GW->>Match: Forward (kèm DriverId)
        Match->>Mongo: db.Drivers.updateOne(<br/>  { _id: @driverId },<br/>  { $set: { location: { type: "Point", coordinates: [lng, lat] }, lastLocationUpdatedAt: UtcNow } }<br/>)
        Mongo-->>Match: Đã cập nhật vị trí
        Match-->>GW: 200 OK: ApiResponse { status: "Success" }
        GW-->>Driver: 200 OK

        opt Nếu tài xế đang trong chuyến đi hoạt động (EnRoute / PickedUp)
            Match->>Bus: Publish DriverLocationUpdatedEvent (tripId, driverId, lat, lng, bearing, speed)
            Bus->>Hub: Consume DriverLocationUpdatedEvent
            Hub-->>Rider: SignalR Push to Group trip_{tripId}: DriverLocationUpdated(lat, lng, bearing, speed)
            Note over Rider: Frontend cập nhật Marker xe di chuyển mượt mà trên bản đồ
        end
    end

    opt Khi tài xế Bật / Tắt nhận cuốc
        Driver->>GW: PUT /api/drivers/{id}/availability { isAvailable: true / false }
        GW->>Match: Forward
        Match->>Mongo: db.Drivers.updateOne({ _id: @driverId }, { $set: { isAvailable: @val } })
        Mongo-->>Match: Đã cập nhật trạng thái
        Match-->>GW: 200 OK
        GW-->>Driver: 200 OK
    end
```

---

#### SD 11: F3.3 - Tìm kiếm & Điều phối tài xế với vòng lặp thử giới hạn <= k lần (Driver Matching Engine)
*Căn cứ tài liệu kiến trúc:* Mục 2.2 (Bước 5), Mục 4 (Flowchart: Đề xuất cho tài xế -> từ chối, $\le k$ lần -> thử lại; $> k$ lần -> Không thành công) & Mục 5.2 (Truy vấn vị trí trên MongoDB).

```mermaid
sequenceDiagram
    autonumber
    participant Bus as RabbitMQ Event Bus
    participant Hub as SignalR Hub (/hubs/trips)
    participant Match as Matching Service (:5248)
    participant Mongo as MongoDB (MatchDB)
    actor Driver as Driver App
    participant GW as API Gateway (:5000)
    participant Trip as Trip Service (:5105)
    actor Rider as Rider App

    Bus->>Match: Consume TripRequestedEvent (tripId, pickupCoordinates)
    Match->>Match: Khởi tạo biến đếm retry: attempt = 1 (Tối đa k lần thử)

    loop Vòng lặp điều phối (attempt từ 1 đến k)
        Match->>Mongo: db.Drivers.find({ isAvailable: true, location: nearSphere(pickupPoint, R) })
        Mongo-->>Match: Tìm thấy Driver gần nhất
        
        alt Tìm thấy tài xế khả dụng
            Match->>Bus: Publish TripOfferDispatchedEvent (tripId, driverId, pickup, dropoff, fare, countdown = 15s)
            Bus->>Hub: Consume TripOfferDispatchedEvent
            Hub-->>Driver: SignalR Push to Group driver_{driverId}: ReceiveTripOffer (15s countdown)
            Note over Driver: Bật Popup mời cuốc: Đếm ngược 15s, Hiển thị bản đồ, điểm đón và cước phí

            alt Trường hợp 1: Tài xế BẤM CHẤP NHẬN trong 15s
                Driver->>GW: PUT /api/trips/{id}/accept (Xem chi tiết SD 05)
                GW->>Trip: Forward
                Trip->>Bus: Publish TripAcceptedEvent (tripId, driverId, riderId)
                Bus->>Match: Consume TripAcceptedEvent
                Note over Match: Thoát vòng lặp điều phối - Ghép cuốc thành công
            else Trường hợp 2: Tài xế BẤM TỪ CHỐI
                Driver->>GW: PUT /api/trips/{id}/reject { reason: "Xe hỏng / Không tiện đường" }
                GW->>Trip: Forward
                Trip->>Bus: Publish TripRejectedByDriverEvent (tripId, driverId)
                Bus->>Match: Consume TripRejectedByDriverEvent
                Match->>Match: Tăng attempt = attempt + 1 và Mở rộng bán kính R
            else Trường hợp 3: HẾT HẠN 15s không phản hồi
                Match->>Match: Timer 15s hết hạn
                Match->>Bus: Publish TripOfferExpiredEvent (tripId, driverId)
                Bus->>Hub: Consume TripOfferExpiredEvent
                Hub-->>Driver: SignalR Push to Group driver_{driverId}: TripOfferExpired (Đóng Popup)
                Match->>Match: Tăng attempt = attempt + 1 và Mở rộng bán kính R
            end

            alt Đã vượt quá k lần thử (attempt > k)
                Note over Match: Vượt quá k lần thử theo quy định PDF
                Match->>Bus: Publish MatchingFailedEvent (tripId, reason: "Đã thử k tài xế nhưng đều bị từ chối")
                Bus->>Trip: Consume MatchingFailedEvent
                Trip->>Trip: Cập nhật trạng thái chuyến đi: Status = 'Failed'
                Trip->>Bus: Publish TripStatusChangedEvent (tripId, status = 'Failed')
                Bus->>Hub: Consume TripStatusChangedEvent
                Hub-->>Rider: SignalR Push to Group trip_{tripId}: TripStatusChanged(Failed, "Không tìm thấy tài xế quanh khu vực")
                Note over Trip,Rider: Chuyến đi kết thúc: Không thành công
            end
        else Không tìm thấy bất kỳ tài xế nào trong bán kính R
            Match->>Bus: Publish MatchingFailedEvent (tripId, reason: "Không có tài xế quanh khu vực")
            Bus->>Trip: Consume MatchingFailedEvent
            Trip->>Trip: Cập nhật trạng thái: Status = 'Failed'
            Trip->>Bus: Publish TripStatusChangedEvent (tripId, status = 'Failed')
            Bus->>Hub: Consume TripStatusChangedEvent
            Hub-->>Rider: SignalR Push to Group trip_{tripId}: TripStatusChanged(Failed, "Không có tài xế trực tuyến")
        end
    end
```

---

### PHÂN HỆ 4: THANH TOÁN, SỔ CÁI KÉP & BỒI THƯỜNG (PAYMENT SERVICE - POSTGRESQL `PayDB`)

#### SD 12: F4.1 - Tự động trừ tiền & Ghi sổ cái kép (Double-entry Ledger Settlement)
*Căn cứ tài liệu kiến trúc:* Mục 2.2 (Bước 7), Mục 3.3 (PostgreSQL đảm bảo sổ cái giao dịch kiểu double-entry, kiểm toán và nhất quán tiền bạc) & Mục 5.3 (Payment Service bắt buộc).

```mermaid
sequenceDiagram
    autonumber
    participant Bus as RabbitMQ Event Bus
    participant Pay as Payment Service (:5114)
    participant Postgres as PostgreSQL (PayDB)
    participant Gateway as Cổng Thanh Toán (Momo / Thẻ)

    Bus->>Pay: Consume TripDroppedOffEvent (tripId, riderId, driverId, fareAmount, idempotencyKey)

    Pay->>Postgres: SELECT * FROM idempotency_records WHERE idempotency_key = @key
    alt Khóa Idempotency đã tồn tại (Yêu cầu trùng lặp)
        Postgres-->>Pay: Đã xử lý trước đó
        Note over Pay: Bỏ qua xử lý, trả lại kết quả giao dịch cũ
    else Khóa mới hoàn toàn (Chưa từng xử lý)
        Pay->>Postgres: INSERT INTO idempotency_records (key, status = 'Processing')
        Pay->>Gateway: Gửi lệnh trừ tiền qua Token thanh toán của Rider
        Gateway-->>Pay: Thanh toán cổng thành công

        Pay->>Postgres: BEGIN TRANSACTION
        Pay->>Postgres: INSERT INTO transactions (id, trip_id, total_amount, status = 'Success')
        Note over Pay,Postgres: Ghi 3 bút toán Sổ Cái Kép (Định luật: Tổng Nợ = Tổng Có)
        Pay->>Postgres: INSERT INTO ledger_entries (account_id = RiderWallet, entry_type = 'Debit', amount = fare)
        Pay->>Postgres: INSERT INTO ledger_entries (account_id = DriverWallet, entry_type = 'Credit', amount = fare * 0.8)
        Pay->>Postgres: INSERT INTO ledger_entries (account_id = PlatformFee, entry_type = 'Credit', amount = fare * 0.2)
        Pay->>Postgres: UPDATE accounts SET balance = balance - fare WHERE id = RiderWallet
        Pay->>Postgres: UPDATE accounts SET balance = balance + (fare * 0.8) WHERE id = DriverWallet
        Pay->>Postgres: UPDATE accounts SET balance = balance + (fare * 0.2) WHERE id = PlatformFee
        Pay->>Postgres: UPDATE idempotency_records SET status = 'Success' WHERE key = @key
        Pay->>Postgres: COMMIT TRANSACTION

        Pay->>Bus: Publish PaymentSettled / PaymentCompletedEvent (tripId, amount)
    end
```

---

#### SD 13: F4.2 - Xử lý lỗi thanh toán tạm thời & Retry có giới hạn với Idempotency Key
*Căn cứ tài liệu kiến trúc:* Mục 4 (Flowchart: Thanh toán lỗi / chờ xử lý $\to$ retry có giới hạn với cùng một idempotency key; nếu vẫn lỗi thì giữ trạng thái chờ xử lý thay vì trừ lặp hoặc báo thành công sai) & Mục 9.2 (Consumer idempotent).

```mermaid
sequenceDiagram
    autonumber
    participant Bus as RabbitMQ Event Bus
    participant Pay as Payment Service (:5114)
    participant Gateway as Cổng Thanh Toán / Ví
    participant Postgres as PostgreSQL (PayDB)
    participant Trip as Trip Service (:5105)

    Bus->>Pay: Consume TripDroppedOffEvent (tripId, fare, idempotencyKey)
    Pay->>Gateway: Gửi yêu cầu trừ tiền (Header: X-Idempotency-Key)
    Gateway-->>Pay: Lỗi mạng tạm thời (504 Gateway Timeout / Network Socket Error)

    Note over Pay,Gateway: Bắt đầu Retry có giới hạn với CÙNG một Idempotency Key (Exponential Backoff)
    loop Retry giới hạn (Lần 1, Lần 2, Lần 3)
        Pay->>Gateway: Thử lại trừ tiền (Cùng X-Idempotency-Key)
        Gateway-->>Pay: Vẫn gặp lỗi kết nối
    end

    Note over Pay,Postgres: Vượt quá số lần thử lại tối đa mà vẫn lỗi
    Pay->>Postgres: BEGIN TRANSACTION
    Pay->>Postgres: INSERT INTO transactions (trip_id, status = 'PaymentPending', error_detail = 'Gateway Timeout')
    Pay->>Postgres: UPDATE idempotency_records SET status = 'Failed' WHERE key = @key
    Pay->>Postgres: COMMIT TRANSACTION

    Pay->>Bus: Publish PaymentFailedEvent (tripId, status = 'PaymentPending')
    Bus->>Trip: Consume PaymentFailedEvent
    Trip->>Trip: Chuyển trạng thái chuyến đi sang: Status = 'PaymentPending'
    Note over Trip: Giữ trạng thái Chờ xử lý thủ công.<br/>Tuyệt đối không trừ tiền lặp và không báo thành công sai!
```

---

#### SD 14: F4.3 - Hoàn tiền & Giao dịch bồi thường Saga (Refund & Compensating Transaction)
*Căn cứ tài liệu kiến trúc:* Mục 5.3 (Payment Service xử lý hoàn tiền và bồi thường khi một Saga thất bại) & Mục 9.2 (Saga điều phối nhiều bước và định nghĩa hành động bồi thường).

```mermaid
sequenceDiagram
    autonumber
    actor Admin as Quản Trị Viên / Hệ Thống Saga Coordinator
    participant GW as API Gateway (:5000)
    participant Pay as Payment Service (:5114)
    participant Postgres as PostgreSQL (PayDB)
    participant Gateway as Cổng Thanh Toán Hoàn Trả (Momo/Stripe)
    participant Bus as RabbitMQ Event Bus

    Admin->>GW: POST /api/payments/{transactionId}/refund<br/>{ reason: "Saga bù trừ do chuyến đi bị sự cố bất khả kháng" }
    GW->>Pay: Forward Request
    
    Pay->>Postgres: SELECT * FROM transactions WHERE id = @id AND status = 'Success'
    Postgres-->>Pay: Thông tin giao dịch gốc (total_amount = fare)

    Pay->>Gateway: Yêu cầu hoàn tiền về nguồn thẻ/ví của khách
    Gateway-->>Pay: Hoàn tiền thành công (refund_ref_id)

    Pay->>Postgres: BEGIN TRANSACTION
    Pay->>Postgres: UPDATE transactions SET status = 'Refunded' WHERE id = @id
    Note over Pay,Postgres: Bút toán Đảo Sổ Cái (Compensating Ledger Entries)
    Pay->>Postgres: INSERT INTO ledger_entries (account_id = RiderWallet, entry_type = 'Credit', amount = fare)
    Pay->>Postgres: INSERT INTO ledger_entries (account_id = DriverWallet, entry_type = 'Debit', amount = fare * 0.8)
    Pay->>Postgres: INSERT INTO ledger_entries (account_id = PlatformFee, entry_type = 'Debit', amount = fare * 0.2)
    Pay->>Postgres: UPDATE accounts SET balance = balance + fare WHERE id = RiderWallet
    Pay->>Postgres: UPDATE accounts SET balance = balance - (fare * 0.8) WHERE id = DriverWallet
    Pay->>Postgres: UPDATE accounts SET balance = balance - (fare * 0.2) WHERE id = PlatformFee
    Pay->>Postgres: COMMIT TRANSACTION

    Pay->>Bus: Publish PaymentRefundedEvent (transactionId, tripId)
    Pay-->>GW: 200 OK: ApiResponse { status: 'Refunded', message: "Hoàn tiền bồi thường thành công" }
    GW-->>Admin: 200 OK
```

---

#### SD 15: F4.4 - Token hóa phương thức thanh toán (PCI-DSS Payment Tokenization)
*Căn cứ tài liệu kiến trúc:* Mục 5.3 (Payment Service chỉ lưu token do cổng thanh toán cấp, không lưu số thẻ/CVV để đạt chuẩn an toàn bảo mật PCI-DSS).

```mermaid
sequenceDiagram
    autonumber
    actor Rider as Rider App
    participant ThirdParty as Cổng Thanh Toán Đối Tác (Momo / VNPay / Stripe)
    participant GW as API Gateway (:5000)
    participant Pay as Payment Service (:5114)
    participant Postgres as PostgreSQL (PayDB)

    Rider->>ThirdParty: Nhập thông tin thẻ ngân hàng / Ủy quyền tài khoản ví Momo
    ThirdParty->>ThirdParty: Mã hóa và lưu trữ thẻ an toàn đạt chuẩn PCI-DSS
    ThirdParty-->>Rider: Trả về Token đại diện (Ví dụ: "tok_momo_88a91b2c3d")
    Note over Rider,ThirdParty: Thiết bị Client không bao giờ lưu số thẻ hay CVV!

    Rider->>GW: POST /api/payment-tokens<br/>{ provider: "Momo", token: "tok_momo_88a91b2c3d", isDefault: true }
    GW->>Pay: Forward (kèm X-User-Id = RiderId)

    Pay->>Pay: Tạo Masked Token hiển thị UI ("Momo ví liên kết: **** 5678")
    Pay->>Postgres: INSERT INTO payment_tokens (id, user_id, provider, token, is_default, created_at)<br/>VALUES (gen_random_uuid(), @userId, 'Momo', @token, true, now())
    Postgres-->>Pay: Đã lưu token thành công

    Pay-->>GW: 201 Created: ApiResponse (PaymentTokenDto { provider: "Momo", maskedToken: "**** 5678" })
    GW-->>Rider: 201 Created (Hiển thị phương thức thanh toán đã liên kết trên màn hình)
```
