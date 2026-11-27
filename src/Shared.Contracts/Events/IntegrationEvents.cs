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
    List<Guid> ExcludedDriverIds,
    DateTimeOffset Deadline = default);

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
    List<Guid> ExcludedDriverIds,
    DateTimeOffset Deadline = default);

// MatchingService proposes a reserved driver. TripService commits the offer before notifying the driver.
public record TripOfferProposedEvent(Guid TripId, Guid DriverId, int Attempt);
public record MatchingAttemptClosedEvent(Guid TripId, int Attempt);

// 3. TripService -> TripHub (mời tài xế nhận cuốc trong 15s ở SD 11)
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
    DateTimeOffset ExpiresAt,
    Guid OfferId = default);

// 4. TripService -> TripHub (hết 15s tài xế không phản hồi ở SD 11)
public record TripOfferExpiredEvent(Guid TripId, Guid DriverId, string Message, Guid OfferId = default);

// 5. TripService -> MatchingService (tài xế bấm từ chối để mở lại is_available = true)
public record DriverRejectedOfferEvent(Guid TripId, Guid DriverId, string? Reason);

// 6. MatchingService -> TripService (không tìm thấy tài xế quanh khu vực ở SD 11)
public record MatchingFailedEvent(Guid TripId, string Reason, int Attempt = 0);

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

// TripService -> TripHub only after the state change is committed.
public record TripStatusChangedEvent(
    Guid TripId, string NewStatus, Guid? DriverId, DateTimeOffset Timestamp, string? CancellationReason = null);

// 13. PaymentService -> All (hoàn tiền thành công ở SD 14)
public record PaymentRefundedEvent(
    Guid TransactionId,
    Guid TripId,
    Guid RefundTransactionId,
    decimal RefundAmount,
    string Reason);
