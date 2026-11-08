using Microsoft.EntityFrameworkCore;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
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

    public Task<Dictionary<Guid, Account>> GetAccountsByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        => db.Accounts.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);

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
}
