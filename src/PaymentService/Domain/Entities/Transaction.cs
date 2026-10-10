using PaymentService.Domain.Enums;

namespace PaymentService.Domain.Entities;

public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TripId { get; set; }
    public TransactionType TransactionType { get; set; } = TransactionType.RidePayment;
    public decimal TotalAmount { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();
}
