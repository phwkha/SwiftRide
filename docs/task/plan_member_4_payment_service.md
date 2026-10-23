# 💳 Kế Hoạch Phát Triển Cá Nhân — Thành Viên 4 (Dev BE 4): Payment Service & PostgreSQL (`PayDB`)

> **Người phụ trách:** Thành viên 4 (Dev BE 4)  
> **Nhánh Git làm việc:** `feat/payment-service`  
> **Phạm vi thư mục sở hữu độc quyền:**  
> - `src/PaymentService/`  
> - `tests/PaymentService.Tests/`  
> **Cổng dịch vụ & CSDL:** Port `:5114` — PostgreSQL (`localhost:5432`, Database: `PayDB`, User: `postgres`, Pass: `postgrespassword`)  
> **Sơ đồ trình tự phụ trách:** **SD 12 (Sổ cái kép Double-entry Ledger), SD 13 (Idempotency & Retry có giới hạn), SD 14 (Hoàn tiền bồi thường Saga), SD 15 (Token hóa PCI-DSS Factory Pattern)**.

## 1. Hiện Trạng Mã Nguồn Đã Có Sẵn & Những File Cần Tạo Thêm

Trong repo `SwiftRide.slnx`, `src/PaymentService` là một dịch vụ **bắt buộc** theo đề bài PDF và đã được dựng sẵn toàn bộ 6 Entities (`Account`, `Transaction`, `LedgerEntry`, `PaymentToken`, `IdempotencyRecord`, `OutboxMessage`), `PaymentEnums.cs`, `LedgerValidator.cs` và `PaymentDbContext.cs`. Bạn cần hoàn thiện các phần sau:

```text
src/PaymentService/
├── Domain/
│   ├── Enums/PaymentEnums.cs              # [ĐÃ CÓ] AccountType, EntryType, TransactionStatus...
│   ├── Entities/
│   │   ├── Transaction.cs                 # [CẬP NHẬT] Thêm thuộc tính PaymentMethod
│   │   └── (5 Entities còn lại)           # [ĐÃ CÓ]
│   ├── Services/LedgerValidator.cs        # [ĐÃ CÓ] EnsureBalanced (Tổng Debit == Tổng Credit)
│   └── Factories/                         # [TẠO MỚI] Factory Pattern (Mục 7 PDF)
│       ├── IPaymentMethodProcessor.cs     # [TẠO MỚI] Interface xử lý cổng thanh toán
│       └── PaymentMethodFactory.cs        # [TẠO MỚI] Khởi tạo bộ xử lý Wallet / Momo / VNPay / Stripe
├── Application/
│   ├── DTOs/PaymentDtos.cs                # [TẠO MỚI] ProcessPaymentRequest, PaymentResponse, Refund...
│   ├── Services/PaymentAppService.cs      # [TẠO MỚI] Sổ cái kép, Idempotency, Saga Refund, Tokenization
│   └── Consumers/
│       └── TripDroppedOffConsumer.cs      # [TẠO MỚI] Lắng nghe TripDroppedOffEvent + Retry 3 lần
├── Infrastructure/
│   ├── Data/
│   │   ├── PaymentDbContext.cs            # [ĐÃ CÓ]
│   │   ├── PaymentDbSeeder.cs             # [TẠO MỚI] Seed ví Rider, Driver, Sàn & Token Momo mẫu
│   │   └── Configurations/
│   │       └── TransactionConfiguration.cs# [CẬP NHẬT] Thêm Unique Filtered Index chống thu trùng
│   └── BackgroundServices/
│       └── PaymentOutboxPublisherService.cs # [TẠO MỚI] Quét outbox_messages bắn lên RabbitMQ
└── Api/
    └── PaymentEndpoints.cs                # [TẠO MỚI] 5 REST Endpoints cho Payment, Token & Account
```

## 2. Hướng Dẫn Code Chi Tiết Theo Từng Ngày

### Ngày 1 – Ngày 2: Cập Nhật Configuration, Seed Data, Factory Pattern & Outbox Worker
1. **Cập nhật `Transaction.cs` và `TransactionConfiguration.cs`:**
   - Trong `Transaction.cs`, thêm:
     ```csharp
     public string PaymentMethod { get; set; } = "Wallet"; // Wallet, Momo, VNPay, Stripe
     ```
   - Trong `TransactionConfiguration.cs`, map cột `payment_method` và thêm **Unique Filtered Index** bảo vệ ở tầng vật lý PostgreSQL không bao giờ có 2 giao dịch thu tiền chuyến đi thành công cho cùng 1 `trip_id`:
     ```csharp
     builder.Property(t => t.PaymentMethod)
         .HasColumnName("payment_method")
         .HasMaxLength(50)
         .HasDefaultValue("Wallet")
         .IsRequired();

     builder.HasIndex(t => t.TripId)
         .HasDatabaseName("ux_transactions_trip_success")
         .IsUnique()
         .HasFilter("\"status\" = 'Success' AND \"transaction_type\" = 'RidePayment'");
     ```
2. **Viết `PaymentDbSeeder.cs` với đúng bộ GUID cố định của dự án:**
   - Gọi `await dbContext.Database.EnsureCreatedAsync()`.
   - Nếu bảng `accounts` trống, tạo sẵn 6 tài khoản kế toán chuẩn:
     - **Ví `Rider 1`:** `OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111")`, `OwnerType = "Rider"`, `AccountType = AccountType.WalletLiability`, `Balance = 500000m`.
     - **Ví `Driver 1`:** `OwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222")`, `OwnerType = "Driver"`, `AccountType = AccountType.WalletLiability`, `Balance = 100000m`.
     - **Ví `Driver 2`:** `OwnerId = Guid.Parse("33333333-3333-3333-3333-333333333333")`, `OwnerType = "Driver"`, `AccountType = AccountType.WalletLiability`, `Balance = 100000m`.
     - **Ví `Driver 3`:** `OwnerId = Guid.Parse("44444444-4444-4444-4444-444444444444")`, `OwnerType = "Driver"`, `AccountType = AccountType.WalletLiability`, `Balance = 200000m`.
     - **Tài khoản Doanh thu Sàn (20%):** `OwnerId = Guid.Parse("99999999-9999-9999-9999-999999999999")`, `OwnerType = "Platform"`, `AccountType = AccountType.PlatformRevenue`, `Balance = 0m`.
     - **Tài khoản Thu hộ Cổng ngoài (`EscrowHolding`):** `OwnerId = Guid.Parse("99999999-9999-9999-9999-999999999999")`, `OwnerType = "Platform"`, `AccountType = AccountType.EscrowHolding`, `Balance = 10000000m`.
   - Nếu bảng `payment_tokens` trống, tạo 1 token mẫu cho `Rider 1`:
     - `Id = Guid.Parse("e7b0a708-3bb8-4fc1-a589-cf7320b7a8a1")`, `UserId = Guid.Parse("11111111-1111-1111-1111-111111111111")`, `Provider = "Momo"`, `Token = "tok_momo_88a91b2c3d"`, `MaskedToken = "**** 5678"`, `IsDefault = true`.
3. **Hiện thực hóa Factory Pattern (`PaymentMethodFactory.cs` - Mục 7 PDF):**
   - Tạo `IPaymentMethodProcessor` với phương thức `Task<(bool Success, string? ExternalRef, string? Error)> ChargeAsync(Guid userId, decimal amount, string? token, string idempotencyKey)`.
   - `PaymentMethodFactory.Create(string method)` trả về `WalletPaymentProcessor` hoặc `TokenizedGatewayProcessor` (`Momo`, `VNPay`, `Stripe`).
4. **Tạo `PaymentOutboxPublisherService.cs`:**
   - Quét bảng `outbox_messages` (`ProcessedAt == null`) mỗi 3 giây và publish `PaymentSettledEvent`, `PaymentFailedEvent`, `PaymentRefundedEvent` lên RabbitMQ.

### Ngày 3 – Ngày 5: Xây Dựng Sổ Cái Kép, Idempotency & 5 REST APIs
1. **Logic quyết toán Sổ cái kép trong `PaymentAppService.ProcessPaymentAsync` (SD 12):**
   - **Bước 1 (Idempotency Check):** Tra cứu `idempotency_records` theo `idempotencyKey`. Nếu đã tồn tại và `Status == IdempotencyStatus.Success`, deserialize `ResponsePayload` trả về ngay mà **không trừ tiền lại**.
   - **Bước 2 (Xác định tài khoản ghi Nợ - `DebitAccount`):**
     - Nếu `paymentMethod == "Wallet"`: Tìm tài khoản `WalletLiability` của `riderId`. Kiểm tra `Balance >= amount` (nếu không đủ tiền, ném lỗi trả về `422 INSUFFICIENT_BALANCE`). `DebitAccount = riderWallet`.
     - Nếu `paymentMethod IN ("Momo", "VNPay", "Stripe")`: Kiểm tra `payment_tokens` của `riderId`, gọi `PaymentMethodFactory` thu tiền qua Token. Tìm tài khoản `EscrowHolding` của Sàn làm `DebitAccount` (không trừ vào `riderWallet`).
   - **Bước 3 (Tạo 3 dòng bút toán Sổ cái kép bất biến):**
     ```csharp
     var driverShare = Math.Round(amount * 0.80m, 2);
     var platformShare = amount - driverShare; // 20%

     var entries = new List<LedgerEntry>
     {
         new() { TransactionId = tx.Id, AccountId = debitAccount.Id, EntryType = EntryType.Debit, Amount = amount },
         new() { TransactionId = tx.Id, AccountId = driverWallet.Id, EntryType = EntryType.Credit, Amount = driverShare },
         new() { TransactionId = tx.Id, AccountId = platformRevenue.Id, EntryType = EntryType.Credit, Amount = platformShare }
     };

     // Bắt buộc gọi validator đảm bảo Tổng Debit == Tổng Credit
     LedgerValidator.EnsureBalanced(entries);
     ```
   - **Bước 4 (Cập nhật số dư & Ghi Outbox trong cùng 1 Transaction):**
     - `debitAccount.Balance -= amount;`
     - `driverWallet.Balance += driverShare;`
     - `platformRevenue.Balance += platformShare;`
     - Lưu `Transaction` (`Status = Success`), 3 dòng `LedgerEntry`, cập nhật `IdempotencyRecord` (`Status = Success`) và thêm `OutboxMessage` (`EventType = "PaymentSettledEvent"`).
     - `await _dbContext.SaveChangesAsync();`
2. **Xây dựng 5 REST APIs (`PaymentEndpoints.cs`):**
   - `POST /api/payments/process` (SD 12, SD 13): Đọc Header `X-Idempotency-Key` (bắt buộc có) và gọi `ProcessPaymentAsync`.
   - `POST /api/payments/{transactionId}/refund` (SD 14 - Giao dịch bồi thường Saga):
     - Tìm giao dịch gốc (`Status == Success`). Đổi `Status = Refunded`, tạo 1 giao dịch mới `TransactionType = Refund` và ghi **3 bút toán đảo sổ cái** (`Credit` hoàn lại 100% vào `RiderWallet`/`EscrowHolding`, `Debit` thu hồi 80% từ `DriverWallet` và `Debit` thu hồi 20% từ `PlatformRevenue`), gọi `LedgerValidator.EnsureBalanced()`, cập nhật số dư 3 tài khoản và ghi `PaymentRefundedEvent` vào Outbox.
   - `POST /api/payment-tokens` (SD 15): Chỉ nhận `provider` và `token` (không nhận số thẻ/CVV), tự sinh `maskedToken = "**** " + token[^4..]` và lưu vào `payment_tokens`.
   - `GET /api/payment-tokens`: Lấy danh sách token của `X-User-Id`.
   - `GET /api/accounts/{ownerId}`: Trả về thông tin số dư ví `AccountDto`.

### Ngày 6 – Ngày 8: Xây Dựng `TripDroppedOffConsumer` & Retry Có Giới Hạn (SD 13)
- Lắng nghe `TripDroppedOffEvent` từ RabbitMQ.
- Thực hiện retry tối đa 3 lần (với cùng `msg.IdempotencyKey`) nếu gặp lỗi kết nối cổng thanh toán tạm thời.
- Nếu sau 3 lần vẫn lỗi: Lưu `Transaction` với `Status = TransactionStatus.PaymentPending`, cập nhật `IdempotencyRecord` (`Status = Failed`) và ghi `PaymentFailedEvent(msg.TripId, "PaymentPending", ex.Message)` vào `outbox_messages`.

### Ngày 9 – Ngày 10: Viết Unit Tests (`tests/PaymentService.Tests`)
- Viết ít nhất 5 Unit Tests bằng `xUnit`:
  1. `LedgerValidator.EnsureBalanced` chạy thành công khi `Debit (65,000) == Credit (52,000 + 13,000)`.
  2. `LedgerValidator.EnsureBalanced` ném `InvalidOperationException` khi tổng Debit lệch tổng Credit dù chỉ 1 đồng.
  3. Gửi 2 lần `ProcessPaymentAsync` với cùng một `IdempotencyKey` chỉ tạo ra duy nhất 1 `Transaction` và trừ tiền đúng 1 lần.
  4. `RefundAsync` tạo đúng 3 bút toán đảo ngược và trả lại đúng số dư ban đầu cho cả 3 tài khoản.

## 3. Cách Tự Test Độc Lập 100% (Không Cần Chờ TripService)
1. Chạy `docker compose up -d postgres rabbitmq` và `dotnet run --project src/PaymentService`.
2. Dùng `src/PaymentService/PaymentService.http` hoặc Scalar UI:
   - Gọi `GET http://localhost:5114/api/accounts/11111111-1111-1111-1111-111111111111` $\to$ thấy số dư ban đầu `500,000 VND`.
   - Gọi `POST http://localhost:5114/api/payments/process` kèm Header `X-Idempotency-Key: test-key-001` với `amount: 65000`, `paymentMethod: "Wallet"`.
   - Gọi lại `GET /api/accounts/...` thấy ví Rider còn `435,000 VND`, ví Driver 1 tăng lên `152,000 VND` (+52,000), ví Sàn tăng lên `13,000 VND`.
   - Bấm gọi lại `POST /api/payments/process` lần thứ 2 với cùng `X-Idempotency-Key: test-key-001` $\to$ nhận lại kết quả `200 OK` cũ nhưng số dư các ví không hề bị trừ thêm!
   - Gọi tiếp `POST http://localhost:5114/api/payments/{transactionId}/refund` $\to$ kiểm tra 3 ví quay về đúng số dư ban đầu (`500,000`, `100,000`, `0`).
