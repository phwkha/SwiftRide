using PaymentService.Domain.Enums;

namespace PaymentService.Domain.Entities;

public class LedgerEntry
{
    public long Id { get; set; }
    public Guid TransactionId { get; set; }
    public Guid AccountId { get; set; }
    public EntryType EntryType { get; set; } = EntryType.Debit;
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Transaction? Transaction { get; set; }
    public Account? Account { get; set; }
}
