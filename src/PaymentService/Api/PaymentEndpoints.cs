using PaymentService.Application.DTOs;
using PaymentService.Application.Services;
using PaymentService.Domain.Exceptions;
using SwiftRide.Contracts.Common;

namespace PaymentService.Api;

/// <summary>
/// 5 REST endpoints cua Payment Service (#19 - #23 trong api_contract_and_fe_integration.md).
/// Identity comes from JWT validated locally; external identity headers are ignored.
/// </summary>
public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        // #19 - SD 12, SD 13: Thanh toan lai thu cong khi PaymentPending
        api.MapPost("/payments/process", async (
                ProcessPaymentRequest request,
                HttpContext http,
                PaymentAppService service,
                PaymentRequestAuthorization access,
                CancellationToken ct) =>
            {
                var key = http.Request.Headers["X-Idempotency-Key"].ToString();
                var verified = await access.AuthorizeProcessAsync(http.User, http.Request.Headers.Authorization.ToString(), request, ct);
                var result = await service.ProcessPaymentAsync(verified, key, ct);
                return Results.Ok(ApiResponse<PaymentResponse>.Ok(result, "Thanh toan thanh cong", http.TraceIdentifier));
            })
            .WithTags("Payments");

        // #20 - SD 14: Hoan tien & giao dich bu tru Saga
        api.MapPost("/payments/{transactionId:guid}/refund", async (
                Guid transactionId,
                RefundPaymentRequest request,
                HttpContext http,
                PaymentAppService service,
                PaymentRequestAuthorization access,
                CancellationToken ct) =>
            {
                await access.AuthorizeRefundAsync(http.User, http.Request.Headers.Authorization.ToString(), transactionId, ct);
                var result = await service.RefundAsync(transactionId, request.Reason, ct);
                return Results.Ok(ApiResponse<RefundPaymentResponse>.Ok(result, "Hoan tien thanh cong", http.TraceIdentifier));
            })
            .WithTags("Payments");

        // #21 - SD 15: Lien ket nguon tien (chi nhan token, khong nhan so the/CVV)
        api.MapPost("/payment-tokens", async (
                CreatePaymentTokenRequest request,
                HttpContext http,
                PaymentAppService service,
                CancellationToken ct) =>
            {
                var result = await service.CreateTokenAsync(PaymentRequestAuthorization.RequireUser(http.User), request, ct);
                return Results.Created($"/api/payment-tokens/{result.Id}",
                    ApiResponse<PaymentTokenDto>.Created(result, "Lien ket nguon tien thanh cong", http.TraceIdentifier));
            })
            .WithTags("Payment Tokens");

        // #22 - SD 15: Danh sach nguon tien da lien ket
        api.MapGet("/payment-tokens", async (
                HttpContext http,
                PaymentAppService service,
                CancellationToken ct) =>
            {
                var result = await service.GetTokensAsync(PaymentRequestAuthorization.RequireUser(http.User), ct);
                return Results.Ok(ApiResponse<List<PaymentTokenDto>>.Ok(result, traceId: http.TraceIdentifier));
            })
            .WithTags("Payment Tokens");

        // #23 - Tra cuu so du vi
        api.MapGet("/accounts/{ownerId:guid}", async (
                Guid ownerId,
                HttpContext http,
                PaymentAppService service,
                CancellationToken ct) =>
            {
                PaymentRequestAuthorization.RequireAccountAccess(http.User, ownerId);
                var result = await service.GetAccountAsync(ownerId, ct);
                return Results.Ok(ApiResponse<AccountDto>.Ok(result, traceId: http.TraceIdentifier));
            })
            .WithTags("Accounts");

        return app;
    }

}
