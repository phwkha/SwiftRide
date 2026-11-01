using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Domain.Services;

namespace PaymentService.Tests;

public class LedgerValidatorTests
{
    [Fact]
    public void EnsureBalanced_DebitEqualsCredit_DoesNotThrow()
    {
        var entries = new List<LedgerEntry>
        {
            new() { EntryType = EntryType.Debit, Amount = 65000m },
            new() { EntryType = EntryType.Credit, Amount = 52000m },
            new() { EntryType = EntryType.Credit, Amount = 13000m }
        };

        LedgerValidator.EnsureBalanced(entries);
    }

    [Fact]
    public void EnsureBalanced_OffByOneDong_Throws()
    {
        var entries = new List<LedgerEntry>
        {
            new() { EntryType = EntryType.Debit, Amount = 65000m },
            new() { EntryType = EntryType.Credit, Amount = 52000m },
            new() { EntryType = EntryType.Credit, Amount = 12999m }
        };

        Assert.Throws<InvalidOperationException>(() => LedgerValidator.EnsureBalanced(entries));
    }
}
