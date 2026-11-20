# Kiểm tra API server để bàn giao frontend — 16/11/2026

Base URL: `https://kha-dev.impala-tritone.ts.net`.

Đối chiếu với [FE_INTEGRATION_GUIDE.md](FE_INTEGRATION_GUIDE.md), route/DTO/consumer trong source hiện tại và gọi trực tiếp server bằng tài khoản demo. Kiểm tra WebSocket thực tế, không chỉ negotiate. Không triển khai hay sửa code backend trên server.

## Cập nhật ngày 20/11/2026

| Mục | Kết quả |
|---|---|
| Matching timeout ngày 16/11 | Đọc lại được trip `e850d434-95a5-49bd-a8a4-af561fb954a0`. Snapshot còn `fareConfirmed:true`, không có driver và chuyển Cancelled sau khoảng 20 giây. Không có log lịch sử của các service để xác định đoạn nào đã mất offer. |
| Lỗi ghép lại phát hiện khi đọc code | `RetryMatchingCommand` có thể đến trước `DriverRejectedOfferEvent`. Code cũ bỏ qua retry khi còn giữ tài xế vừa từ chối. Bản sửa giải phóng reservation bị loại trước khi tìm tài xế tiếp theo. Redis chỉ xóa lock của đúng chuyến. Đã thêm log theo `tripId` từ outbox đến matching và Hub. |
| Rate limit | Gateway chạy authentication trước rate limit. JWT hợp lệ dùng bucket theo user, request chưa xác thực dùng IP kết nối. Header client tự gửi không đổi bucket. Có thể cấu hình proxy tin cậy theo `ForwardedHeaders__TrustedProxies__0`. Kiểm tra local bằng Gateway thật xác nhận HTTP 429 và Rider/Driver cùng IP có bucket riêng. |
| `POST /api/payment-tokens` | **Live HTTP 201** với token Momo giả lập. Phản hồi có `Location`, trả token đã che và `isDefault:false`. `GET /api/payment-tokens` trả 200 và có bản ghi mới `4fad6876-9079-4b8b-ba6a-120bbcb8aad2`. Nguồn Momo mặc định cũ vẫn giữ nguyên. Bản ghi test còn trên server demo. |
| Event trạng thái | Thêm `TripStatusChanged` cho `EnRoute`, `PickedUp`, `DroppedOff`, `PaymentPending`, `Paid`, `Completed`. Sự kiện được ghi vào outbox cùng thay đổi trạng thái. `Failed` cũng được phát qua outbox sau khi lưu, gồm trường hợp vượt giới hạn từ chối. |
| Reconnect và JWT hết hạn | Test WebSocket local xác nhận kết nối bị đóng khi JWT hết hạn. Kết nối mới dùng JWT mới, join lại phòng và nhận event. Hướng dẫn FE mô tả cách dùng chung lần refresh giữa REST và SignalR, join lại và đọc snapshot. Hub ghi log nếu offer đến sau `expiresAt`. Việc tự giải phóng xe khi offer hết hạn tự nhiên vẫn cần xử lý riêng. |
| Cảnh báo dependency | Nâng OpenTelemetry lên 1.21.0 và SignalR Redis lên 10.0.15. Docker runtime dùng ASP.NET Core 10.0.15. PaymentService tham chiếu EF Relational 10.0.12 để đồng bộ với các gói EF trong solution. Build sạch và NuGet không báo gói trực tiếp hoặc bắc cầu có lỗ hổng tại thời điểm kiểm tra. |

`dotnet test SwiftRide.slnx --no-restore -m:1` qua **111/111 ca**. `docker compose config --quiet` hợp lệ. Test WebSocket dùng TestHost local và kiểm tra Gateway chạy trên loopback. Chưa chạy lại luồng nghiệp vụ hoàn chỉnh với bản sửa trên server.

**Trạng thái bàn giao.** Đã có dữ liệu phản hồi thành công trên server demo cho cả 24 API trong bảng lịch sử. Bản sửa code ngày 20/11 vẫn ở workspace. Để đối chiếu lần timeout tiếp theo sau khi triển khai, lọc log `trip-service` và `matching-service` theo `tripId`. So thời điểm outbox publish, đặt/giải phóng reservation, phát offer và Hub gửi vào phòng. Log Hub cho biết server đã gửi vào group, còn FE cần theo dõi việc nhận event và trạng thái kết nối phía client.

## Cập nhật khắc phục ngày 17/11/2026 — code local, chưa triển khai server

Đã sửa 4 hạn chế ưu tiên trong workspace. Toàn bộ kết quả **live ngày 16/11** ở các phần sau vẫn là lịch sử của bản server trước sửa; không dùng chúng để kết luận bản sửa đã hoạt động trên server. Hướng dẫn FE đã cập nhật theo contract của bản sửa.

| Hạn chế | Thay đổi trong code | Quy tắc sau khi triển khai |
|---|---|---|
| Hub thiếu xác thực/phân quyền | Gateway bảo vệ cả route Hub và negotiate; TripService xác thực JWT, kiểm tra danh tính và chủ phòng | Không JWT/sai JWT/hết hạn → 401; chỉ Driver chính chủ vào phòng Driver; chỉ Rider chủ chuyến hoặc Driver được gán join Trip; GUID sai và người ngoài bị từ chối |
| Availability thiếu quyền chính chủ | MatchingService lấy user từ JWT được xác thực tại service, bỏ tin tưởng header danh tính | Chỉ role Driver với sub trùng path id; sai role/ID → 403, không cập nhật repository |
| Process/refund thiếu quyền và đối chiếu dữ liệu | Process chỉ cho Rider chủ chuyến; lấy snapshot từ TripService bằng JWT của người gọi; refund chỉ Rider chủ chuyến hoặc Admin | Process chỉ chạy khi PaymentPending, driverId/actualFare hợp lệ; body khác dữ liệu Trip → 400 PAYMENT_TRIP_MISMATCH; sai trạng thái → 409 PAYMENT_NOT_PENDING; thiếu quyền → 403; không xác minh được Trip → 503 và không ghi sổ |
| Đọc ví thiếu kiểm tra quyền | PaymentService kiểm tra chủ ví từ JWT trước truy vấn số dư | Chủ ví hoặc Admin được đọc; user khác → 403; không thể giả mạo bằng X-User-Id/X-User-Role |

### Cấu hình và triển khai bản sửa

- TripService, MatchingService, PaymentService đều xác thực JWT trực tiếp. REST không còn dùng danh tính mặc định khi Development hoặc tin tưởng X-User-* do client gửi. Gateway overview chuyển tiếp Bearer để giữ được quyền đọc Trip/vị trí.
- Tất cả service phải dùng cùng `Jwt__Secret`, `Jwt__Issuer`, `Jwt__Audience` với Gateway. Compose đã truyền các giá trị này từ `SWIFTRIDE_JWT_SECRET`, `SWIFTRIDE_JWT_ISSUER`, `SWIFTRIDE_JWT_AUDIENCE`; secret tối thiểu 32 byte UTF-8. Khi chạy `dotnet run` riêng từng service, cần truyền cấu hình tương tự; không đưa signing secret cho FE.
- PaymentService cần `Downstream__TripService` trỏ trực tiếp TripService, không trỏ ngược Gateway. Compose đã đặt `http://trip-service:8080`; local mặc định `http://localhost:5105`. Nếu service không truy cập được hoặc snapshot không hợp lệ, process/refund của Rider bị từ chối thay vì ghi tiền khi chưa xác minh.
- Cần build/deploy đồng bộ Gateway, TripService, MatchingService và PaymentService. Lệnh khi triển khai bằng Compose: `docker compose up -d --build api-gateway trip-service matching-service payment-service`. Lệnh này **chưa được chạy** trên server trong lần sửa này.
- Sau triển khai cần smoke test lại login → Wallet trip, PaymentPending → retry, refund, đọc ví; kiểm tra 401 khi không JWT và 403 khi dùng tài khoản khác. Giữ SignalR accessTokenFactory và join lại phòng sau reconnect. Bản sửa đóng kết nối Hub khi JWT hết hạn.

### Kiểm thử local của bản sửa

- `dotnet test SwiftRide.slnx --no-restore -m:1`: **108/108 ca qua**, gồm 54 ca trong Security.Tests, 19 TripService, 19 MatchingService và 16 PaymentService.
- Security.Tests chạy HTTP và WebSocket bằng ASP.NET TestHost với JWT ký thật cho test; kiểm tra token thiếu/giả/hết hạn, giả mạo X-User-*, quyền vào phòng, availability, ví, process và refund. Ledger dùng EF InMemory; Trip lookup được giả lập ở kiểm thử API và kiểm tra HTTP client riêng. Chưa chạy bản sửa với SQL/PostgreSQL/RabbitMQ/Redis thật hoặc server triển khai.
- Các ca từ chối process/refund xác nhận số dư và bút toán không bị thay đổi. Có kiểm thử đối chiếu rider/driver/amount, chỉ cho phép PaymentPending, TripService không khả dụng, chuyển tiếp Bearer, và không trả response thanh toán cũ khi tái sử dụng idempotency key với request khác.
- `docker compose config --quiet` hợp lệ. NuGet vẫn báo cảnh báo dependency có sẵn (SignalR Redis/OpenTelemetry) và xung đột phiên bản EF Relational; việc nâng cấp dependency nằm ngoài 4 sửa đổi này.

Các hạn chế matching timeout, rate limit, sự kiện realtime còn thiếu, cổng thanh toán thật và chức năng tài khoản/lịch sử chưa được xử lý trong bản sửa này.

## Kết luận

**Có thể bàn giao để FE bắt đầu tích hợp trên môi trường demo**, kèm các giới hạn bên dưới. Route, HTTP method, envelope, tên trường response và payload realtime đã kiểm tra đều khớp tài liệu. Đã cập nhật base URL/Hub URL trong hướng dẫn và thêm [Postman environment server](../postman/SwiftRide.Server.postman_environment.json).

- Đã gọi cả **24 REST operation**: **23 operation có response thành công**, `POST /api/payment-tokens` kiểm tra nhánh validation HTTP 422 và đối chiếu DTO trong code; chưa tạo nguồn tiền mới để xác minh HTTP 201.
- So sánh đệ quy tập tên trường trong response thành công thực tế của 23 operation với JSON ví dụ tương ứng: **không có sai khác**. Giá trị mẫu, timestamp, ID và độ chính xác số không dùng để so sánh bằng nhau.
- Đã nhận đủ **5 tên event SignalR**, kiểm tra hai biến thể `TripStatusChanged`: `DriverAccepted` và `Cancelled`.
- Đã invoke thành công **4 method**: `JoinDriverRoom`, `LeaveDriverRoom`, `JoinTripRoom`, `LeaveTripRoom`.
- Đây không phải xác nhận toàn bộ nhánh lỗi, độ ổn định tải cao, quyền truy cập hoặc an toàn production.

## API nào FE tích hợp được, API nào chưa

Phân loại dựa trên bằng chứng kiểm tra ngày 16/11/2026, không phải kết quả kiểm thử trực tiếp ứng dụng FE:

- **Tích hợp demo:** Đã gọi thành công trên server, response khớp cấu trúc trong hướng dẫn; FE có thể triển khai trong phạm vi ghi ở từng dòng. Không đồng nghĩa đã kiểm tra mọi loại xe, phương thức thanh toán, quyền truy cập hoặc khả năng chịu tải.
- **Chưa xác minh đủ:** Có endpoint và contract, nhưng chưa kiểm tra được nhánh thành công. FE có thể chuẩn bị request/UI, chưa coi chức năng đã nghiệm thu.
- **Chưa hỗ trợ:** Backend chưa có chức năng hoặc chưa kết nối dịch vụ thật; FE cần BE bổ sung trước khi hoàn thiện chức năng đó.

**Tổng hợp: 23 REST operation tích hợp demo được; 1 operation chưa xác minh đủ (`POST /api/payment-tokens`). Chưa có bằng chứng một endpoint trong bộ 24 API bị hỏng hoàn toàn hoặc sai route khiến FE không thể gọi.** Những chức năng chưa hỗ trợ được liệt kê riêng bên dưới, không gán nhãn “API hỏng” cho chức năng chưa kiểm tra.

### Trạng thái từng REST API

| # | Method và route | FE tích hợp được chưa? | HTTP đã xác minh | Phạm vi và giới hạn |
|---|---|---|---|---|
| 01 | POST `/api/auth/login` | Tích hợp demo | 200 | Tài khoản demo Rider, Driver 1, Driver 2; nhận user và cặp token |
| 02 | POST `/api/auth/refresh` | Tích hợp demo | 200, 401 | Đổi cả hai token; refresh token cũ dùng lại bị từ chối |
| 03 | GET `/api/auth/me` | Tích hợp demo | 200, 401 | Khôi phục danh tính; thiếu JWT bị từ chối |
| 04 | POST `/api/matching/quote` | Tích hợp demo | 200, 400 | Đã kiểm tra báo giá Bike; chưa nghiệm thu giá Car4/Car7 trên server |
| 05 | POST `/api/matching/find-drivers` | Tích hợp demo | 200 | Trả array xe khả dụng; không giữ xe hay tạo offer |
| 06 | GET `/api/drivers/{id}/location` | Tích hợp demo | 200 | Đọc GPS; bearing/speed có thể null |
| 07 | PUT `/api/drivers/{id}/location` | Tích hợp demo | 200, 400, 403 | Driver cập nhật GPS của mình; có activeTripId thì đã nhận event GPS |
| 08 | PUT `/api/drivers/{id}/availability` | Tích hợp demo | 200, 404 | Bật online được; BE còn thiếu kiểm tra role Driver và ID chính chủ |
| 09 | POST `/api/trips` | Tích hợp demo | 201, 400 | Tạo Trip từ quote và địa chỉ; chưa matching cho đến confirm |
| 10 | POST `/api/trips/{id}/confirm-quote` | Tích hợp demo | 200, 404 | Xác nhận giá, kích hoạt matching; có một lượt không nhận offer trong 20 giây, chưa rõ nguyên nhân |
| 11 | GET `/api/trips/active` | Tích hợp demo | 200, 404 | Khôi phục Trip; Rider demo có sẵn active trip seed |
| 12 | GET `/api/trips/{id}` | Tích hợp demo | 200, 403, 404 | Đọc snapshot, polling; Driver chưa được gán bị từ chối |
| 13 | PUT `/api/trips/{id}/accept` | Tích hợp demo | 200, 404 | Driver nhận offer; status 1; đã nhận event DriverAccepted |
| 14 | PUT `/api/trips/{id}/reject` | Tích hợp demo | 200, 404 | Driver từ chối; đã xác minh offer chuyển sang Driver 2 |
| 15 | PUT `/api/trips/{id}/start-route` | Tích hợp demo | 200, 404 | Status 2; FE phía còn lại cần đọc lại Trip vì chưa có event riêng cho bước này |
| 16 | PUT `/api/trips/{id}/pick-up` | Tích hợp demo | 200, 404 | Status 3; FE phía còn lại cần đọc lại Trip vì chưa có event riêng cho bước này |
| 17 | PUT `/api/trips/{id}/drop-off` | Tích hợp demo | 200, 404 | Wallet tự quyết toán đến Completed; nguồn tiền chưa liên kết đã chuyển PaymentPending; chưa có thanh toán cổng thật |
| 18 | PUT `/api/trips/{id}/cancel` | Tích hợp demo | 200, 404 | Hủy khi đang matching; status 8; đã nhận event Cancelled và TripOfferExpired |
| 19 | GET `/api/trips/{id}/overview` | Tích hợp demo | 200, 404 | Đọc trip + vị trí; driverLocation có thể null; lỗi 404 có body rỗng |
| 20 | POST `/api/payments/process` | Tích hợp demo | 200, 400, 409 | Đã retry PaymentPending bằng Wallet; cần X-Idempotency-Key; BE còn thiếu kiểm tra quyền và đối chiếu dữ liệu chuyến |
| 21 | POST `/api/payments/{transactionId}/refund` | Tích hợp demo | 200, 404, 409 | Đã hoàn giao dịch ví demo; BE còn thiếu kiểm tra quyền người hoàn tiền; chưa xác minh hoàn tiền qua cổng thật |
| 22 | POST `/api/payment-tokens` | **Chưa xác minh đủ** | 422 | Mới kiểm tra provider sai; cần gọi với token demo hợp lệ, nhận 201 và đọc lại danh sách để nghiệm thu |
| 23 | GET `/api/payment-tokens` | Tích hợp demo | 200 | Hiển thị danh sách nguồn tiền seed; chỉ trả maskedToken; không chứng minh liên kết cổng thật |
| 24 | GET `/api/accounts/{ownerId}` | Tích hợp demo | 200 | Đọc số dư ví demo; BE còn thiếu kiểm tra quyền xem ví của ownerId |

`GET /` cũng trả HTTP 200 với `{"service":"ApiGateway","status":"Running"}`.

### Phần FE chưa thể hoàn thiện bằng backend hiện tại

| Chức năng FE | Trạng thái | API liên quan | BE cần bổ sung / FE xử lý tạm |
|---|---|---|---|
| Thu tiền thật qua Momo/VNPay/Stripe | Chưa hỗ trợ cổng thật | POST `/api/payments/process`, PUT `/api/trips/{id}/drop-off` | Backend hiện giả lập provider; dùng Wallet demo trước, bổ sung tích hợp cổng thật để thu tiền thật |
| Liên kết nguồn tiền thật qua SDK/provider | Chưa xác nhận hỗ trợ luồng thật | POST `/api/payment-tokens` | Endpoint lưu token không thay thế luồng cấp token của provider; cần kiểm tra tạo 201 và triển khai luồng provider thật |
| Đăng ký tài khoản | Chưa có endpoint | Chưa có route đăng ký trong bộ endpoint hiện tại | Dùng tài khoản demo được cấp; BE bổ sung API nếu FE cần màn đăng ký |
| Quên / đặt lại mật khẩu | Chưa có endpoint | Chưa có route khôi phục mật khẩu trong bộ endpoint hiện tại | BE bổ sung API và luồng xác minh người dùng |
| Danh sách lịch sử chuyến | Chưa có endpoint danh sách | GET `/api/trips/active` và GET `/api/trips/{id}` không thay thế API lịch sử | BE bổ sung API danh sách, lọc theo user và phân trang |
| Theo dõi mọi bước chuyến chỉ bằng realtime | Chưa hỗ trợ đủ event | Hub `/hubs/trips`; GET `/api/trips/{id}` | Chưa có TripStatusChanged cho mọi bước start-route/pick-up/drop-off/Paid/Completed và chưa có event PaymentPending riêng; FE đọc lại snapshot/polling |

### Những điều không đồng nghĩa API không tích hợp được

- **Thiếu phân quyền:** Availability, đọc ví, process, refund và Hub có thể dùng trong demo, nhưng BE phải hoàn thiện kiểm tra quyền trước khi dùng dữ liệu thực. Ẩn nút trong FE không thay thế phân quyền backend.
- **CORS:** Đã kiểm tra localhost port 3000/5173/5174. Nếu domain FE khác, cần cấu hình origin ở Gateway trước khi nghiệm thu trên domain đó; lỗi CORS không chứng minh route API bị hỏng.
- **Độ ổn định matching:** Một lượt không nhận offer trong 20 giây, lượt kiểm tra riêng sau đó thành công. Chưa đủ bằng chứng xác định nguyên nhân; FE cần timeout, reconnect và snapshot fallback.
- **Phạm vi chưa kiểm tra:** Car4/Car7, reconnect/token hết hạn trên SignalR, biến thể event Failed, offer hết hạn tự nhiên, SSE/Long Polling và tải/429 chưa được nghiệm thu live. Chưa kiểm tra không có nghĩa backend không hỗ trợ.

## Luồng nghiệp vụ đã chạy

### Thanh toán tự động bằng Wallet

Trip `459e9cc2-de79-4026-ba0d-d7203d986df3`:

`quote → create → JoinTripRoom → confirm → ReceiveTripOffer → accept → GPS → overview → start-route → pick-up → drop-off Wallet → PaymentCompleted → Completed`.

- Fare 38.322 VND; phần tài xế 30.657,6 VND; phí sàn 7.664,4 VND.
- Transaction `30ee2f97-a86a-4627-a607-55995fc700bc` đã hoàn tiền thành công sau kiểm tra.
- Gửi refund lần thứ hai trả 409 `PAYMENT_DUPLICATE`.

### PaymentPending và thanh toán lại

Trip `57173958-ea1f-4fb7-8e31-106aee2d51cb`:

`drop-off Stripe khi chưa liên kết → REST status 5 → POST payments/process Wallet → PaymentCompleted → REST status 7`.

- Cùng idempotency key gửi lại trả 409 `PAYMENT_DUPLICATE`.
- Transaction `319983c9-1905-470c-a109-d26ecc003c6a` đã hoàn tiền thành công.
- Chỉ kiểm tra ví demo nội bộ; không gọi cổng thanh toán thật.

### Từ chối, ghép lại và hủy

Trip `b10f86fa-fc9d-4175-8539-67b8bf543e35`:

`Driver 1 nhận offer → reject → Driver 2 nhận offer → Rider cancel → TripStatusChanged(Cancelled) + TripOfferExpired`.

Một lượt test trước đó, trip `e850d434-95a5-49bd-a8a4-af561fb954a0`, không nhận offer trong thời gian chờ 20 giây; đã hủy chuyến này. Lượt riêng tiếp theo nhận đầy đủ offer, rematch và event hủy. Chưa xác định nguyên nhân timeout đơn lẻ; không kết luận matching luôn ổn định từ một lượt thành công. Client kiểm thử ở lượt riêng có gửi SignalR keepalive định kỳ; FE nên dùng SDK chính thức để quản lý kết nối.

### Dữ liệu sau kiểm tra

- Hai trip thanh toán ở Completed (7), hai trip còn lại ở Cancelled (8). Lịch sử trip/giao dịch test vẫn nằm trong database.
- Ví Rider trở về **500.000 VND**, Driver 1 trở về **100.000 VND**.
- Đã khôi phục tọa độ, bearing và speed của Driver 1; timestamp cập nhật vị trí thay đổi do kiểm tra.
- Đọc lại find-drivers cuối cùng trả cả Driver 1 và Driver 2 khả dụng. Một request đọc cuối lượt trước đó gặp lỗi mạng; gọi lại trả HTTP 200.
- Chuyến seed `f47ac10b-58cc-4372-a567-0e02b2c3d479` giữ nguyên Requested, `fareConfirmed:false`, cùng rowVersion như trước kiểm tra.
- Không thêm/sửa nguồn tiền liên kết. Không lưu mật khẩu hoặc JWT vào tài liệu hay environment server.

## Realtime đã nhận trên WebSocket

| Event | Kết quả |
|---|---|
| ReceiveTripOffer | Đủ trường tuyến, giá, durationMinutes, countdownSeconds, expiresAt; tới Driver đúng phòng |
| DriverLocationUpdated | Đủ driverId, tọa độ, bearing, speed, updatedAt; không có tripId trong payload |
| TripStatusChanged | Đã nhận DriverAccepted và Cancelled; payload trực tiếp, newStatus là string |
| PaymentCompleted | Đủ transactionId, totalAmount, paymentMethod, netEarnings, status, completedAt |
| TripOfferExpired | Driver 2 nhận khi Rider hủy chuyến đang được đề nghị |

Chưa kiểm tra live biến thể `TripStatusChanged(newStatus:Failed)`, offer hết hạn tự nhiên, reconnect/token hết hạn và transport SSE/Long Polling. Payload `Failed` đã đối chiếu code consumer.

## Các điểm FE/BE cần biết khi bàn giao

1. **URL:** Hướng dẫn cũ chỉ có localhost; đã bổ sung server REST và Hub. Import collection APIs Only với environment Server, điền `demo_password` theo mật khẩu demo BE cung cấp.
2. **CORS:** Preflight thực tế hợp lệ cho localhost port 3000, 5173, 5174, với Authorization/Content-Type/X-Idempotency-Key. Origin khác cần cấu hình ở Gateway. OPTIONS từ origin `https://kha-dev.impala-tritone.ts.net` trả 204 nhưng không có Access-Control-Allow-Origin; 204 riêng lẻ không chứng minh CORS được cho phép. Request cùng origin không cần CORS.
3. **Hub chưa bắt buộc JWT:** Negotiate không token trả HTTP 200; [TripHub](../src/TripService/Api/Hubs/TripHub.cs) nhận ID phòng trực tiếp, không kiểm tra ownership. Chưa thử nghe dữ liệu bằng kết nối anonymous. BE cần bổ sung xác thực và phân quyền Hub trước khi dùng dữ liệu thực.
4. **Không đồng nhất “role FE nên dùng” với kiểm tra quyền BE:** [MatchingEndpoints](../src/MatchingService/Api/MatchingEndpoints.cs) kiểm tra Driver/self cho GPS, nhưng availability chưa gọi kiểm tra tương tự. [PaymentEndpoints](../src/PaymentService/Api/PaymentEndpoints.cs) chưa so quyền chủ ví/giao dịch cho đọc balance, process và refund. Đây là phát hiện từ source, chưa thử khai thác trên dữ liệu khác. Các role ghi trong hướng dẫn là luồng FE dự kiến, không phải chứng nhận phân quyền đầy đủ.
5. **Rate limit:** [Gateway Program](../src/ApiGateway/Program.cs) chạy rate limiter trước authentication. Vì vậy mô tả cũ “100 request/phút/user” không được bảo đảm; client cùng IP có thể chia sẻ bucket. Đã sửa hướng dẫn; chưa chạy kiểm thử tải/429 trên server.
6. **Dữ liệu demo và số:** Rider có sẵn active trip seed. `distanceKm` trong quote/create là `2.079845808247298`, GET lại là `2.08` do lưu trữ. FE cập nhật theo snapshot; không so số thực bằng nhau để kết luận response sai.
7. **Thanh toán ngoài đang giả lập:** [TokenizedGatewayProcessor](../src/PaymentService/Domain/Factories/TokenizedGatewayProcessor.cs) là bộ xử lý demo cho Momo/VNPay/Stripe. Kiểm tra này không xác nhận tích hợp cổng thanh toán thật hay luồng SDK liên kết nguồn tiền.
