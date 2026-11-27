# Kế hoạch hoàn thiện SwiftRide theo đề bài

**Trạng thái hiện tại.** Mục 1 và 2 đã triển khai trong mã nguồn và nghiệm thu backend local qua Gateway. Mốc demo tiếp theo là mục 3 trên bản triển khai dùng chung với hai ứng dụng Rider và Driver. [Đề bài SwiftRide](RideLite-KienTrucPhanMem.pdf) · [Báo cáo kiểm tra API trước thay đổi](FE_API_VERIFICATION_2026-11-16.md)

| Thứ tự | Hạng mục | Mốc hoàn thành |
|---|---|---|
| 1 | Xác minh báo giá ở backend | Giá lưu trong Trip và số tiền quyết toán lấy từ báo giá đã được xác minh. |
| 2 | Hoàn thiện vòng đời offer | Đúng tài xế có thể nhận hoặc từ chối offer còn hiệu lực. Offer hết hạn được dọn và chuyến có kết quả cuối. |
| 3 | Kiểm thử tích hợp và nghiệm thu FE | Rider và Driver hoàn thành một chuyến Wallet bằng giao diện trên bản server mới, kèm kết quả kiểm tra các nhánh lỗi. |
| 4 | Hoàn thiện yêu cầu chất lượng §9 | Có bằng chứng về nhất quán giao dịch, bồi thường Saga, xác thực và khả năng quan sát. |

## 1. Xác minh báo giá ở backend (§2.2–2.3)

- [x] TripService tra cứu báo giá theo `quoteId` qua API nội bộ Matching/Pricing, dùng JWT của Rider. Backend kiểm tra chủ báo giá, hạn, tọa độ đón/trả và loại xe. [Xác minh báo giá](../src/TripService/Application/Services/TripAppService.Quotes.cs)
- [x] Trip lấy cước, quãng đường và ETA từ báo giá đã xác minh. Lần xác nhận đầu ghi giá và một `TripRequestedEvent` trong cùng giao dịch SQL. Chuyến dùng giá đã chốt đến khi quyết toán.
- [x] Demo theo §2.2. Tạo `Requested`, lấy báo giá, gắn bằng `PUT /api/trips/{id}/quote`, rồi xác nhận. Client cũ vẫn có thể gửi `quoteId` lúc tạo chuyến. Postman có thư mục **01. Kiểm thử mục 1 và 2**. [Hợp đồng FE](FE_INTEGRATION_GUIDE.md) · [Collection SwiftRide](../postman/SwiftRide.postman_collection.json)
- [x] Nghiệm thu giá hợp lệ, hết hạn, khác Rider, sai tuyến, sai loại xe và các trường giá/ETA/quãng đường client gửi sai. Đối chiếu `actualFare`, số dư ví và giao dịch Wallet sau khi báo giá gốc đã được dọn.

## 2. Hoàn thiện vòng đời offer (§2.2 và §4)

- [x] TripService lưu `offerId`, tài xế được mời, lượt matching và hạn 15 giây. Matching giữ tài xế theo chuyến và theo lượt. Offer chỉ được tạo sau khi Rider chốt giá. [Luồng offer](../src/TripService/Application/Services/TripAppService.Offers.cs) · [Xử lý matching](../src/MatchingService/Application/Services/DriverMatchingAppService.cs)
- [x] `accept` và `reject` yêu cầu role Driver, đúng người được mời và offer còn hiệu lực. SQL Server dùng `RowVersion` để chỉ ghi một kết quả khi các thao tác đến cùng lúc.
- [x] Worker xử lý offer hết hạn dù Driver đã ngắt kết nối. Backend phát `TripOfferExpired`, giải phóng reservation và thử tài xế khác, tối đa 4 lượt. Chuyến chuyển `Failed` khi cạn lượt hoặc hết tài xế phù hợp.
- [x] Nghiệm thu nhận hợp lệ, sai tài xế, nhận quá hạn, hết hạn tự nhiên, từ chối rồi ghép lại, hết lượt và hủy khi đang có offer. Kiểm tra event lặp sau khi offer đóng và sau khi chuyến hoàn tất.

### Kết quả nghiệm thu local trước khi chuyển sang Kafka, ngày 23/11/2026

`dotnet test SwiftRide.slnx` đạt 117/117. Kiểm tra tích hợp qua Gateway và SignalR chạy với SQL Server, MongoDB, PostgreSQL, broker của bản nghiệm thu và Redis local.

| Ca kiểm tra | Trip ID | Kết quả |
|---|---|---|
| Báo giá và Wallet | `8ae61049-0667-435d-91a3-f8d14931157e` | Hai lần xác nhận đồng thời tạo một yêu cầu matching. Chuyến `Completed`, `actualFare` và số tiền trừ ví cùng là 38.324 ₫. PostgreSQL ghi một giao dịch với ba bút toán. |
| Báo giá hết hạn trước khi chốt | `11cb211c-bd98-4ea0-a3e6-718bfae48932` | HTTP 409 `QUOTE_EXPIRED`. |
| Từ chối, ghép lại và để offer tự hết hạn | `4a07aead-7f82-4fed-95b4-7a946b54275a` | Driver thứ hai nhận `ReceiveTripOffer` rồi `TripOfferExpired`. Nhận muộn trả 409. Chuyến `Failed` và reservation được giải phóng. |
| Hủy khi đang có offer | `2706b6b3-e3ec-4e94-a867-de2ad5b96ac0` | Chuyến `Cancelled`, SignalR đóng đúng offer và event matching gửi lại không giữ xe. |
| Nhận và từ chối đồng thời | `c4229dc5-ebdc-40c9-af41-c975ae6d68b3` | Một yêu cầu trả 200, yêu cầu còn lại trả 409. |
| Cạn 4 lượt matching | `145b5619-3963-4398-84c0-7ba3405884fa` | Worker xử lý đủ bốn offer theo hạn, chuyến `Failed` và toàn bộ reservation được giải phóng. |

### Nâng cấp dữ liệu

TripService bổ sung cột cho TripDB hiện có khi khởi động. Các chuyến Requested cũ có thể lấy và gắn báo giá mới. Reservation của các chuyến Requested đã xác nhận theo phiên bản cũ được giải phóng qua outbox. Khi nâng cấp môi trường demo, hoàn tất các chuyến đã nhận trước khi đổi phiên bản. Chuyến cũ chưa có giá xác minh sẽ nhận `FARE_NOT_CONFIRMED` khi trả khách. Ca kiểm tra nâng cấp `b38beb0e-cbee-43b5-bb0f-e99c98d03cd8` giữ chuyến ở Requested và giải phóng reservation cũ.

## 3. Tích hợp FE và nghiệm thu bản triển khai (§3.1 và §4)

- [ ] Tích hợp hai ứng dụng Rider/Driver với Gateway và Hub. Nếu FE ở repo riêng, ghi commit hoặc phiên bản FE dùng để nghiệm thu. FE dùng API snapshot sau reconnect và hiển thị các trạng thái hủy, thất bại và `PaymentPending`.
- [ ] Triển khai đồng bộ Gateway, TripService, MatchingService và PaymentService. Ghi commit, URL kiểm tra và ngày triển khai vào báo cáo. Chạy kiểm tra trên SQL Server, MongoDB, PostgreSQL, Kafka và Redis của môi trường demo.
- [ ] Demo chính từ hai giao diện. Rider đăng nhập, chọn tuyến, chốt giá và đặt xe. Driver bật nhận chuyến, nhận offer, đến đón, đón khách và trả khách. Xác nhận cả hai giao diện thấy `Completed`, số dư và bút toán Wallet khớp giá đã chốt.
- [ ] Chạy các nhánh nghiệm thu gồm Rider hủy, Driver từ chối hoặc để hết hạn offer, thanh toán lỗi rồi thử lại, gửi lại cùng yêu cầu thanh toán, hoàn tiền và SignalR kết nối lại sau khi JWT hết hạn. Lưu `tripId`, mã HTTP và trạng thái cuối cho từng ca trong báo cáo. [Hướng dẫn tích hợp FE](FE_INTEGRATION_GUIDE.md) · [Báo cáo kiểm tra API](FE_API_VERIFICATION_2026-11-16.md)

## 4. Hoàn thiện yêu cầu chất lượng (§9)

- [ ] **Saga và bồi thường.** Chọn một tình huống cụ thể cần hoàn tiền sau khi đã ghi nhận thanh toán. Triển khai luồng tự động đảo bút toán, ghi trạng thái kết quả và bảo đảm tín hiệu lỗi gửi lại chỉ dẫn đến một lần bồi thường. API refund hiện có là căn cứ để tái sử dụng nghiệp vụ đảo sổ. [PaymentAppService.cs](../src/PaymentService/Application/Services/PaymentAppService.cs)
- [ ] **Nhất quán và giao nhận trùng.** Kiểm thử thanh toán và refund đồng thời trên PostgreSQL thật. Đối chiếu số dư cuối với ledger. Kiểm tra consumer khi nhận lại message và khi worker outbox khởi động lại. Xác nhận lỗi tạm thời được retry có giới hạn và lỗi sau khi hết lượt vào topic lỗi Kafka theo cấu hình đã công bố.
- [ ] **Xác thực và phân quyền.** Để đáp ứng OAuth 2.0/OpenID Connect trong §9.3, tích hợp nhà cung cấp danh tính thay cho cơ chế cấp JWT demo. Giữ kiểm tra quyền trên tài nguyên tại service. Nghiệm thu login, token hết hạn và truy cập tài nguyên sai quyền.
- [ ] **Quan sát hệ thống.** Cấu hình nơi nhận dữ liệu OpenTelemetry và lưu bằng chứng truy vết một chuyến từ Gateway qua Trip, Kafka, Matching và Payment. Có số liệu cho tỉ lệ đặt xe thành công, thời gian matching, độ trễ API, tỉ lệ lỗi thanh toán và độ trễ consumer group Kafka. [Cấu hình và chỉ số hiện có](security-and-business-metrics.md)

## Phạm vi và mốc bàn giao

**Mốc demo đồ án** là hoàn thành mục 1–3 trên cùng một bản triển khai, có kết quả thực tế của luồng Wallet và các nhánh quan trọng. **Mốc đối chiếu đầy đủ với PDF** gồm thêm bằng chứng nghiệm thu mục 4. Đề bài cho phép luồng thanh toán mẫu. Cổng Momo/VNPay/Stripe thật, đăng ký, quên mật khẩu và lịch sử chuyến có thể để ngoài đợt này nếu không có yêu cầu bổ sung.
