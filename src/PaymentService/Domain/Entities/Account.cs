using PaymentService.Domain.Enums;

namespace PaymentService.Domain.Entities;

public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public string OwnerType { get; set; } = "Rider"; // Rider, Driver, Platform
    public AccountType AccountType { get; set; } = AccountType.WalletLiability;
    public string Currency { get; set; } = "VND";
    public decimal Balance { get; set; } = 0.00m;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();
}
