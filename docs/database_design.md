# 🗄️ SwiftRide - Thiết Kế Cơ Sở Dữ Liệu Chi Tiết (Database Architecture & Design)

> **Căn cứ tài liệu kiến trúc gốc:** `RideLite-KienTrucPhanMem.pdf` (Mục 2.4: 3 Microservices, Mục 3.3: Ba cơ sở dữ liệu khác nhau - Polyglot Persistence, Mục 4: Flowchart vòng đời chuyến đi, Mục 5: Chi tiết 3 microservices, Mục 9.2: Nhất quán & Độ tin cậy).  
> **Bộ tài liệu thiết kế đồng bộ:**  
> - `FUNCTIONAL_DECOMPOSITION_AND_SEQUENCE_DIAGRAMS.md` (Sơ đồ phân rã chức năng & 15 Sơ đồ trình tự)  
> - `DATABASE_DESIGN.md` (Thiết kế chi tiết 3 CSDL: SQL Server `TripDB`, MongoDB `MatchDB`, PostgreSQL `PayDB`)  
> - `API_CONTRACT_AND_FE_INTEGRATION.md` (Hợp đồng giao tiếp REST API, YARP Route Alias & SignalR Realtime)

## 📑 Cấu Trúc Tài Liệu
1. **Kiến Trúc Tổng Thể CSDL (Polyglot Persistence & Database-per-Service)**
2. **Chi Tiết CSDL Trip Service (SQL Server - `TripDB`)**
   - Bảng `Trips`
   - Bảng ánh xạ 10 Trạng thái Chuyến đi (`TripStatus`)
   - Bảng `TripStatusHistories`
   - Bảng `OutboxMessages`
3. **Chi Tiết CSDL Matching & Pricing Service (MongoDB - `MatchDB`)**
   - Collection `Drivers`
   - Collection `PricingConfigs`
   - Collection `SurgeZones`
   - Collection `TripQuotes`
4. **Chi Tiết CSDL Payment Service (PostgreSQL - `PayDB`)**
   - Bảng `accounts`
   - Bảng `transactions`
   - Bảng `ledger_entries` (Sổ cái kép bất biến)
   - Bảng `payment_tokens`
   - Bảng `idempotency_records`
   - Bảng `outbox_messages`
5. **Ma Trận Ánh Xạ CSDL & 15 Sơ Đồ Trình Tự (Traceability Matrix)**
6. **Tổng Hợp Cấu Hình Docker & Connection String**

## 1. Kiến Trúc Tổng Thể CSDL (Polyglot Persistence & Database-per-Service)

Hệ thống SwiftRide áp dụng mô hình **Database-per-Service** và nguyên lý **Polyglot Persistence** (Mục 3.3 PDF): mỗi microservice độc quyền sở hữu một CSDL chuyên biệt phù hợp nhất với bản chất nghiệp vụ của nó, tuyệt đối không truy cập chéo database của nhau.

> **Lưu ý kiến trúc về Xác thực (Auth Store tại API Gateway):**  
> Do đề bài giới hạn tối đa **3 microservices và 3 CSDL nghiệp vụ** (`TripDB`, `MatchDB`, `PayDB`), tầng `API Gateway (YARP)` sử dụng **In-Memory Identity & RefreshToken Store** (khởi tạo sẵn các tài khoản mẫu `Rider`, `Driver`, `Admin`) để cấp phát và kiểm tra JWT Bearer Token (SD 01, SD 02) mà không cần tạo CSDL thứ 4.

```mermaid
flowchart TD
    subgraph TripService_Scope["1. Trip Service (:5105 - SQL Server TripDB)"]
        direction TB
        Trips["Trips (Bảng chính - State Machine 10 trạng thái)"]
        TripStatusHistories["TripStatusHistories (Audit nhật ký trạng thái)"]
        TripOutbox["OutboxMessages (Transactional Outbox)"]
        Trips --> TripStatusHistories
        Trips -.-> TripOutbox
    end

    subgraph MatchingService_Scope["2. Matching & Pricing Service (:5248 - MongoDB MatchDB)"]
        direction TB
        Drivers[("Drivers (GeoJSON Point + 2dsphere index)")]
        PricingConfigs[("PricingConfigs (Biểu phí gốc: cd, ct, cm, T, eta)")]
        SurgeZones[("SurgeZones (Vùng giá động: GeoJSON Polygon)")]
        TripQuotes[("TripQuotes (Báo giá tạm thời + TTL index 10m)")]
    end

    subgraph PaymentService_Scope["3. Payment Service (:5114 - PostgreSQL PayDB)"]
        direction TB
        Accounts["accounts (Tài khoản: RiderWallet, DriverWallet, PlatformRevenue, EscrowHolding)"]
        Transactions["transactions (Giao dịch tài chính + Unique Index chống thu trùng)"]
        LedgerEntries["ledger_entries (Sổ cái kép bất biến Debit/Credit)"]
        PaymentTokens["payment_tokens (Token bảo mật chuẩn PCI-DSS)"]
        IdempotencyRecords["idempotency_records (Khóa chống trừ tiền trùng)"]
        PayOutbox["outbox_messages (Transactional Outbox)"]
        
        Transactions --> LedgerEntries
        Accounts --> LedgerEntries
        Transactions -.-> PayOutbox
    end

    EventBus[("RabbitMQ MassTransit Event Bus")]
    TripOutbox -->|Publish TripRequested / TripDroppedOff / TripCancelled| EventBus
    EventBus -->|Consume| MatchingService_Scope
    EventBus -->|Consume| PaymentService_Scope
    PayOutbox -->|Publish PaymentSettled / PaymentFailed / PaymentRefunded| EventBus
    EventBus -->|Consume| TripService_Scope
```

## 2. Chi Tiết CSDL Trip Service (SQL Server - `TripDB`)

### 2.1. Mục đích & Lý do chọn
- **Tính toàn vẹn & ACID cao:** Vòng đời chuyến đi là một máy trạng thái (State Machine) cần bảo toàn tính nhất quán tuyệt đối, không được nhảy cóc trạng thái.
- **Khóa lạc quan (Optimistic Concurrency Control):** Sử dụng cột `RowVersion` để giải quyết tranh chấp cuốc xe khi nhiều tài xế cùng bấm nhận 1 chuyến tại cùng mili-giây (SD 05: F2.3).
- **Mẫu Transactional Outbox:** Bảng `OutboxMessages` được ghi trong cùng Local Transaction với bảng `Trips` và `TripStatusHistories` để đảm bảo dữ liệu và sự kiện domain không bao giờ bị lệch (SD 04, SD 05, SD 06, SD 07, SD 08).

### 2.2. Sơ đồ thực thể ERD (TripDB)

```mermaid
erDiagram
    Trips ||--o{ TripStatusHistories : "has audit history"
    Trips {
        uniqueidentifier Id PK
        uniqueidentifier RiderId "Indexed"
        uniqueidentifier DriverId "Indexed, Nullable"
        uniqueidentifier QuoteId "Nullable"
        nvarchar VehicleType "Bike, Car4, Car7"
        nvarchar Status "Indexed (10 states)"
        decimal PickupLatitude
        decimal PickupLongitude
        nvarchar PickupAddress
        decimal DropoffLatitude
        decimal DropoffLongitude
        nvarchar DropoffAddress
        decimal DistanceKm
        int EstimatedDurationMinutes
        decimal EstimatedFare
        decimal ActualFare "Nullable"
        bit FareConfirmed "Default 0"
        int DriverRejectionCount "Default 0"
        nvarchar ExcludedDriverIds "JSON array, Nullable"
        datetimeoffset CreatedAt
        datetimeoffset UpdatedAt "Nullable"
        datetimeoffset AcceptedAt "Nullable"
        datetimeoffset PickedUpAt "Nullable"
        datetimeoffset DroppedOffAt "Nullable"
        datetimeoffset CompletedAt "Nullable"
        nvarchar CancellationReason "Nullable"
        rowversion RowVersion "Optimistic Locking"
    }

    TripStatusHistories {
        bigint Id PK
        uniqueidentifier TripId FK
        nvarchar FromStatus "Nullable"
        nvarchar ToStatus
        nvarchar Reason "Nullable"
        datetimeoffset ChangedAt
    }

    OutboxMessages {
        uniqueidentifier Id PK
        nvarchar EventType
        nvarchar Payload "JSON"
        datetimeoffset CreatedAt
        datetimeoffset ProcessedAt "Nullable, Indexed"
        nvarchar Error "Nullable"
    }
```

### 2.3. Bảng dữ liệu chi tiết

#### Bảng `Trips`
| Tên cột | Kiểu dữ liệu | Ràng buộc | Mô tả & Ánh xạ Sơ đồ trình tự |
| :--- | :--- | :--- | :--- |
| `Id` | `UNIQUEIDENTIFIER` | PK | Mã định danh duy nhất của chuyến đi |
| `RiderId` | `UNIQUEIDENTIFIER` | NOT NULL, Index | Mã hành khách đặt cuốc xe (SD 03) |
| `DriverId` | `UNIQUEIDENTIFIER` | NULL, Index | Mã tài xế nhận cuốc (gán ở SD 05: F2.3) |
| `QuoteId` | `UNIQUEIDENTIFIER` | NULL | Mã báo giá gắn với chuyến đi (SD 03, SD 04) |
| `VehicleType` | `NVARCHAR(20)` | NOT NULL, DEFAULT 'Bike' | Loại phương tiện yêu cầu: `Bike`, `Car4`, `Car7` |
| `Status` | `NVARCHAR(50)` | NOT NULL, Index | Trạng thái vòng đời chuyến đi (10 trạng thái) |
| `PickupLatitude` | `DECIMAL(9, 6)` | NOT NULL | Vĩ độ điểm đón khách |
| `PickupLongitude` | `DECIMAL(9, 6)` | NOT NULL | Kinh độ điểm đón khách |
| `PickupAddress` | `NVARCHAR(255)` | NOT NULL | Địa chỉ văn bản điểm đón khách |
| `DropoffLatitude` | `DECIMAL(9, 6)` | NOT NULL | Vĩ độ điểm trả khách |
| `DropoffLongitude` | `DECIMAL(9, 6)` | NOT NULL | Kinh độ điểm trả khách |
| `DropoffAddress` | `NVARCHAR(255)` | NOT NULL | Địa chỉ văn bản điểm trả khách |
| `DistanceKm` | `DECIMAL(8, 2)` | NOT NULL | Cự ly di chuyển ước tính (km) |
| `EstimatedDurationMinutes` | `INT` | NOT NULL | Thời gian ước tính (phút) |
| `EstimatedFare` | `DECIMAL(18, 2)` | NOT NULL | Giá cước ước tính ban đầu |
| `ActualFare` | `DECIMAL(18, 2)` | NULL | Giá cước quyết toán thực tế khi trả khách (SD 06) |
| `FareConfirmed` | `BIT` | NOT NULL, DEFAULT 0 | Đánh dấu khách đã đồng ý giá cước để bắt đầu tìm xe (SD 04: F2.2) |
| `DriverRejectionCount` | `INT` | NOT NULL, DEFAULT 0 | Số lần tài xế từ chối hoặc hết hạn lời mời (tối đa $k$ lần, SD 11: F3.3) |
| `ExcludedDriverIds` | `NVARCHAR(MAX)` | NULL | Mảng JSON lưu các `DriverId` đã từ chối cuốc này để không mời lặp (SD 11) |
| `CreatedAt` | `DATETIMEOFFSET` | NOT NULL | Thời điểm khởi tạo cuốc xe (SD 03: F2.1) |
| `UpdatedAt` | `DATETIMEOFFSET` | NULL | Thời điểm cập nhật dữ liệu gần nhất |
| `AcceptedAt` | `DATETIMEOFFSET` | NULL | Thời điểm tài xế bấm nhận cuốc (SD 05: F2.3) |
| `PickedUpAt` | `DATETIMEOFFSET` | NULL | Thời điểm khách lên xe (SD 06: F2.4) |
| `DroppedOffAt` | `DATETIMEOFFSET` | NULL | Thời điểm khách xuống xe (SD 06: F2.4) |
| `CompletedAt` | `DATETIMEOFFSET` | NULL | Thời điểm hoàn tất thanh toán và đóng chuyến (SD 08: F2.6) |
| `CancellationReason` | `NVARCHAR(255)` | NULL | Lý do hủy chuyến hoặc lý do thất bại (SD 04, SD 07, SD 11) |
| `RowVersion` | `ROWVERSION` | NOT NULL | Token kiểm soát đồng thời chống tranh chấp cuốc (SD 05) |

#### Bảng ánh xạ 10 Trạng thái Chuyến đi (`TripStatus`)
Bám sát **Mục 4 (Flowchart vòng đời chuyến đi)** và **Mục 7 (State Pattern - Listing 6)** trong tài liệu kiến trúc PDF:

| Mã Trạng Thái | Tên Trạng Thái (`TripStatus`) | Hành Động Kích Hoạt | Sơ Đồ Trình Tự Tương Ứng |
|:---:|:---|:---|:---:|
| **0** | `Requested` | Khách tạo cuốc xe mới và xác nhận báo giá | **SD 03, SD 04** |
| **1** | `DriverAccepted` | Tài xế bấm chấp nhận cuốc xe | **SD 05 (F2.3)** |
| **2** | `EnRoute` | Tài xế bắt đầu di chuyển tới điểm đón | **SD 06 (F2.4)** |
| **3** | `PickedUp` | Tài xế xác nhận khách đã lên xe | **SD 06 (F2.4)** |
| **4** | `DroppedOff` | Tài xế trả khách tại điểm đích, phát sự kiện thanh toán | **SD 06 (F2.4)** |
| **5** | `PaymentPending` | Lỗi trừ tiền tạm thời sau khi hết retry, chờ xử lý lại | **SD 13 (F4.2)** |
| **6** | `Paid` | Payment Service trừ tiền thành công, ghi nhận thanh toán | **SD 08, SD 12** |
| **7** | `Completed` | Chuyến đi đóng lại hoàn tất sau khi đã `Paid` | **SD 08 (F2.6)** |
| **8** | `Cancelled` | Khách từ chối báo giá hoặc hủy cuốc trước khi đón | **SD 04, SD 07** |
| **9** | `Failed` | Tài xế từ chối vượt quá $k$ lần thử hoặc không có xe | **SD 11 (F3.3)** |

#### Bảng `TripStatusHistories`
Phục vụ Audit Trail lưu vết lịch sử mọi lần chuyển đổi trạng thái của cuốc xe (bao gồm cả bước chuyển `DroppedOff` -> `Paid` -> `Completed`):
| Tên cột | Kiểu dữ liệu | Ràng buộc | Mô tả |
| :--- | :--- | :--- | :--- |
| `Id` | `BIGINT` | PK, IDENTITY(1,1) | Khóa chính tăng tự động |
| `TripId` | `UNIQUEIDENTIFIER` | NOT NULL, FK (`Trips.Id`), Index | Khóa ngoại liên kết chuyến đi |
| `FromStatus` | `NVARCHAR(50)` | NULL | Trạng thái nguồn trước khi chuyển đổi |
| `ToStatus` | `NVARCHAR(50)` | NOT NULL | Trạng thái đích mới |
| `Reason` | `NVARCHAR(255)` | NULL | Lý do chuyển trạng thái hoặc mã giao dịch thanh toán |
| `ChangedAt` | `DATETIMEOFFSET` | NOT NULL | Thời điểm ghi nhận sự kiện chuyển đổi |

#### Bảng `OutboxMessages` (Transactional Outbox trên SQL Server)
| Tên cột | Kiểu dữ liệu | Ràng buộc | Mô tả & Mục đích |
| :--- | :--- | :--- | :--- |
| `Id` | `UNIQUEIDENTIFIER` | PK | Mã định danh thông điệp |
| `EventType` | `NVARCHAR(255)` | NOT NULL | Tên sự kiện: `TripRequestedEvent`, `TripAcceptedEvent`, `TripDroppedOffEvent`, `TripCancelledEvent`, `TripCompletedEvent` |
| `Payload` | `NVARCHAR(MAX)` | NOT NULL | Nội dung Event serialize JSON |
| `CreatedAt` | `DATETIMEOFFSET` | NOT NULL | Thời điểm phát sinh sự kiện trong transaction |
| `ProcessedAt` | `DATETIMEOFFSET` | NULL, Index | Thời điểm Outbox Worker đã publish lên RabbitMQ |
| `Error` | `NVARCHAR(MAX)` | NULL | Lỗi chi tiết nếu publish lên broker thất bại |

## 3. Chi Tiết CSDL Matching & Pricing Service (MongoDB - `MatchDB`)

### 3.1. Mục đích & Lý do chọn
- **Lưu trữ vị trí động tốc độ cao:** Tọa độ GPS kèm hướng di chuyển (`bearing`) và tốc độ (`speed`) của tài xế được gửi về mỗi 10 giây (SD 10: F3.2).
- **Chỉ mục không gian địa lý `2dsphere`:** Bắt buộc phải có để thực hiện toán tử `$nearSphere` tìm tài xế gần nhất kết hợp `$nin` loại trừ tài xế đã từ chối (SD 11: F3.3).
- **Tính toán biểu phí linh hoạt (Strategy Pattern):** Lưu trữ cấu hình tham số công thức giá $p = ((c_d \cdot d + c_t \cdot t) \cdot s + c_m) \cdot \eta \cdot (1 + T)$ (SD 09: F3.1).
- **Chuẩn hóa Naming:** Toàn bộ Document fields sử dụng định dạng `snake_case` khớp với các thuộc tính `[BsonElement]` trong mã nguồn C#.

### 3.2. Cấu trúc Collections chi tiết

#### Collection `Drivers`
Lưu trữ hồ sơ tài xế và vị trí không gian địa lý thời gian thực:
```json
{
  "_id": "ObjectId('6706850a1234567890abcdef')",
  "driver_id": "22222222-2222-2222-2222-222222222222",
  "name": "Nguyễn Văn Tài Xế",
  "phone": "0901234567",
  "vehicle": {
    "make": "Honda",
    "model": "Air Blade",
    "license_plate": "59-A1 123.45",
    "type": "Bike"
  },
  "is_active": true,
  "is_available": true,
  "rating": 4.95,
  "location": {
    "type": "Point",
    "coordinates": [106.682145, 10.762887]
  },
  "bearing": 180.0,
  "speed": 35.0,
  "last_location_updated_at": "2026-10-18T12:30:00Z"
}
```
**Chỉ mục (Indexes):**
1. `{ "driver_id": 1 }` (Unique Index - tra cứu nhanh theo mã tài xế).
2. `{ "location": "2dsphere" }` (**Chỉ mục không gian địa lý bắt buộc** cho truy vấn `$nearSphere` tại SD 11: F3.3).
3. `{ "is_active": 1, "is_available": 1, "vehicle.type": 1 }` (Compound Index lọc nhanh tài xế sẵn sàng nhận cuốc).

#### Collection `PricingConfigs` (Biểu phí cấu hình cơ sở)
Cung cấp các tham số đầu vào cho Strategy Pattern tại SD 09: F3.1:
```json
{
  "_id": "ObjectId('6706850a1234567890abcde1')",
  "vehicle_type": "Bike",
  "base_distance_rate": 10000.0,
  "base_time_rate": 1000.0,
  "base_toll_fee": 0.0,
  "tax_rate": 0.10,
  "promo_multiplier": 1.0,
  "is_active": true,
  "effective_from": "2026-01-01T00:00:00Z"
}
```
**Chỉ mục:** `{ "vehicle_type": 1, "is_active": 1 }` (Tra cứu nhanh biểu phí hiện hành).

#### Collection `SurgeZones` (Vùng phụ trội giá động)
Định nghĩa đa giác không gian địa lý để xác định hệ số nhân giá động $s$ theo khu vực cao điểm:
```json
{
  "_id": "ObjectId('6706850a1234567890abcde2')",
  "zone_name": "Khu vực Chợ Bến Thành - Quận 1",
  "surge_multiplier": 1.3,
  "boundary": {
    "type": "Polygon",
    "coordinates": [
      [
        [106.690, 10.770],
        [106.705, 10.770],
        [106.705, 10.785],
        [106.690, 10.785],
        [106.690, 10.770]
      ]
    ]
  },
  "is_active": true,
  "expires_at": "2026-10-18T18:00:00Z"
}
```
**Chỉ mục:** `{ "boundary": "2dsphere" }` (Hỗ trợ truy vấn `$geoIntersects` kiểm tra tọa độ đón có nằm trong vùng Surge hay không).

#### Collection `TripQuotes` (Lưu trữ báo giá tạm)
Lưu báo giá cước tính toán được tại SD 09: F3.1 để khách xác nhận trong 10 phút:
```json
{
  "_id": "6a9e1e3e-4fa0-459f-bb24-9b2f6efc8721",
  "rider_id": "11111111-1111-1111-1111-111111111111",
  "vehicle_type": "Bike",
  "distance_km": 4.8,
  "estimated_duration_minutes": 14,
  "pricing_breakdown": {
    "distance_cost": 48000.0,
    "time_cost": 14000.0,
    "surge_multiplier": 1.3,
    "toll_fee": 0.0,
    "discount_rate": 1.0,
    "tax_rate": 0.10
  },
  "total_fare": 65000.0,
  "created_at": "2026-10-18T12:30:00Z",
  "expires_at": "2026-10-18T12:40:00Z"
}
```
**Chỉ mục:** TTL Index trên trường `expires_at` (`{ "expires_at": 1 }, { expireAfterSeconds: 0 }`) giúp MongoDB tự động xóa bản ghi báo giá sau khi quá hạn 10 phút.

## 4. Chi Tiết CSDL Payment Service (PostgreSQL - `PayDB`)

### 4.1. Mục đích & Lý do chọn
- **Hệ thống Kế toán Sổ cái kép (Double-entry Ledger):** Quản lý tiền bạc bắt buộc tính toàn vẹn tuyệt đối. Mọi biến động số dư phải được ghi thành các cặp bút toán Nợ (`Debit`) và Có (`Credit`) bất biến, đảm bảo $\sum \text{Debit} = \sum \text{Credit}$ (SD 12: F4.1).
- **Kiểm soát trùng lặp 2 lớp (Idempotency + Unique Index):** Bảng `idempotency_records` kết hợp với Unique Filtered Index trên `transactions(trip_id) WHERE status = 'Success' AND transaction_type = 'RidePayment'` bảo đảm 100% không bao giờ xảy ra tình trạng trừ tiền 2 lần cho cùng một chuyến đi (SD 12, SD 13).
- **Chuẩn bảo mật PCI-DSS (Tokenization):** Bảng `payment_tokens` chỉ lưu Token an toàn do cổng thanh toán (`Momo`, `VNPay`, `Stripe`) cấp, không lưu số thẻ tín dụng hay mã CVV (SD 15: F4.4).
- **Giao dịch bồi thường Saga (Compensating Transaction):** Lưu vết chi tiết để thực hiện bút toán đảo sổ cái khi cần hoàn tiền (SD 14: F4.3).

### 4.2. Sơ đồ thực thể ERD (PostgreSQL)

```mermaid
erDiagram
    accounts ||--o{ ledger_entries : "participates in ledger"
    transactions ||--o{ ledger_entries : "contains entries"
    
    accounts {
        uuid id PK
        uuid owner_id "Indexed"
        varchar owner_type "Rider, Driver, Platform"
        varchar account_type "WalletLiability, PlatformRevenue, EscrowHolding"
        varchar currency "VND"
        numeric balance "Precision 18, Scale 2"
        timestamptz created_at
        timestamptz updated_at "Nullable"
    }

    transactions {
        uuid id PK
        uuid trip_id "Nullable, Unique Filtered Index on Success RidePayment"
        varchar transaction_type "RidePayment, PlatformFee, DriverPayout, Refund"
        varchar payment_method "Wallet, Momo, VNPay, Stripe"
        numeric total_amount "Check > 0"
        varchar status "Success, PaymentPending, Failed, Refunded"
        varchar description
        text error_detail "Nullable"
        timestamptz created_at
    }

    ledger_entries {
        bigserial id PK
        uuid transaction_id FK "Indexed"
        uuid account_id FK "Indexed"
        varchar entry_type "Debit, Credit"
        numeric amount "Check > 0"
        timestamptz created_at
    }

    payment_tokens {
        uuid id PK
        uuid user_id "Indexed"
        varchar provider "Momo, VNPay, Stripe"
        varchar token
        varchar masked_token
        boolean is_default
        timestamptz created_at
    }

    idempotency_records {
        varchar idempotency_key PK
        varchar request_hash
        varchar status "Processing, Success, Failed"
        text response_payload "Nullable"
        timestamptz created_at
        timestamptz expires_at
    }

    outbox_messages {
        uuid id PK
        varchar event_type
        text payload "JSON"
        timestamptz created_at
        timestamptz processed_at "Nullable, Indexed"
        text error "Nullable"
    }
```

### 4.3. Bảng dữ liệu chi tiết

#### Bảng `accounts` (Tài khoản ví / sàn / thu hộ)
| Tên cột | Kiểu dữ liệu | Ràng buộc | Mô tả |
| :--- | :--- | :--- | :--- |
| `id` | `UUID` | PK | Khóa chính tài khoản tài chính |
| `owner_id` | `UUID` | NOT NULL, Index | Mã chủ sở hữu (`RiderId`, `DriverId`, hoặc `PlatformId`) |
| `owner_type` | `VARCHAR(50)` | NOT NULL | Phân loại chủ sở hữu: `Rider`, `Driver`, `Platform` |
| `account_type` | `VARCHAR(50)` | NOT NULL | Loại tài khoản kế toán:<br/>• `WalletLiability`: Ví tiền nội bộ của Rider / Driver<br/>• `EscrowHolding`: Tài khoản trung gian ghi nhận tiền thu từ cổng ngoài (`Momo`/`VNPay`/`Stripe`)<br/>• `PlatformRevenue`: Tài khoản doanh thu phí sàn (20%) |
| `currency` | `VARCHAR(3)` | NOT NULL, DEFAULT 'VND' | Đơn vị tiền tệ chuẩn ISO |
| `balance` | `NUMERIC(18, 2)` | NOT NULL, DEFAULT 0.00 | Số dư hiện tại |
| `created_at` | `TIMESTAMPTZ` | NOT NULL | Thời điểm khởi tạo tài khoản |
| `updated_at` | `TIMESTAMPTZ` | NULL | Thời điểm cập nhật số dư cuối cùng |

#### Bảng `transactions` (Giao dịch tài chính)
| Tên cột | Kiểu dữ liệu | Ràng buộc | Mô tả & Ánh xạ Sơ đồ trình tự |
| :--- | :--- | :--- | :--- |
| `id` | `UUID` | PK | Khóa chính giao dịch |
| `trip_id` | `UUID` | NULL, Unique Filtered Index | Mã cuốc xe liên quan. Có Unique Index: `CREATE UNIQUE INDEX ux_transactions_trip_success ON transactions(trip_id) WHERE status = 'Success' AND transaction_type = 'RidePayment'` |
| `transaction_type` | `VARCHAR(50)` | NOT NULL | Loại giao dịch: `RidePayment`, `PlatformFee`, `DriverPayout`, `Refund` |
| `payment_method` | `VARCHAR(50)` | NOT NULL, DEFAULT 'Wallet' | Nguồn thanh toán: `Wallet`, `Momo`, `VNPay`, `Stripe` |
| `total_amount` | `NUMERIC(18, 2)` | NOT NULL, CHECK > 0 | Tổng số tiền phát sinh trong giao dịch |
| `status` | `VARCHAR(50)` | NOT NULL | Trạng thái giao dịch: `Success` (SD 12), `PaymentPending` (SD 13), `Failed`, `Refunded` (SD 14) |
| `description` | `VARCHAR(255)` | NOT NULL | Nội dung diễn giải giao dịch |
| `error_detail` | `TEXT` | NULL | Ghi nhận nguyên nhân lỗi nếu thanh toán bị gián đoạn (SD 13: F4.2) |
| `created_at` | `TIMESTAMPTZ` | NOT NULL | Thời điểm phát sinh giao dịch |

#### Bảng `ledger_entries` (Sổ cái ghi kép bất biến)
Bảng ghi nhận từng dòng Nợ/Có. Tuyệt đối **không được UPDATE hay DELETE** dòng đã ghi (Append-only Ledger):

*Quy tắc hạch toán thanh toán cuốc xe $65,000$ VNĐ tại SD 12 (F4.1):*
1. **Bút toán Nợ (`Debit` - $100\%$ cước = $65,000$ VNĐ):**
   - Nếu khách trả bằng **Ví nội bộ (`Wallet`)**: Ghi `Debit` vào `RiderWallet` (`WalletLiability` của khách).
   - Nếu khách trả qua **Cổng ngoài (`Momo`/`VNPay`/`Stripe`)**: Ghi `Debit` vào `EscrowHolding` (tiền cổng thanh toán thu hộ chuyển về sàn, không trừ vào số dư ví nội bộ của khách).
2. **Bút toán Có 1 (`Credit` - $80\%$ cước = $52,000$ VNĐ):** Ghi `Credit` vào `DriverWallet` (Tăng số dư ví tài xế).
3. **Bút toán Có 2 (`Credit` - $20\%$ cước = $13,000$ VNĐ):** Ghi `Credit` vào `PlatformRevenue` (Tăng doanh thu phí sàn).
$$\Rightarrow \sum \text{Debit } (65,000) = \sum \text{Credit } (52,000 + 13,000)$$

| Tên cột | Kiểu dữ liệu | Ràng buộc | Mô tả |
| :--- | :--- | :--- | :--- |
| `id` | `BIGSERIAL` | PK | Khóa chính tăng tự động |
| `transaction_id` | `UUID` | NOT NULL, FK (`transactions.id`), Index | Thuộc giao dịch tài chính nào |
| `account_id` | `UUID` | NOT NULL, FK (`accounts.id`), Index | Tài khoản ghi nhận biến động |
| `entry_type` | `VARCHAR(10)` | NOT NULL, CHECK in (`Debit`, `Credit`) | Bản chất bút toán: Nợ hoặc Có |
| `amount` | `NUMERIC(18, 2)` | NOT NULL, CHECK > 0 | Số tiền ghi nhận |
| `created_at` | `TIMESTAMPTZ` | NOT NULL | Thời điểm ghi sổ bất biến |

#### Bảng `payment_tokens` (Phương thức thanh toán đã liên kết - PCI-DSS)
| Tên cột | Kiểu dữ liệu | Ràng buộc | Mô tả & Ánh xạ Sơ đồ trình tự |
| :--- | :--- | :--- | :--- |
| `id` | `UUID` | PK | Khóa chính phương thức thanh toán |
| `user_id` | `UUID` | NOT NULL, Index | Mã hành khách sở hữu (SD 15: F4.4) |
| `provider` | `VARCHAR(50)` | NOT NULL, CHECK in (`Momo`, `VNPay`, `Stripe`) | Nhà cung cấp cổng thanh toán: `Momo`, `VNPay`, `Stripe` |
| `token` | `VARCHAR(255)` | NOT NULL | Mã Token đại diện do cổng thanh toán cấp (không lưu số thẻ/CVV) |
| `masked_token` | `VARCHAR(50)` | NOT NULL | Chuỗi che mờ hiển thị UI an toàn (vd: `**** 5678`) |
| `is_default` | `BOOLEAN` | NOT NULL, DEFAULT FALSE | Đánh dấu phương thức thanh toán mặc định |
| `created_at` | `TIMESTAMPTZ` | NOT NULL | Thời điểm liên kết |

#### Bảng `idempotency_records` (Kiểm soát chống trùng lặp)
| Tên cột | Kiểu dữ liệu | Ràng buộc | Mô tả & Ánh xạ Sơ đồ trình tự |
| :--- | :--- | :--- | :--- |
| `idempotency_key` | `VARCHAR(255)` | PK | Khóa định danh duy nhất (vd: `pay_trip_{tripId}` từ RabbitMQ hoặc Header `X-Idempotency-Key` ở SD 12, SD 13) |
| `request_hash` | `VARCHAR(255)` | NOT NULL | Hash nội dung request để phát hiện sai lệch payload |
| `status` | `VARCHAR(50)` | NOT NULL | Trạng thái xử lý: `Processing`, `Success`, `Failed` |
| `response_payload` | `TEXT` | NULL | Kết quả JSON lưu trữ để phản hồi tức thì khi có request retry gửi lại |
| `created_at` | `TIMESTAMPTZ` | NOT NULL | Thời điểm tiếp nhận khóa lần đầu |
| `expires_at` | `TIMESTAMPTZ` | NOT NULL, Index | Thời điểm hết hạn của khóa Idempotency |

#### Bảng `outbox_messages` (Transactional Outbox trên PostgreSQL)
Đảm bảo các sự kiện `PaymentSettledEvent`, `PaymentFailedEvent`, `PaymentRefundedEvent` (SD 12, SD 13, SD 14) được ghi trong cùng transaction với sổ cái và bắn sang RabbitMQ một cách đáng tin cậy:
| Tên cột | Kiểu dữ liệu | Ràng buộc | Mô tả |
| :--- | :--- | :--- | :--- |
| `id` | `UUID` | PK | Mã thông điệp |
| `event_type` | `VARCHAR(255)` | NOT NULL | Tên sự kiện: `PaymentSettledEvent`, `PaymentFailedEvent`, `PaymentRefundedEvent` |
| `payload` | `TEXT` | NOT NULL | Nội dung Event dạng JSON |
| `created_at` | `TIMESTAMPTZ` | NOT NULL | Thời điểm tạo trong transaction |
| `processed_at` | `TIMESTAMPTZ` | NULL, Index | Thời điểm Outbox Worker gửi thành công lên RabbitMQ |
| `error` | `TEXT` | NULL | Lỗi chi tiết nếu broker gặp sự cố |

## 5. Ma Trận Ánh Xạ CSDL & 15 Sơ Đồ Trình Tự (Traceability Matrix)

Bảng đối chiếu 1:1 chứng minh mọi bảng và collection trong thiết kế CSDL đều phục vụ trực tiếp cho các luồng nghiệp vụ trong tài liệu kiến trúc PDF:

| CSDL | Bảng / Collection | Chức Năng Nghiệp Vụ Phục Vụ | Sơ Đồ Trình Tự Kích Hoạt |
|:---|:---|:---|:---:|
| **SQL Server** | `Trips` | Quản lý vòng đời 10 trạng thái chuyến đi, kiểm soát đồng thời `RowVersion`, lưu `ExcludedDriverIds` | **SD 03, SD 04, SD 05, SD 06, SD 07, SD 08, SD 11** |
| (`TripDB`) | `TripStatusHistories` | Ghi log Audit mọi lần chuyển đổi trạng thái (bao gồm cả `Paid` -> `Completed`) | **SD 03, SD 04, SD 05, SD 06, SD 07, SD 08, SD 11** |
| | `OutboxMessages` | Ghi và phát sự kiện `TripRequestedEvent`, `TripAcceptedEvent`, `TripDroppedOffEvent`, `TripCancelledEvent`, `TripCompletedEvent` | **SD 04, SD 05, SD 06, SD 07, SD 08** |
| **MongoDB** | `Drivers` | Định vị GPS chu kỳ 10s (`bearing`, `speed`) & Truy vấn `$nearSphere` kết hợp `$nin` tìm tài xế | **SD 10 (F3.2), SD 11 (F3.3)** |
| (`MatchDB`) | `PricingConfigs` | Biểu phí tham số $c_d, c_t, c_m, T, \eta$ cho Strategy Pattern | **SD 09 (F3.1)** |
| | `SurgeZones` | Vùng đa giác nhân hệ số giá động $s$ theo khu vực | **SD 09 (F3.1)** |
| | `TripQuotes` | Lưu báo giá tạm thời kèm TTL index tự hủy sau 10 phút | **SD 03, SD 09 (F3.1)** |
| **PostgreSQL** | `accounts` | Quản lý số dư `RiderWallet`, `DriverWallet`, `PlatformRevenue` và tài khoản thu hộ `EscrowHolding` | **SD 12 (F4.1), SD 14 (F4.3)** |
| (`PayDB`) | `transactions` | Ghi nhận giao dịch tài chính (`Success`, `PaymentPending`, `Refunded`) + Unique Index chống thu trùng | **SD 12, SD 13, SD 14** |
| | `ledger_entries` | Ghi 3 bút toán Sổ cái kép bất biến ($\sum \text{Debit} = \sum \text{Credit}$) | **SD 12 (F4.1), SD 14 (F4.3)** |
| | `payment_tokens` | Token hóa thẻ/ví (`Momo`, `VNPay`, `Stripe`) chuẩn PCI-DSS (không lưu CVV/số thẻ) | **SD 12, SD 15 (F4.4)** |
| | `idempotency_records` | Khóa chống trừ tiền 2 lần theo `pay_trip_{tripId}` / header `X-Idempotency-Key` | **SD 12 (F4.1), SD 13 (F4.2)** |
| | `outbox_messages` | Transactional Outbox bắn `PaymentSettledEvent`, `PaymentFailedEvent`, `PaymentRefundedEvent` | **SD 12, SD 13, SD 14** |

## 6. Tổng Hợp Cấu Hình Docker & Connection String

Hạ tầng CSDL được đóng gói đồng bộ trong file `docker-compose.yml`:

| Dịch Vụ | Cổng Port | Database Name | Thông Tin Xác Thực (Local) | Chuỗi Kết Nối Chuẩn (.NET) |
|:---|:---:|:---:|:---|:---|
| **SQL Server** | `1433:1433` | `TripDB` | `User: sa`<br/>`Password: Password123!` | `Server=localhost,1433;Database=TripDB;User Id=sa;Password=Password123!;TrustServerCertificate=True;` |
| **MongoDB** | `27017:27017` | `MatchDB` | *No Authentication (Local Dev)* | `mongodb://localhost:27017/MatchDB` |
| **PostgreSQL** | `5432:5432` | `PayDB` | `User: postgres`<br/>`Password: postgrespassword` | `Host=localhost;Port=5432;Database=PayDB;Username=postgres;Password=postgrespassword;` |
| **RabbitMQ** | `5672`, `15672` | `/` (vhost) | `User: guest`<br/>`Password: guest` | `amqp://guest:guest@localhost:5672/` |
