namespace PaymentService.Domain.Exceptions;

/// <summary>Ma loi nghiep vu chuan hoa (api_contract_and_fe_integration.md muc 10).</summary>
public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string NotFound = "NOT_FOUND";
    public const string InsufficientBalance = "INSUFFICIENT_BALANCE";
    public const string PaymentDuplicate = "PAYMENT_DUPLICATE";
    public const string PaymentGatewayError = "PAYMENT_GATEWAY_ERROR";
    public const string LedgerUnbalanced = "LEDGER_UNBALANCED";
}

/// <summary>Loi nghiep vu Payment, duoc map thanh ApiResponse.Fail(statusCode, code).</summary>
public class PaymentException : Exception
{
    public PaymentException(string code, int statusCode, string message) : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}

/// <summary>Cong thanh toan loi tam thoi (timeout / mat ket noi) - duoc phep retry (SD 13).</summary>
public class PaymentGatewayException : PaymentException
{
    public PaymentGatewayException(string message)
        : base(ErrorCodes.PaymentGatewayError, 502, message)
    {
    }
}
