using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentService.Application.DTOs;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Domain.Exceptions;
using PaymentService.Domain.Factories;
using PaymentService.Domain.Services;
using PaymentService.Infrastructure.Data;
using SwiftRide.Contracts.Events;

namespace PaymentService.Application.Services;

/// <summary>
/// Nghiep vu Payment: so cai kep (SD 12), idempotency (SD 13), refund Saga (SD 14), token hoa (SD 15).
/// Moi thay doi so du duoc ghi cung 1 SaveChanges voi ledger entries va outbox message.
/// </summary>
public class PaymentAppService
{
    public static readonly Guid PlatformOwnerId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private const decimal DriverShareRate = 0.80m;

    private readonly PaymentDbContext _db;
    private readonly PaymentMethodFactory _factory;
    private readonly ILogger<PaymentAppService> _logger;

    public PaymentAppService(PaymentDbContext db, PaymentMethodFactory factory, ILogger<PaymentAppService> logger)
    {
        _db = db;
        _factory = factory;
        _logger = logger;
    }

    // ==================== SD 12, SD 13: Quyet toan so cai kep ====================

    public async Task<PaymentResponse> ProcessPaymentAsync(
        ProcessPaymentRequest request, string idempotencyKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new PaymentException(ErrorCodes.ValidationError, 400, "Thieu header X-Idempotency-Key.");
        if (request.Amount <= 0)
            throw new PaymentException(ErrorCodes.ValidationError, 422, "So tien thanh toan phai lon hon 0.");

        var method = PaymentMethods.Normalize(request.PaymentMethod)
            ?? throw new PaymentException(ErrorCodes.ValidationError, 422,
                $"Phuong thuc thanh toan '{request.PaymentMethod}' khong duoc ho tro.");

        // Buoc 1: Idempotency check
        var record = await _db.IdempotencyRecords.FindAsync([idempotencyKey], ct);
        if (record is not null)
        {
            if (record.Status == IdempotencyStatus.Success && record.ResponsePayload is not null)
                return JsonSerializer.Deserialize<PaymentResponse>(record.ResponsePayload)!;

            if (record.Status == IdempotencyStatus.Processing)
                throw new PaymentException(ErrorCodes.PaymentDuplicate, 409,
                    "Giao dich voi khoa idempotency nay dang duoc xu ly.");

            // Failed: cho phep thu lai voi cung khoa (retry SD 13)
            record.Status = IdempotencyStatus.Processing;
            record.RequestHash = ComputeHash(request);
            record.ExpiresAt = DateTimeOffset.UtcNow.AddHours(24);
        }
        else
        {
            record = new IdempotencyRecord { IdempotencyKey = idempotencyKey, RequestHash = ComputeHash(request) };
            _db.IdempotencyRecords.Add(record);
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Request khac cung khoa vua INSERT truoc
            _db.ChangeTracker.Clear();
            throw new PaymentException(ErrorCodes.PaymentDuplicate, 409,
                "Giao dich voi khoa idempotency nay dang duoc xu ly.");
        }

        try
        {
            return await SettleAsync(request, method, record, ct);
        }
        catch
        {
            // Giai phong khoa de lan thu lai (cung khoa) duoc xu ly tiep
            _db.ChangeTracker.Clear();
            var failed = await _db.IdempotencyRecords.FindAsync([idempotencyKey], ct);
            if (failed is not null && failed.Status != IdempotencyStatus.Success)
            {
                failed.Status = IdempotencyStatus.Failed;
                await _db.SaveChangesAsync(ct);
            }
            throw;
        }
    }

    private async Task<PaymentResponse> SettleAsync(
        ProcessPaymentRequest request, string method, IdempotencyRecord record, CancellationToken ct)
    {
        var tripAlreadyPaid = await _db.Transactions.AnyAsync(t =>
            t.TripId == request.TripId
            && t.TransactionType == TransactionType.RidePayment
            && t.Status == TransactionStatus.Success, ct);
        if (tripAlreadyPaid)
            throw new PaymentException(ErrorCodes.PaymentDuplicate, 409,
                "Giao dich cua chuyen di nay da duoc xu ly, khong the tru tien lap.");

        var amount = request.Amount;
        var riderWallet = await GetRequiredAccountAsync(request.RiderId, AccountType.WalletLiability, ct);
        var driverWallet = await GetRequiredAccountAsync(request.DriverId, AccountType.WalletLiability, ct);
        var platformRevenue = await GetRequiredAccountAsync(PlatformOwnerId, AccountType.PlatformRevenue, ct);

        // Buoc 2: Xac dinh tai khoan ghi No
        Account debitAccount;
        string? token = null;
        if (method == PaymentMethods.Wallet)
        {
            if (riderWallet.Balance < amount)
                throw new PaymentException(ErrorCodes.InsufficientBalance, 422,
                    "So du trong vi khong du de thanh toan. Vui long nap them tien.");
            debitAccount = riderWallet;
        }
        else
        {
            token = (await ResolveTokenAsync(request.RiderId, method, request.PaymentTokenId, ct)).Token;
            debitAccount = await GetRequiredAccountAsync(PlatformOwnerId, AccountType.EscrowHolding, ct);
        }

        var (success, externalRef, error) = await _factory.Create(method)
            .ChargeAsync(request.RiderId, amount, token, record.IdempotencyKey);
        if (!success)
            throw new PaymentGatewayException(error ?? $"{method}: loi cong thanh toan.");

        // Buoc 3: 3 dong but toan so cai kep bat bien
        var driverShare = Math.Round(amount * DriverShareRate, 2);
        var platformShare = amount - driverShare; // 20%

        var tx = new Transaction
        {
            TripId = request.TripId,
            TransactionType = TransactionType.RidePayment,
            TotalAmount = amount,
            PaymentMethod = method,
            Status = TransactionStatus.Success,
            Description = externalRef is null
                ? $"Thanh toan chuyen di {request.TripId} qua {method}"
                : $"Thanh toan chuyen di {request.TripId} qua {method} (ref: {externalRef})"
        };

        var entries = new List<LedgerEntry>
        {
            new() { TransactionId = tx.Id, AccountId = debitAccount.Id, EntryType = EntryType.Debit, Amount = amount },
            new() { TransactionId = tx.Id, AccountId = driverWallet.Id, EntryType = EntryType.Credit, Amount = driverShare },
            new() { TransactionId = tx.Id, AccountId = platformRevenue.Id, EntryType = EntryType.Credit, Amount = platformShare }
        };
        LedgerValidator.EnsureBalanced(entries);

        // Buoc 4: Cap nhat so du + idempotency + outbox trong cung 1 SaveChanges
        ApplyEntry(debitAccount, entries[0]);
        ApplyEntry(driverWallet, entries[1]);
        ApplyEntry(platformRevenue, entries[2]);

        _db.Transactions.Add(tx);
        _db.LedgerEntries.AddRange(entries);

        var response = new PaymentResponse(
            tx.Id, request.TripId, tx.Status.ToString(), method, amount,
            amount, driverShare, platformShare, tx.Description, tx.CreatedAt);

        record.Status = IdempotencyStatus.Success;
        record.ResponsePayload = JsonSerializer.Serialize(response);

        AddOutbox(new PaymentSettledEvent(request.TripId, tx.Id, amount, method, driverShare));

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                               { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // ux_transactions_trip_success chan lop cuoi cung
            throw new PaymentException(ErrorCodes.PaymentDuplicate, 409,
                "Giao dich cua chuyen di nay da duoc xu ly, khong the tru tien lap.");
        }

        _logger.LogInformation(
            "Settled trip {TripId}: tx {TransactionId}, {Amount} via {Method}.", request.TripId, tx.Id, amount, method);
        return response;
    }

    /// <summary>
    /// SD 13: het retry van loi -> ghi Transaction PaymentPending, khoa idempotency Failed,
    /// va PaymentFailedEvent vao outbox (cung 1 SaveChanges).
    /// </summary>
    public async Task MarkPaymentPendingAsync(TripDroppedOffEvent message, string reason, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();

        var pending = await _db.Transactions.FirstOrDefaultAsync(t =>
            t.TripId == message.TripId
            && t.TransactionType == TransactionType.RidePayment
            && t.Status == TransactionStatus.PaymentPending, ct);

        if (pending is null)
        {
            _db.Transactions.Add(new Transaction
            {
                TripId = message.TripId,
                TransactionType = TransactionType.RidePayment,
                TotalAmount = message.ActualFare,
                PaymentMethod = PaymentMethods.Normalize(message.PaymentMethod) ?? message.PaymentMethod,
                Status = TransactionStatus.PaymentPending,
                Description = $"Thanh toan chuyen di {message.TripId} dang cho xu ly",
                ErrorDetail = reason
            });
        }
        else
        {
            pending.ErrorDetail = reason;
        }

        var record = await _db.IdempotencyRecords.FindAsync([message.IdempotencyKey], ct);
        if (record is null)
            _db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                IdempotencyKey = message.IdempotencyKey,
                RequestHash = string.Empty,
                Status = IdempotencyStatus.Failed
            });
        else
            record.Status = IdempotencyStatus.Failed;

        AddOutbox(new PaymentFailedEvent(message.TripId, nameof(TransactionStatus.PaymentPending), reason));

        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("Trip {TripId} payment pending: {Reason}", message.TripId, reason);
    }

    // ==================== SD 14: Hoan tien Saga ====================

    public async Task<RefundPaymentResponse> RefundAsync(Guid transactionId, string reason, CancellationToken ct = default)
    {
        var original = await _db.Transactions
            .Include(t => t.LedgerEntries)
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct)
            ?? throw new PaymentException(ErrorCodes.NotFound, 404, "Khong tim thay giao dich.");

        if (original.TransactionType != TransactionType.RidePayment || original.Status != TransactionStatus.Success)
            throw new PaymentException(ErrorCodes.PaymentDuplicate, 409,
                "Chi hoan tien duoc giao dich thanh toan chuyen di dang o trang thai Success.");

        var refundTx = new Transaction
        {
            TripId = original.TripId,
            TransactionType = TransactionType.Refund,
            TotalAmount = original.TotalAmount,
            PaymentMethod = original.PaymentMethod,
            Status = TransactionStatus.Success,
            Description = string.IsNullOrWhiteSpace(reason) ? $"Hoan tien giao dich {original.Id}" : reason
        };

        // But toan dao: cung tai khoan, cung so tien, doi chieu Debit <-> Credit (khong sua dong cu)
        var entries = original.LedgerEntries
            .Select(e => new LedgerEntry
            {
                TransactionId = refundTx.Id,
                AccountId = e.AccountId,
                EntryType = e.EntryType == EntryType.Debit ? EntryType.Credit : EntryType.Debit,
                Amount = e.Amount
            })
            .ToList();
        LedgerValidator.EnsureBalanced(entries);

        var accountIds = entries.Select(e => e.AccountId).Distinct().ToList();
        var accounts = await _db.Accounts.Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        foreach (var entry in entries)
            ApplyEntry(accounts[entry.AccountId], entry);

        original.Status = TransactionStatus.Refunded;
        _db.Transactions.Add(refundTx);
        _db.LedgerEntries.AddRange(entries);

        AddOutbox(new PaymentRefundedEvent(
            original.Id, original.TripId ?? Guid.Empty, refundTx.Id, original.TotalAmount, refundTx.Description));

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Refunded tx {TransactionId} with compensating tx {RefundId}.", original.Id, refundTx.Id);
        return new RefundPaymentResponse(original.Id, original.TotalAmount,
            nameof(TransactionStatus.Refunded), refundTx.CreatedAt);
    }

    // ==================== SD 15: Token hoa PCI-DSS ====================

    public async Task<PaymentTokenDto> CreateTokenAsync(
        Guid userId, CreatePaymentTokenRequest request, CancellationToken ct = default)
    {
        var provider = PaymentMethods.Providers.FirstOrDefault(p =>
            string.Equals(p, request.Provider, StringComparison.OrdinalIgnoreCase))
            ?? throw new PaymentException(ErrorCodes.ValidationError, 422,
                "Provider chi nhan Momo, VNPay hoac Stripe.");

        var token = request.Token?.Trim() ?? string.Empty;
        if (token.Length is < 4 or > 255)
            throw new PaymentException(ErrorCodes.ValidationError, 422, "Token khong hop le.");

        var existing = await _db.PaymentTokens.Where(t => t.UserId == userId).ToListAsync(ct);
        var isDefault = request.IsDefault ?? existing.Count == 0;
        if (isDefault)
            existing.ForEach(t => t.IsDefault = false);

        var entity = new PaymentToken
        {
            UserId = userId,
            Provider = provider,
            Token = token,
            MaskedToken = "**** " + token[^4..],
            IsDefault = isDefault
        };
        _db.PaymentTokens.Add(entity);
        await _db.SaveChangesAsync(ct);

        return ToDto(entity);
    }

    public async Task<List<PaymentTokenDto>> GetTokensAsync(Guid userId, CancellationToken ct = default)
    {
        var tokens = await _db.PaymentTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.IsDefault)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
        return tokens.Select(ToDto).ToList();
    }

    // ==================== Tra cuu so du ====================

    public async Task<AccountDto> GetAccountAsync(Guid ownerId, CancellationToken ct = default)
    {
        // WalletLiability cho Rider/Driver; PlatformRevenue cho tai khoan San
        var account = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.OwnerId == ownerId && a.AccountType != AccountType.EscrowHolding)
            .OrderBy(a => a.AccountType)
            .FirstOrDefaultAsync(ct)
            ?? throw new PaymentException(ErrorCodes.NotFound, 404, "Khong tim thay tai khoan.");

        return new AccountDto(account.Id, account.OwnerId, account.OwnerType, account.AccountType.ToString(),
            account.Currency, account.Balance, account.UpdatedAt);
    }

    // ==================== Helpers ====================

    private async Task<Account> GetRequiredAccountAsync(Guid ownerId, AccountType type, CancellationToken ct)
        => await _db.Accounts.FirstOrDefaultAsync(a => a.OwnerId == ownerId && a.AccountType == type, ct)
           ?? throw new PaymentException(ErrorCodes.NotFound, 404,
               $"Khong tim thay tai khoan {type} cua {ownerId}.");

    private async Task<PaymentToken> ResolveTokenAsync(Guid riderId, string provider, Guid? tokenId, CancellationToken ct)
    {
        var query = _db.PaymentTokens.Where(t => t.UserId == riderId && t.Provider == provider);
        var token = tokenId is not null
            ? await query.FirstOrDefaultAsync(t => t.Id == tokenId, ct)
            : await query.OrderByDescending(t => t.IsDefault).ThenByDescending(t => t.CreatedAt).FirstOrDefaultAsync(ct);

        return token ?? throw new PaymentException(ErrorCodes.ValidationError, 422,
            $"Khach hang chua lien ket nguon tien {provider}.");
    }

    /// <summary>Quy uoc chung: Debit tru so du, Credit cong so du (SD 12, SD 14).</summary>
    private static void ApplyEntry(Account account, LedgerEntry entry)
    {
        account.Balance += entry.EntryType == EntryType.Credit ? entry.Amount : -entry.Amount;
        account.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void AddOutbox<TEvent>(TEvent @event) where TEvent : class
    {
        _db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = typeof(TEvent).Name,
            Payload = JsonSerializer.Serialize(@event)
        });
    }

    private static string ComputeHash(ProcessPaymentRequest request)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));

    private static PaymentTokenDto ToDto(PaymentToken t)
        => new(t.Id, t.Provider, t.MaskedToken, t.IsDefault, t.CreatedAt);
}
