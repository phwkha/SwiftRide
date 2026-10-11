using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Domain.Services;

/// <summary>
/// Validation logic cho double-entry ledger.
/// Dam bao tong Debit = tong Credit truoc khi luu transaction.
/// </summary>
public static class LedgerValidator
{
    /// <summary>
    /// Validates that total debits equal total credits for a set of ledger entries.
    /// Must be called before saving a transaction with its entries.
    /// </summary>
    public static void EnsureBalanced(IEnumerable<LedgerEntry> entries)
    {
        var entryList = entries.ToList();

        if (entryList.Count == 0)
            throw new InvalidOperationException("Transaction phai co it nhat mot ledger entry.");

        var totalDebit = entryList
            .Where(e => e.EntryType == EntryType.Debit)
            .Sum(e => e.Amount);

        var totalCredit = entryList
            .Where(e => e.EntryType == EntryType.Credit)
            .Sum(e => e.Amount);

        if (totalDebit != totalCredit)
            throw new InvalidOperationException(
                $"Ledger entries khong can bang. Tong Debit: {totalDebit}, Tong Credit: {totalCredit}");
    }
}
