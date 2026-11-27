using Microsoft.EntityFrameworkCore;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Domain.Exceptions;
using PaymentService.Domain.Interfaces;
using PaymentService.Infrastructure.Data;

namespace PaymentService.Infrastructure.Repositories;

public sealed class PaymentRepository(PaymentDbContext db) : IPaymentRepository
{
    public async Task<IdempotencyRecord?> GetIdempotencyRecordAsync(string key, CancellationToken ct = default)
        => await db.IdempotencyRecords.FindAsync([key], ct);

    public void AddIdempotencyRecord(IdempotencyRecord record)
        => db.IdempotencyRecords.Add(record);

    public Task<bool> IsTripAlreadyPaidAsync(Guid tripId, CancellationToken ct = default)
        => db.Transactions.AnyAsync(t => t.TripId == tripId && t.Status == TransactionStatus.Success, ct);

    public Task<Transaction?> GetPendingRideTransactionAsync(Guid tripId, CancellationToken ct = default)
        => db.Transactions
            .Where(t => t.TripId == tripId && t.TransactionType == TransactionType.RidePayment && t.Status == TransactionStatus.PaymentPending)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task<Transaction?> GetTransactionWithEntriesAsync(Guid transactionId, CancellationToken ct = default)
        => db.Transactions
            .Include(t => t.LedgerEntries)
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct);

    public Task<Account?> GetAccountAsync(Guid ownerId, AccountType type, CancellationToken ct = default)
        => db.Accounts.FirstOrDefaultAsync(a => a.OwnerId == ownerId && a.AccountType == type, ct);

    public Task<Account?> GetPrimaryAccountByOwnerAsync(Guid ownerId, CancellationToken ct = default)
        => db.Accounts
            .AsNoTracking()
            .Where(a => a.OwnerId == ownerId && a.AccountType != AccountType.EscrowHolding)
            .OrderBy(a => a.AccountType)
            .FirstOrDefaultAsync(ct);

    public Task<PaymentToken?> ResolveTokenAsync(Guid riderId, string provider, Guid? tokenId, CancellationToken ct = default)
    {
        var query = db.PaymentTokens.Where(t => t.UserId == riderId && t.Provider == provider);
        if (tokenId.HasValue && tokenId.Value != Guid.Empty)
            query = query.Where(t => t.Id == tokenId.Value);
        else
            query = query.OrderByDescending(t => t.IsDefault).ThenByDescending(t => t.CreatedAt);

        return query.FirstOrDefaultAsync(ct);
    }

    public Task<List<PaymentToken>> GetUserTokensAsync(Guid userId, bool asNoTracking = false, CancellationToken ct = default)
    {
        IQueryable<PaymentToken> query = db.PaymentTokens.Where(t => t.UserId == userId);
        if (asNoTracking) query = query.AsNoTracking();
        return query.OrderByDescending(t => t.IsDefault).ThenByDescending(t => t.CreatedAt).ToListAsync(ct);
    }

    public void AddPaymentToken(PaymentToken token) => db.PaymentTokens.Add(token);
    public void AddTransaction(Transaction transaction) => db.Transactions.Add(transaction);
    public void AddLedgerEntries(IEnumerable<LedgerEntry> entries) => db.LedgerEntries.AddRange(entries);
    public void AddOutboxMessage(OutboxMessage message) => db.OutboxMessages.Add(message);
    public void ClearTracking() => db.ChangeTracker.Clear();
    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task SaveLedgerChangesAsync(
        IReadOnlyCollection<LedgerEntry> entries, Guid? walletToDebit, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        var updatedAt = DateTimeOffset.UtcNow;
        // Use the same account lock order for settlement and refund.
        foreach (var group in entries.GroupBy(entry => entry.AccountId).OrderBy(group => group.Key))
        {
            var accountId = group.Key;
            var debits = group.Where(entry => entry.EntryType == EntryType.Debit).Sum(entry => entry.Amount);
            var credits = group.Where(entry => entry.EntryType == EntryType.Credit).Sum(entry => entry.Amount);
            var delta = credits - debits;
            var checkFunds = accountId == walletToDebit;

            if (transaction is null)
            {
                var account = await db.Accounts.FindAsync([accountId], ct) ?? throw AccountNotFound(accountId);
                if (checkFunds && account.Balance < debits)
                    throw InsufficientFunds();
                account.Balance += delta;
                account.UpdatedAt = updatedAt;
                continue;
            }

            var accounts = db.Accounts.Where(account => account.Id == accountId);
            if (checkFunds)
                accounts = accounts.Where(account => account.Balance >= debits);

            var updated = await accounts.ExecuteUpdateAsync(setters => setters
                .SetProperty(account => account.Balance, account => account.Balance + delta)
                .SetProperty(account => account.UpdatedAt, updatedAt), ct);
            if (updated == 0)
                throw checkFunds ? InsufficientFunds() : AccountNotFound(accountId);
        }

        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
    }

    private static PaymentException InsufficientFunds()
        => new(ErrorCodes.InsufficientBalance, 422,
            "So du trong vi khong du de thanh toan. Vui long nap them tien.");

    private static PaymentException AccountNotFound(Guid accountId)
        => new(ErrorCodes.NotFound, 404, $"Khong tim thay tai khoan {accountId}.");
}
