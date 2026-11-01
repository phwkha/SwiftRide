# 💳 Kế Hoạch Chi Tiết Hoàn Thiện Payment Service Backend (`PayDB`)

> **Phạm vi:** `src/PaymentService/`, `tests/PaymentService.Tests/`  
> **Bổ sung cho:** [`plan_member_4_payment_service.md`](plan_member_4_payment_service.md). File đó mô tả "làm gì"; file này đi sâu vào **khoảng trống còn thiếu, quyết định kỹ thuật, thứ tự triển khai, tiêu chí hoàn thành và rủi ro** để Payment chạy đúng khi ghép với Trip Service & API Gateway.  
> **Sơ đồ trình tự:** SD 12 (Sổ cái kép), SD 13 (Idempotency + Retry), SD 14 (Refund Saga), SD 15 (Tokenization), liên quan SD 08 (đóng chuyến).  
> **Ước lượng:** ~11 ngày công (1 người).

---

## 1. Đánh Giá Hiện Trạng (Gap Analysis)

| Hạng mục | Hiện trạng | Còn thiếu / Vấn đề |
| :--- | :--- | :--- |
| Entities + EF Configurations | Đủ 6 bảng | `Transaction` thiếu `PaymentMethod`, `ExternalRef`, `RiderId`, `DriverId`; chưa có Unique Filtered Index chống thu trùng; `accounts` chưa có unique `(owner_id, account_type)` |
| `LedgerValidator` | Có | Chưa kiểm tra `Amount > 0` cho từng bút toán |
| Schema DB | Chưa có migration / `EnsureCreated` | Chạy service lên **không có bảng nào** |
| Seed data | Chưa có | 6 tài khoản + 1 token mẫu theo GUID cố định |
| Application layer | Trống (`.gitkeep`) | DTOs, `PaymentAppService`, Factory, Consumers |
| API layer | Chỉ có `GET /` | 5 endpoints (#19 – #23) + wrapper `ApiResponse<T>` + mã lỗi chuẩn |
| Messaging | Đã add MassTransit nhưng chưa đăng ký consumer | `TripDroppedOffConsumer`, Outbox Publisher |
| Shared Contracts | **Chưa có** `src/Shared.Contracts` | Thiếu luôn `PaymentRefundedEvent` trong danh sách 12 event của BE Lead |
| Outbox của TripService | Publish `object` ẩn danh `{ Type, Id, Data }` | Payment **sẽ không nhận được** `TripDroppedOffEvent` có kiểu — cần Trip sửa (xem mục 3) |
| Scalar UI | Docs FE trỏ tới `/scalar/v1` | Chưa cài `Scalar.AspNetCore` |
| Tests | Không có project test | Cần unit + integration (PostgreSQL thật) |
| `PaymentService.http` | Còn mẫu `weatherforecast` | Viết lại theo kịch bản demo |

---

## 2. Các Quyết Định Kỹ Thuật Cần Chốt Trước Khi Code

| # | Vấn đề | Quyết định đề xuất | Lý do |
| :-: | :--- | :--- | :--- |
| D1 | Tạo schema | **EF Core Migrations** + `Database.MigrateAsync()` khi khởi động | Filtered index, trigger append-only cần SQL ổn định; dễ thay đổi schema về sau hơn `EnsureCreated` |
| D2 | Chống race khi cập nhật số dư | Explicit transaction + `SELECT ... FOR UPDATE` các account theo **thứ tự `id` tăng dần** | Tránh lost update và deadlock khi 2 chuyến cùng ghi vào ví Sàn |
| D3 | Quy ước số dư | Thống nhất cho mọi loại tài khoản: `Debit` ⇒ `balance -= amount`, `Credit` ⇒ `balance += amount` | Khớp đúng SD 12/SD 14 hiện tại; một hàm `ApplyEntry()` duy nhất |
| D4 | Làm tròn VND | `driverShare = Math.Round(amount * 0.80m, 0, MidpointRounding.ToEven)`; `platformShare = amount - driverShare` | VND không có đơn vị lẻ; phép trừ đảm bảo luôn cân bằng |
| D5 | Refund ghi bút toán thế nào | **Đảo ngược từng dòng `LedgerEntry` của giao dịch gốc** (Debit↔Credit, cùng `AccountId`, cùng `Amount`) | Không cần đoán lại RiderWallet/Escrow/Driver; luôn cân bằng chính xác |
| D6 | Retry SD 13 | **Polly v8 `ResiliencePipeline`** chỉ bọc lệnh gọi cổng thanh toán (3 lần, 2s/4s/8s, jitter), chỉ retry `TransientGatewayException` | Retry của MassTransit chạy lại cả consumer và cuối cùng đẩy vào `_error` queue thay vì ghi `PaymentPending` |
| D7 | Lỗi không tạm thời (ví không đủ tiền, token không hợp lệ) | **Không retry**, ghi thẳng `PaymentPending` + `PaymentFailedEvent` | Khách có thể nạp tiền / đổi nguồn tiền rồi bấm "Thanh toán lại" |
| D8 | Một chuyến có nhiều lần thử | 1 dòng `transactions` cho mỗi chuyến: lần thử thủ công thành công sẽ **cập nhật** dòng `PaymentPending` cũ sang `Success` | Dễ truy vết; Unique Filtered Index vẫn đúng |
| D9 | Định danh người gọi | Đọc `X-User-Id`, `X-User-Role` do Gateway bơm vào; Admin mới được refund | Gateway đã xác thực JWT (plan BE Lead) |
| D10 | Cổng thanh toán ngoài | **Giả lập** (Momo/VNPay/Stripe), cấu hình được tỉ lệ lỗi; token bắt đầu bằng `tok_fail_` luôn timeout | Demo SD 13 chủ động, không phụ thuộc mạng ngoài |
| D11 | Shared contracts | Ưu tiên dùng `src/Shared.Contracts` của BE Lead; nếu chưa có thì tạo tạm `Contracts/SharedContracts.cs` **đúng namespace `SwiftRide.Contracts.Events`**, xoá khi project chung được merge | MassTransit định tuyến theo `namespace + tên type` |

---

## 3. Phụ Thuộc & Việc Cần Phối Hợp Với Thành Viên Khác

| Với ai | Việc cần thống nhất | Hạn chót |
| :--- | :--- | :--- |
| BE Lead | Thêm `PaymentRefundedEvent(Guid TransactionId, Guid TripId, Guid RefundTransactionId, decimal RefundAmount, string Reason)` vào `SwiftRide.Contracts.Events` | Ngày 1 |
| BE Lead | Đưa `ApiResponse<T>` vào `Shared.Contracts` (hoặc chấp nhận bản copy tạm) | Ngày 1 |
| BE Lead | YARP routes: `/api/payments/**`, `/api/payment-tokens/**`, `/api/accounts/**` + alias `/payments`, `/payments/{id}/refund`; bơm `X-User-Id`, `X-User-Role`, `X-Correlation-Id`; **cho phép header `X-Idempotency-Key` đi qua** (CORS `WithHeaders`) | Ngày 8 |
| Trip Service | Outbox publisher phải **deserialize về record có kiểu** (`TripDroppedOffEvent`) rồi `Publish(typedEvent)` — không publish `object` ẩn danh | Ngày 5 |
| Trip Service | `TripDroppedOffEvent.IdempotencyKey = "pay_trip_{tripId}"`, `ActualFare > 0`, `PaymentMethod ∈ {Wallet, Momo, VNPay, Stripe}` | Ngày 5 |
| Trip Service | Consumer `PaymentSettledEvent` → `Paid` → `Completed`; `PaymentFailedEvent` → `PaymentPending`; từ `PaymentPending` nhận tiếp `PaymentSettledEvent` vẫn hợp lệ | Ngày 9 |
| Frontend | Lỗi mới trong luồng retry: `409 PAYMENT_DUPLICATE` khi cùng key đang xử lý | Ngày 8 |

---

## 4. Cấu Trúc Thư Mục Đích

```text
src/PaymentService/
├── Domain/
│   ├── Entities/Transaction.cs                  # [SỬA] + PaymentMethod, ExternalRef, RiderId, DriverId, UpdatedAt
│   ├── Enums/PaymentEnums.cs                    # [SỬA] + enum PaymentMethod { Wallet, Momo, VNPay, Stripe }
│   ├── Exceptions/PaymentExceptions.cs          # [MỚI] PaymentDomainException(code, httpStatus), TransientGatewayException
│   ├── Services/LedgerValidator.cs              # [SỬA] + kiểm tra Amount > 0
│   ├── Services/LedgerPosting.cs                # [MỚI] ApplyEntry(account, entry), BuildSettlementEntries, BuildReversalEntries
│   └── Factories/
│       ├── IPaymentMethodProcessor.cs           # [MỚI] ChargeAsync / RefundAsync
│       ├── PaymentMethodFactory.cs              # [MỚI] Create(PaymentMethod)
│       ├── WalletPaymentProcessor.cs            # [MỚI] Không gọi ra ngoài, chỉ trả Success
│       └── SimulatedGatewayProcessor.cs         # [MỚI] Momo / VNPay / Stripe giả lập
├── Application/
│   ├── DTOs/PaymentDtos.cs                      # [MỚI] Khớp 100% mục 5 của api_contract
│   ├── Services/IdempotencyService.cs           # [MỚI] TryBegin / Complete / Fail / Replay
│   ├── Services/PaymentAppService.cs            # [MỚI] ProcessPayment, MarkPending, Refund
│   ├── Services/PaymentTokenAppService.cs       # [MỚI] Create / List
│   ├── Services/AccountQueryService.cs          # [MỚI] GetByOwner
│   └── Consumers/TripDroppedOffConsumer.cs      # [MỚI]
├── Infrastructure/
│   ├── Data/
│   │   ├── Configurations/*.cs                  # [SỬA] Transaction, Account (xem Phase 1)
│   │   ├── Migrations/                          # [MỚI] InitialPayDb (+ trigger append-only)
│   │   └── PaymentDbSeeder.cs                   # [MỚI]
│   ├── Gateways/GatewaySimulationOptions.cs     # [MỚI] FailureRate, LatencyMs
│   ├── Resilience/PaymentResilience.cs          # [MỚI] Polly pipeline "gateway-charge"
│   └── BackgroundServices/
│       ├── PaymentOutboxPublisherService.cs     # [MỚI]
│       └── IdempotencyCleanupService.cs         # [MỚI – tuỳ chọn] Xoá key hết hạn
├── Api/
│   ├── PaymentEndpoints.cs                      # [MỚI] #19, #20
│   ├── PaymentTokenEndpoints.cs                 # [MỚI] #21, #22
│   ├── AccountEndpoints.cs                      # [MỚI] #23
│   ├── RequestContext.cs                        # [MỚI] Đọc X-User-Id / X-User-Role
│   └── PaymentExceptionHandler.cs               # [MỚI] IExceptionHandler → ApiResponse.Fail
├── Contracts/SharedContracts.cs                 # [TẠM] Chỉ khi chưa có src/Shared.Contracts
├── Program.cs                                   # [SỬA] DI, MassTransit consumer, worker, migrate+seed, Scalar
└── PaymentService.http                          # [SỬA] Kịch bản demo
tests/PaymentService.Tests/                      # [MỚI] xUnit + Testcontainers.PostgreSql + MassTransit.Testing
```

Package cần thêm: `Scalar.AspNetCore`, `Polly.Core` (hoặc `Microsoft.Extensions.Resilience`). Project test: `xunit`, `FluentAssertions`, `Testcontainers.PostgreSql`, `MassTransit` (test harness có sẵn).

---

## 5. Kế Hoạch Triển Khai Theo Phase

### Phase 0 — Chuẩn bị & đồng bộ hợp đồng (0.5 ngày)
- [ ] Tạo nhánh `feat/payment-service` từ `main`.
- [ ] Chốt với BE Lead các mục ở **Mục 3** (đặc biệt `PaymentRefundedEvent`).
- [ ] Reference `Shared.Contracts` hoặc tạo file tạm `Contracts/SharedContracts.cs`.
- [ ] Thêm packages, xoá `UseHttpsRedirection()` (service chạy HTTP sau Gateway).

**Xong khi:** `dotnet build` sạch, các type `TripDroppedOffEvent`, `PaymentSettledEvent`, `PaymentFailedEvent`, `PaymentRefundedEvent`, `ApiResponse<T>` dùng được.

### Phase 1 — Domain, Schema & Seed (1 ngày)
1. **`Transaction`** thêm:
   ```csharp
   public Guid? RiderId { get; set; }
   public Guid? DriverId { get; set; }
   public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Wallet;
   public string? ExternalRef { get; set; }       // mã giao dịch phía cổng ngoài, cần cho refund
   public Guid? OriginalTransactionId { get; set; } // dòng Refund trỏ về giao dịch gốc
   public DateTimeOffset? UpdatedAt { get; set; }
   ```
2. **`TransactionConfiguration`**: map các cột snake_case mới, `payment_method` lưu dạng string, và:
   ```csharp
   builder.HasIndex(t => t.TripId)
       .HasDatabaseName("ux_transactions_trip_success")
       .IsUnique()
       .HasFilter("status = 'Success' AND transaction_type = 'RidePayment'");
   ```
   > Lưu ý: index hiện tại `HasIndex(t => t.TripId)` (không unique) phải đổi tên khác (`ix_transactions_trip_id`) để không trùng.
3. **`AccountConfiguration`**: `HasIndex(a => new { a.OwnerId, a.AccountType }).IsUnique()`.
4. **`LedgerValidator`**: ném lỗi nếu có `Amount <= 0`; ném `PaymentDomainException("LEDGER_UNBALANCED", 500)` thay cho `InvalidOperationException` để map được mã lỗi chuẩn.
5. **Migration** `InitialPayDb`, thêm raw SQL bảo vệ sổ cái append-only:
   ```sql
   CREATE FUNCTION forbid_ledger_mutation() RETURNS trigger AS $$
   BEGIN RAISE EXCEPTION 'ledger_entries is append-only'; END; $$ LANGUAGE plpgsql;
   CREATE TRIGGER trg_ledger_append_only BEFORE UPDATE OR DELETE ON ledger_entries
   FOR EACH ROW EXECUTE FUNCTION forbid_ledger_mutation();
   ```
6. **`PaymentDbSeeder`**: `MigrateAsync()` rồi seed đúng bộ GUID ở plan cũ (Rider 1 = 500.000, Driver 1/2 = 100.000, Driver 3 = 200.000, PlatformRevenue = 0, EscrowHolding = 10.000.000, token Momo `e7b0a708-…`). Seed **chỉ khi bảng rỗng** để chạy lại nhiều lần an toàn.

**Xong khi:** `docker compose up -d postgres` + chạy service → `\dt` thấy 6 bảng + `__EFMigrationsHistory`; `\di` thấy `ux_transactions_trip_success`; `UPDATE ledger_entries ...` bị DB từ chối.

### Phase 2 — Factory Pattern & cổng thanh toán giả lập (1 ngày)
```csharp
public interface IPaymentMethodProcessor
{
    PaymentMethod Method { get; }
    Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct);   // ChargeRequest: UserId, Amount, Token?, IdempotencyKey
    Task<RefundResult> RefundAsync(string externalRef, decimal amount, CancellationToken ct);
}
```
- `PaymentMethodFactory` nhận `IEnumerable<IPaymentMethodProcessor>` qua DI và trả processor theo `Method`; method không hỗ trợ → `PaymentDomainException("VALIDATION_ERROR", 422)`.
- `WalletPaymentProcessor`: không gọi ra ngoài (việc trừ tiền nằm ở sổ cái), `ExternalRef = null`.
- `SimulatedGatewayProcessor` (đăng ký 3 instance Momo/VNPay/Stripe): giả lập độ trễ; token `tok_fail_*` hoặc rơi vào `FailureRate` → ném `TransientGatewayException`; trả `ExternalRef = $"{provider}_{Guid.NewGuid():N}"`. Gateway giả lập phải **tôn trọng idempotency key** (cùng key trả cùng `ExternalRef`) — dùng `ConcurrentDictionary` trong bộ nhớ.
- `PaymentResilience`: pipeline `"gateway-charge"` — `MaxRetryAttempts = 3`, `Delay = 2s`, `BackoffType = Exponential`, `UseJitter = true`, `ShouldHandle = TransientGatewayException`. Trong test, cấu hình delay = 0.
- **Không bao giờ log `Token`** — chỉ log `MaskedToken`.

**Xong khi:** Unit test factory trả đúng processor; test pipeline retry đúng 3 lần rồi ném lỗi.

### Phase 3 — Lõi quyết toán Sổ cái kép + Idempotency (2 ngày) — SD 12
`PaymentAppService.ProcessPaymentAsync(ProcessPaymentCommand cmd, string idempotencyKey, PaymentSource source, CancellationToken ct)`

**Bước A – Idempotency (`IdempotencyService.TryBeginAsync`)**
1. `RequestHash = SHA256(tripId|riderId|driverId|amount|method|tokenId)`.
2. `INSERT idempotency_records (key, hash, 'Processing')`.
   - Thành công → tiếp tục.
   - Vi phạm PK (`PostgresException.SqlState == "23505"`) → đọc bản ghi cũ:
     - `Success` → **trả lại `ResponsePayload`** (HTTP 200 giống lần đầu; consumer thì ack và bỏ qua).
     - `Processing` và `CreatedAt` < 2 phút → `409 PAYMENT_DUPLICATE` ("đang xử lý").
     - `Failed` hoặc `Processing` quá 2 phút (tiến trình chết giữa chừng) → chiếm lại bằng `UPDATE ... SET status='Processing' WHERE key=@k AND status=@old` (kiểm tra rows affected = 1).
     - Khác `RequestHash` → `409 PAYMENT_DUPLICATE` ("khoá đã dùng cho yêu cầu khác").
3. Kiểm tra mức chuyến đi: đã có `RidePayment` `Success` cho `tripId` → `409 PAYMENT_DUPLICATE` (chặn trường hợp key tự động và key `_manual` khác nhau).

**Bước B – Validate**
- `amount > 0`; `riderId`, `driverId` có ví `WalletLiability`; method hợp lệ.
- Method ≠ Wallet: lấy token theo `paymentTokenId` (phải thuộc `riderId` và đúng provider) hoặc token `IsDefault` của provider đó; không có → `422 VALIDATION_ERROR`.

**Bước C – Thu tiền ngoài (chỉ khi Momo/VNPay/Stripe)** — chạy *ngoài* DB transaction, bọc bởi pipeline `"gateway-charge"`, truyền **cùng idempotency key** mọi lần thử.

**Bước D – Ghi sổ (1 DB transaction, isolation `ReadCommitted`)**
```csharp
await using var tx = await db.Database.BeginTransactionAsync(ct);
var ids = new[] { debitAccountId, driverWalletId, platformRevenueId }.Order().ToArray();
var accounts = await db.Accounts
    .FromSql($"SELECT * FROM accounts WHERE id = ANY({ids}) ORDER BY id FOR UPDATE")
    .ToDictionaryAsync(a => a.Id, ct);

if (method == PaymentMethod.Wallet && accounts[debitAccountId].Balance < amount)
    throw new PaymentDomainException("INSUFFICIENT_BALANCE", 422, ...);

var entries = LedgerPosting.BuildSettlementEntries(txId, debitAccountId, driverWalletId, platformRevenueId, amount);
LedgerValidator.EnsureBalanced(entries);
foreach (var e in entries) LedgerPosting.ApplyEntry(accounts[e.AccountId], e);

// Tạo mới Transaction, hoặc cập nhật dòng PaymentPending sẵn có của trip này (D8)
// IdempotencyRecord -> Success + ResponsePayload = JSON(PaymentResponse)
// OutboxMessage { EventType = nameof(PaymentSettledEvent), Payload = JSON(new PaymentSettledEvent(...)) }
await db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
```
- `debitAccount` = ví Rider nếu Wallet, `EscrowHolding` nếu cổng ngoài.
- Nếu Bước D thất bại **sau khi** cổng ngoài đã thu tiền → gọi `processor.RefundAsync(externalRef)` để bù, rồi đánh dấu idempotency `Failed` và ném lỗi (ghi log cảnh báo).
- Bắt `DbUpdateException` do `ux_transactions_trip_success` → `409 PAYMENT_DUPLICATE`.

**Xong khi:** Wallet 65.000 → Rider 435.000, Driver 1 152.000, Platform 13.000; gọi lại cùng key không đổi số dư; tổng số dư toàn hệ thống trước/sau không đổi.

### Phase 4 — Outbox Publisher (0.5 ngày)
- `PaymentOutboxPublisherService` theo mẫu `TripService/.../OutboxPublisherService.cs` (PeriodicTimer 3s, batch 20), **nhưng publish event có kiểu**:
  ```csharp
  private static readonly Dictionary<string, Type> EventTypes = new()
  {
      [nameof(PaymentSettledEvent)]  = typeof(PaymentSettledEvent),
      [nameof(PaymentFailedEvent)]   = typeof(PaymentFailedEvent),
      [nameof(PaymentRefundedEvent)] = typeof(PaymentRefundedEvent),
  };
  var evt = JsonSerializer.Deserialize(message.Payload, EventTypes[message.EventType])!;
  await publishEndpoint.Publish(evt, evt.GetType(), ctx => ctx.MessageId = message.Id, ct);
  ```
- Lấy batch bằng `FOR UPDATE SKIP LOCKED` để chạy nhiều instance không publish trùng.
- Lỗi publish: ghi `Error`, giữ `ProcessedAt = null` để lần sau thử lại. Consumer phía Trip phải idempotent (at-least-once).

**Xong khi:** RabbitMQ UI (`:15672`) thấy exchange `SwiftRide.Contracts.Events:PaymentSettledEvent` nhận message; dòng outbox có `processed_at`.

### Phase 5 — Consumer `TripDroppedOffEvent` & đường lỗi SD 13 (1 ngày)
```csharp
public async Task Consume(ConsumeContext<TripDroppedOffEvent> context)
{
    var m = context.Message;
    try
    {
        await _payments.ProcessPaymentAsync(m.ToCommand(), m.IdempotencyKey, PaymentSource.Event, context.CancellationToken);
    }
    catch (Exception ex) when (ex is TransientGatewayException
                               || ex is PaymentDomainException { Code: "INSUFFICIENT_BALANCE" or "VALIDATION_ERROR" })
    {
        await _payments.MarkPendingAsync(m, m.IdempotencyKey, ex.Message, context.CancellationToken);
    }
    // PAYMENT_DUPLICATE: bỏ qua (đã xử lý). Lỗi DB/hạ tầng khác: ném ra để MassTransit retry message.
}
```
- `MarkPendingAsync` (1 DB transaction): upsert `Transaction` `Status = PaymentPending`, `ErrorDetail`; idempotency → `Failed`; outbox `PaymentFailedEvent(tripId, "PaymentPending", reason)`.
- `Program.cs`: `x.AddConsumer<TripDroppedOffConsumer>()`, `x.SetKebabCaseEndpointNameFormatter()`; thêm `UseMessageRetry(r => r.Interval(3, 5s))` **chỉ cho lỗi hạ tầng** (DB mất kết nối) — lỗi nghiệp vụ đã được xử lý trong consumer.
- Prefetch nhỏ (`PrefetchCount = 8`) là đủ; trùng lặp đồng thời đã được PK idempotency chặn.

**Xong khi:** publish thủ công `TripDroppedOffEvent` (MassTransit test harness hoặc endpoint dev) → có `PaymentSettledEvent`; dùng token `tok_fail_*` → sau ~14s có `PaymentFailedEvent`, transaction `PaymentPending`, số dư không đổi.

### Phase 6 — Refund Saga SD 14 (1 ngày)
`RefundAsync(Guid transactionId, string reason, Guid requestedBy)`:
1. Tải giao dịch gốc kèm `LedgerEntries`; không tồn tại → `404 TRANSACTION_NOT_FOUND`*; khác `Success` hoặc không phải `RidePayment` → `409 PAYMENT_ALREADY_REFUNDED`*.
2. Nếu gốc thu qua cổng ngoài → `processor.RefundAsync(ExternalRef, amount)` (có retry), trước DB transaction.
3. DB transaction: `SELECT ... FOR UPDATE` dòng `transactions` gốc (chặn 2 refund song song) + các account liên quan; `entries = LedgerPosting.BuildReversalEntries(original, refundTxId)` (D5); `EnsureBalanced`; `ApplyEntry`; gốc → `Refunded`; tạo `Transaction` `Refund` (`OriginalTransactionId` = gốc, `Description = reason`); outbox `PaymentRefundedEvent`.
4. Ví tài xế được phép âm sau refund (khoản nợ) — Saga bù trừ không được thất bại vì thiếu số dư; ghi log cảnh báo.
5. Idempotent tự nhiên: refund lần 2 trả 409 vì gốc đã `Refunded`.

\* Mã lỗi mới — cần thêm vào bảng mã lỗi ở `api_contract_and_fe_integration.md` mục 10.

**Xong khi:** sau refund, 3 tài khoản về đúng số dư ban đầu; `ledger_entries` có 6 dòng (3 gốc + 3 đảo), không dòng nào bị sửa.

### Phase 7 — Token hoá SD 15 & tra cứu tài khoản (0.5 ngày)
- `POST /api/payment-tokens`: validate `provider ∈ {Momo, VNPay, Stripe}`, `token` không rỗng, ≤ 255 ký tự, **từ chối chuỗi trông như số thẻ** (regex 13–19 chữ số) để chặn lưu PAN. `MaskedToken = "**** " + token[^4..]`. Nếu `isDefault` (hoặc là token đầu tiên của user) → bỏ default các token khác **trong cùng transaction**. Trả `201` + `Location`.
- `GET /api/payment-tokens`: theo `X-User-Id`, sắp `IsDefault desc, CreatedAt desc`; **không bao giờ trả trường `Token`**.
- `GET /api/accounts/{ownerId}`: trả `AccountDto` ví `WalletLiability`; với `ownerId` của Platform trả `PlatformRevenue`. Chỉ cho xem ví của chính mình trừ khi `X-User-Role = Admin` → `403 FORBIDDEN`.

### Phase 8 — Lớp API, xử lý lỗi, tài liệu (1 ngày)
| # | Method & Route | Yêu cầu | Thành công | Lỗi chính |
| :-: | :--- | :--- | :--- | :--- |
| 19 | `POST /api/payments/process` | Header `X-Idempotency-Key` bắt buộc; `riderId == X-User-Id` (trừ Admin) | `200 PaymentResponse` | 400 thiếu key, 403, 409 `PAYMENT_DUPLICATE`, 422 `INSUFFICIENT_BALANCE` / `VALIDATION_ERROR`, 502 khi cổng ngoài hết retry (đồng thời transaction → `PaymentPending`) |
| 20 | `POST /api/payments/{transactionId}/refund` | Chỉ `Admin` | `200 RefundPaymentResponse` | 403, 404, 409 |
| 21 | `POST /api/payment-tokens` | `X-User-Id` | `201 PaymentTokenDto` | 422 |
| 22 | `GET /api/payment-tokens` | `X-User-Id` | `200 PaymentTokenDto[]` | — |
| 23 | `GET /api/accounts/{ownerId}` | `X-User-Id` / Admin | `200 AccountDto` | 403, 404 |

- Dùng Minimal API `MapGroup("/api/payments")…`, mọi response bọc `ApiResponse<T>`, `TraceId = HttpContext.TraceIdentifier`.
- `PaymentExceptionHandler : IExceptionHandler` map `PaymentDomainException` → `ApiResponse.Fail(code)`; lỗi chưa xử lý → 500 không lộ stacktrace.
- JSON enum dạng chuỗi (`JsonStringEnumConverter`) để `status: "Success"` khớp FE.
- `app.MapScalarApiReference()` → `http://localhost:5114/scalar/v1`.
- Viết lại `PaymentService.http` theo kịch bản demo ở Mục 7.

### Phase 9 — Kiểm thử (2 ngày)
**Unit tests** (không DB):
1. `LedgerValidator`: 65.000 = 52.000 + 13.000 → pass; lệch 1đ → `LEDGER_UNBALANCED`; amount ≤ 0 → lỗi.
2. Chia 80/20 với số lẻ (ví dụ 65.555) → tổng luôn bằng `amount`.
3. `BuildReversalEntries` đảo đúng chiều, cùng số tiền, cân bằng.
4. `PaymentMethodFactory` trả đúng processor; method lạ → lỗi.
5. Mask token; từ chối chuỗi giống số thẻ.

**Integration tests** (Testcontainers PostgreSQL thật — provider InMemory không hỗ trợ filtered index, `FOR UPDATE`, trigger):
6. Wallet thành công: số dư 3 tài khoản đúng, 3 ledger entries, 1 outbox `PaymentSettledEvent`.
7. Cùng key gọi 2 lần → 1 transaction, trừ tiền 1 lần, response giống nhau.
8. **10 request song song cùng key** → đúng 1 thành công, số dư trừ đúng 1 lần.
9. 2 key khác nhau cho cùng `tripId` → lần 2 nhận `PAYMENT_DUPLICATE`.
10. Ví không đủ tiền → 422, không có ledger entry nào.
11. Gateway lỗi tạm thời 3 lần → `PaymentPending` + `PaymentFailedEvent`; sau đó thanh toán thủ công key mới → `Success`, cùng 1 dòng transaction.
12. Refund trả 3 tài khoản về số dư ban đầu; refund lần 2 → 409.
13. `UPDATE ledger_entries` bị trigger chặn.
14. Bất biến: `SUM(debit) = SUM(credit)` trên toàn bảng `ledger_entries` sau mọi kịch bản.

**Consumer tests** (MassTransit `ITestHarness`): publish `TripDroppedOffEvent` → assert `Published<PaymentSettledEvent>()`; publish trùng 2 lần → chỉ 1 settlement.

### Phase 10 — Tích hợp end-to-end & demo (0.5 ngày)
- Chạy đủ `docker compose up -d` + Gateway + Trip + Payment.
- Luồng chuẩn: Trip `drop-off` → `PaymentSettledEvent` → Trip `Paid` → `Completed`, FE nhận `PaymentCompleted`.
- Luồng lỗi: rider có token default `tok_fail_*` → Trip `PaymentPending` → FE bấm "Thanh toán lại" qua Gateway → `Completed`.
- Kiểm tra log không chứa token thật, có `X-Correlation-Id`.

---

## 6. Tiến Độ Tổng Hợp

| Ngày | Phase | Kết quả bàn giao |
| :-: | :--- | :--- |
| 1 | 0, 1 | Build sạch, schema + seed chạy được |
| 2 | 2 | Factory + gateway giả lập + retry pipeline |
| 3 – 4 | 3 | `ProcessPaymentAsync` hoàn chỉnh (SD 12) |
| 5 | 4, 5 (bắt đầu) | Outbox publisher; chốt event có kiểu với Trip |
| 6 | 5 | Consumer + đường `PaymentPending` (SD 13) |
| 7 | 6, 7 | Refund Saga (SD 14), Tokens + Accounts (SD 15) |
| 8 | 8 | 5 endpoints + error handling + Scalar + `.http` → **bàn giao FE & Gateway** |
| 9 – 10 | 9 | Bộ test unit + integration + consumer |
| 11 | 10 | E2E với Trip/Gateway, sửa lỗi, demo |

---

## 7. Kịch Bản Tự Kiểm Tra Nhanh (`PaymentService.http`)

| Bước | Request | Kỳ vọng |
| :-: | :--- | :--- |
| 1 | `GET /api/accounts/1111…` (X-User-Id = Rider 1) | `balance: 500000` |
| 2 | `POST /api/payments/process` key `test-001`, Wallet, 65.000, driver `2222…` | `200`, `driverCreditAmount: 52000`, `platformFeeAmount: 13000` |
| 3 | Lặp lại bước 2 | `200` cùng `transactionId`, số dư không đổi |
| 4 | Bước 2 với key `test-002`, cùng `tripId` | `409 PAYMENT_DUPLICATE` |
| 5 | `POST /api/payments/process` amount 10.000.000 | `422 INSUFFICIENT_BALANCE` |
| 6 | `POST /api/payment-tokens` `{ provider: "VNPay", token: "tok_fail_0001" }` rồi thanh toán bằng VNPay | ~14s sau trả `502`, transaction `PaymentPending` |
| 7 | `POST /api/payments/{txId}/refund` với X-User-Role = Admin | Rider 500.000, Driver 1 100.000, Platform 0 |
| 8 | Lặp lại bước 7 | `409` |

---

## 8. Definition of Done

- [ ] 5 endpoints khớp 100% DTO trong `api_contract_and_fe_integration.md` (tên trường camelCase, enum dạng chuỗi).
- [ ] Không thể trừ tiền 2 lần cho 1 chuyến (đã chứng minh bằng test song song + unique index).
- [ ] Mọi thay đổi số dư đi kèm ledger entries cân bằng, trong cùng DB transaction với outbox.
- [ ] `ledger_entries` append-only ở tầng DB.
- [ ] Không lưu / log / trả về số thẻ, CVV hay token thô.
- [ ] Event publish ra RabbitMQ là record có kiểu trong `SwiftRide.Contracts.Events`.
- [ ] Toàn bộ test xanh; `dotnet build` không warning mới.
- [ ] Đã chạy E2E qua Gateway cùng Trip Service (luồng chuẩn + luồng `PaymentPending` + refund).

---

## 9. Rủi Ro & Cách Giảm Thiểu

| Rủi ro | Ảnh hưởng | Giảm thiểu |
| :--- | :--- | :--- |
| Trip vẫn publish `object` ẩn danh | Payment không bao giờ nhận event → chuyến kẹt ở `DroppedOff` | Chốt ở ngày 5; trong lúc chờ dùng MassTransit test harness / endpoint dev để tự publish |
| Thu tiền cổng ngoài thành công nhưng ghi DB lỗi | Khách bị trừ tiền mà hệ thống không ghi nhận | Gọi `RefundAsync` bù ngay + log cảnh báo; gateway giả lập idempotent theo key |
| Deadlock khi nhiều chuyến cùng ghi ví Sàn | Timeout, retry liên tục | Khoá account theo thứ tự `id` cố định |
| Tranh chấp nóng trên dòng `PlatformRevenue` | Thông lượng thấp khi tải lớn | Chấp nhận cho phạm vi đồ án; ghi chú hướng mở rộng: tính số dư Sàn từ ledger thay vì cập nhật cột `balance` |
| Bản ghi idempotency kẹt `Processing` khi service crash | Thanh toán lại bị chặn mãi | Cho phép chiếm lại sau 2 phút (Phase 3, Bước A) |
| Contract thay đổi giữa chừng | Lệch DTO với FE | Mọi thay đổi DTO/mã lỗi phải cập nhật `api_contract_and_fe_integration.md` trong cùng PR |
