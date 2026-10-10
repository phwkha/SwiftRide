namespace PaymentService.Domain.Enums;

public enum AccountType
{
    WalletLiability = 0,
    PlatformRevenue = 1,
    EscrowHolding = 2
}

public enum EntryType
{
    Debit = 0,
    Credit = 1
}

public enum TransactionType
{
    RidePayment = 0,
    PlatformFee = 1,
    DriverPayout = 2,
    Refund = 3
}

public enum IdempotencyStatus
{
    Processing = 0,
    Success = 1,
    Failed = 2
}
