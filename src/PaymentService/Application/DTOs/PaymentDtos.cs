namespace PaymentService.Application.DTOs;

// Khop 100% muc 5 "THANH TOAN & SO CAI" trong docs/api_contract_and_fe_integration.md

public record ProcessPaymentRequest(
    Guid TripId,
    Guid RiderId,
    Guid DriverId,
    decimal Amount,
    string PaymentMethod,
    Guid? PaymentTokenId = null);

public record PaymentResponse(
    Guid TransactionId,
    Guid TripId,
    string Status,
    string PaymentMethod,
    decimal TotalAmount,
    decimal RiderDebitAmount,
    decimal DriverCreditAmount,
    decimal PlatformFeeAmount,
    string Description,
    DateTimeOffset CreatedAt);

public record RefundPaymentRequest(string Reason);

public record RefundPaymentResponse(
    Guid TransactionId,
    decimal RefundAmount,
    string Status,
    DateTimeOffset RefundedAt);

public record CreatePaymentTokenRequest(string Provider, string Token, bool? IsDefault = null);

public record PaymentTokenDto(
    Guid Id,
    string Provider,
    string MaskedToken,
    bool IsDefault,
    DateTimeOffset CreatedAt);

public record AccountDto(
    Guid Id,
    Guid OwnerId,
    string OwnerType,
    string AccountType,
    string Currency,
    decimal Balance,
    DateTimeOffset? UpdatedAt);
