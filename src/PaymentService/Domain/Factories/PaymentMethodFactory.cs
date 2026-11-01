using PaymentService.Domain.Exceptions;

namespace PaymentService.Domain.Factories;

public static class PaymentMethods
{
    public const string Wallet = "Wallet";
    public const string Momo = "Momo";
    public const string VNPay = "VNPay";
    public const string Stripe = "Stripe";

    public static readonly string[] Providers = [Momo, VNPay, Stripe];
    public static readonly string[] All = [Wallet, Momo, VNPay, Stripe];

    /// <summary>Chuan hoa ten phuong thuc ("momo" -> "Momo"); tra null neu khong ho tro.</summary>
    public static string? Normalize(string? method)
        => All.FirstOrDefault(m => string.Equals(m, method, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Factory Pattern (Muc 7 PDF): khoi tao bo xu ly theo phuong thuc thanh toan.
/// </summary>
public class PaymentMethodFactory
{
    public IPaymentMethodProcessor Create(string method)
    {
        return PaymentMethods.Normalize(method) switch
        {
            PaymentMethods.Wallet => new WalletPaymentProcessor(),
            PaymentMethods.Momo => new TokenizedGatewayProcessor(PaymentMethods.Momo),
            PaymentMethods.VNPay => new TokenizedGatewayProcessor(PaymentMethods.VNPay),
            PaymentMethods.Stripe => new TokenizedGatewayProcessor(PaymentMethods.Stripe),
            _ => throw new PaymentException(ErrorCodes.ValidationError, 422,
                $"Phuong thuc thanh toan '{method}' khong duoc ho tro.")
        };
    }
}
