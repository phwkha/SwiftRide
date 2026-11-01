using Microsoft.AspNetCore.Diagnostics;
using PaymentService.Domain.Exceptions;
using SwiftRide.Contracts.Common;

namespace PaymentService.Api;

/// <summary>
/// Map loi nghiep vu sang ApiResponse.Fail voi ma loi chuan (muc 10 api_contract).
/// </summary>
public class PaymentExceptionHandler : IExceptionHandler
{
    private readonly ILogger<PaymentExceptionHandler> _logger;

    public PaymentExceptionHandler(ILogger<PaymentExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (statusCode, code, message) = exception switch
        {
            PaymentException ex => (ex.StatusCode, ex.Code, ex.Message),
            BadHttpRequestException => (400, ErrorCodes.ValidationError, "Du lieu khong hop le."),
            // LedgerValidator nem InvalidOperationException khi Tong Debit != Tong Credit
            InvalidOperationException ex when ex.Message.StartsWith("Ledger entries khong can bang")
                => (500, ErrorCodes.LedgerUnbalanced,
                    "Loi ke toan he thong: But toan khong can bang. Vui long lien he CSKH."),
            _ => (500, "INTERNAL_ERROR", "Loi he thong, vui long thu lai sau.")
        };

        if (statusCode >= 500)
            _logger.LogError(exception, "Unhandled error {Code}.", code);

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(
            ApiResponse<object>.Fail(statusCode, code, message, traceId: httpContext.TraceIdentifier), ct);
        return true;
    }
}
