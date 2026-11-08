using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Domain.Interfaces;

public interface IPaymentRepository
{
    Task<IdempotencyRecord?> GetIdempotencyRecordAsync(string key, CancellationToken ct = default);
    void AddIdempotencyRecord(IdempotencyRecord record);
    Task<bool> IsTripAlreadyPaidAsync(Guid tripId, CancellationToken ct = default);
    Task<Transaction?> GetPendingRideTransactionAsync(Guid tripId, CancellationToken ct = default);
    Task<Transaction?> GetTransactionWithEntriesAsync(Guid transactionId, CancellationToken ct = default);
    Task<Account?> GetAccountAsync(Guid ownerId, AccountType type, CancellationToken ct = default);
    Task<Account?> GetPrimaryAccountByOwnerAsync(Guid ownerId, CancellationToken ct = default);
    Task<Dictionary<Guid, Account>> GetAccountsByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<PaymentToken?> ResolveTokenAsync(Guid riderId, string provider, Guid? tokenId, CancellationToken ct = default);
    Task<List<PaymentToken>> GetUserTokensAsync(Guid userId, bool asNoTracking = false, CancellationToken ct = default);
    void AddPaymentToken(PaymentToken token);
    void AddTransaction(Transaction transaction);
    void AddLedgerEntries(IEnumerable<LedgerEntry> entries);
    void AddOutboxMessage(OutboxMessage message);
    void ClearTracking();
    Task SaveChangesAsync(CancellationToken ct = default);
}
