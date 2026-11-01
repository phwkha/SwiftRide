using MassTransit;
using PaymentService.Application.DTOs;
using PaymentService.Application.Services;
using PaymentService.Domain.Exceptions;
using SwiftRide.Contracts.Events;

namespace PaymentService.Application.Consumers;

/// <summary>
/// Nhan TripDroppedOffEvent va tu dong quyet toan (SD 12).
/// Loi cong tam thoi: retry toi da 3 lan voi CUNG idempotency key (2s, 4s, 8s + jitter);
/// het retry hoac loi nghiep vu (vi khong du tien, chua lien ket nguon tien) -> PaymentPending (SD 13).
/// </summary>
public class TripDroppedOffConsumer : IConsumer<TripDroppedOffEvent>
{
    public const int MaxRetries = 3;

    private readonly PaymentAppService _paymentService;
    private readonly ILogger<TripDroppedOffConsumer> _logger;

    public TripDroppedOffConsumer(PaymentAppService paymentService, ILogger<TripDroppedOffConsumer> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    /// <summary>Do tre giua cac lan retry; test co the gan TimeSpan.Zero.</summary>
    public static TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    public async Task Consume(ConsumeContext<TripDroppedOffEvent> context)
    {
        var msg = context.Message;
        var request = new ProcessPaymentRequest(msg.TripId, msg.RiderId, msg.DriverId, msg.ActualFare, msg.PaymentMethod);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await _paymentService.ProcessPaymentAsync(request, msg.IdempotencyKey, context.CancellationToken);
                return;
            }
            catch (PaymentException ex) when (ex.Code == ErrorCodes.PaymentDuplicate)
            {
                // Message trung lap (at-least-once delivery) hoac chuyen da duoc thanh toan
                _logger.LogInformation("Skip duplicate TripDroppedOffEvent for trip {TripId}: {Reason}", msg.TripId, ex.Message);
                return;
            }
            catch (PaymentGatewayException ex) when (attempt < MaxRetries)
            {
                var delay = BaseRetryDelay * Math.Pow(2, attempt);
                if (delay > TimeSpan.Zero)
                    delay += TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250)); // jitter
                _logger.LogWarning("Payment gateway error for trip {TripId} (attempt {Attempt}/{Max}), retry in {Delay}: {Error}",
                    msg.TripId, attempt + 1, MaxRetries, delay, ex.Message);
                await Task.Delay(delay, context.CancellationToken);
            }
            catch (PaymentGatewayException ex)
            {
                await _paymentService.MarkPaymentPendingAsync(msg,
                    $"{ex.Message} sau {MaxRetries} lan retry", context.CancellationToken);
                return;
            }
            catch (PaymentException ex) when (ex.Code is ErrorCodes.InsufficientBalance or ErrorCodes.ValidationError)
            {
                // Loi nghiep vu khong tu het khi retry -> cho khach thanh toan lai thu cong
                await _paymentService.MarkPaymentPendingAsync(msg, ex.Message, context.CancellationToken);
                return;
            }
        }
    }
}
