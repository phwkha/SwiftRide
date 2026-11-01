using PaymentService.Application.DTOs;
using PaymentService.Application.Services;
using PaymentService.Domain.Exceptions;
using SwiftRide.Contracts.Common;

namespace PaymentService.Api;

/// <summary>
/// 5 REST endpoints cua Payment Service (#19 - #23 trong api_contract_and_fe_integration.md).
/// X-User-Id do API Gateway bom vao sau khi xac thuc JWT.
/// </summary>
public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        // #19 - SD 12, SD 13: Thanh toan lai thu cong khi PaymentPending
        app.MapPost("/api/payments/process", async (
                ProcessPaymentRequest request,
                HttpContext http,
                PaymentAppService service,
                CancellationToken ct) =>
            {
                var key = http.Request.Headers["X-Idempotency-Key"].ToString();
                var result = await service.ProcessPaymentAsync(request, key, ct);
                return Results.Ok(ApiResponse<PaymentResponse>.Ok(result, "Thanh toan thanh cong", http.TraceIdentifier));
            })
            .WithTags("Payments");

        // #20 - SD 14: Hoan tien & giao dich bu tru Saga
        app.MapPost("/api/payments/{transactionId:guid}/refund", async (
                Guid transactionId,
                RefundPaymentRequest request,
                HttpContext http,
                PaymentAppService service,
                CancellationToken ct) =>
            {
                var result = await service.RefundAsync(transactionId, request.Reason, ct);
                return Results.Ok(ApiResponse<RefundPaymentResponse>.Ok(result, "Hoan tien thanh cong", http.TraceIdentifier));
            })
            .WithTags("Payments");

        // #21 - SD 15: Lien ket nguon tien (chi nhan token, khong nhan so the/CVV)
        app.MapPost("/api/payment-tokens", async (
                CreatePaymentTokenRequest request,
                HttpContext http,
                PaymentAppService service,
                CancellationToken ct) =>
            {
                var result = await service.CreateTokenAsync(GetUserId(http), request, ct);
                return Results.Created($"/api/payment-tokens/{result.Id}",
                    ApiResponse<PaymentTokenDto>.Created(result, "Lien ket nguon tien thanh cong", http.TraceIdentifier));
            })
            .WithTags("Payment Tokens");

        // #22 - SD 15: Danh sach nguon tien da lien ket
        app.MapGet("/api/payment-tokens", async (
                HttpContext http,
                PaymentAppService service,
                CancellationToken ct) =>
            {
                var result = await service.GetTokensAsync(GetUserId(http), ct);
                return Results.Ok(ApiResponse<List<PaymentTokenDto>>.Ok(result, traceId: http.TraceIdentifier));
            })
            .WithTags("Payment Tokens");

        // #23 - Tra cuu so du vi
        app.MapGet("/api/accounts/{ownerId:guid}", async (
                Guid ownerId,
                HttpContext http,
                PaymentAppService service,
                CancellationToken ct) =>
            {
                var result = await service.GetAccountAsync(ownerId, ct);
                return Results.Ok(ApiResponse<AccountDto>.Ok(result, traceId: http.TraceIdentifier));
            })
            .WithTags("Accounts");

        return app;
    }

    private static Guid GetUserId(HttpContext http)
    {
        var raw = http.Request.Headers["X-User-Id"].ToString();
        return Guid.TryParse(raw, out var userId)
            ? userId
            : throw new PaymentException(ErrorCodes.Unauthorized, 401, "Thieu hoac sai header X-User-Id.");
    }
}
