using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentService.Application.Consumers;
using PaymentService.Application.DTOs;
using PaymentService.Application.Services;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Domain.Exceptions;
using PaymentService.Domain.Factories;
using PaymentService.Infrastructure.Data;
using SwiftRide.Contracts.Events;
using static PaymentService.Infrastructure.Data.PaymentDbSeeder;

namespace PaymentService.Tests;

public class PaymentAppServiceTests
{
    private static async Task<PaymentDbContext> CreateSeededDbAsync()
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new PaymentDbContext(options);
        await SeedAsync(db);
        return db;
    }

    private static PaymentAppService CreateService(PaymentDbContext db)
        => new(db, new PaymentMethodFactory(), NullLogger<PaymentAppService>.Instance);

    private static ProcessPaymentRequest WalletRequest(Guid tripId, decimal amount = 65000m)
        => new(tripId, Rider1Id, Driver1Id, amount, "Wallet");

    private static async Task<decimal> BalanceAsync(PaymentDbContext db, Guid ownerId, AccountType type)
        => (await db.Accounts.AsNoTracking().SingleAsync(a => a.OwnerId == ownerId && a.AccountType == type)).Balance;

    [Fact]
    public async Task ProcessPayment_Wallet_WritesBalancedLedgerAndSplits80_20()
    {
        await using var db = await CreateSeededDbAsync();
        var service = CreateService(db);

        var result = await service.ProcessPaymentAsync(WalletRequest(Guid.NewGuid()), "key-1");

        Assert.Equal("Success", result.Status);
        Assert.Equal(52000m, result.DriverCreditAmount);
        Assert.Equal(13000m, result.PlatformFeeAmount);
        Assert.Equal(435000m, await BalanceAsync(db, Rider1Id, AccountType.WalletLiability));
        Assert.Equal(152000m, await BalanceAsync(db, Driver1Id, AccountType.WalletLiability));
        Assert.Equal(13000m, await BalanceAsync(db, PlatformId, AccountType.PlatformRevenue));
        Assert.Equal(3, await db.LedgerEntries.CountAsync());
        Assert.Single(await db.OutboxMessages.Where(m => m.EventType == nameof(PaymentSettledEvent)).ToListAsync());
    }

    [Fact]
    public async Task ProcessPayment_SameIdempotencyKeyTwice_ChargesOnlyOnce()
    {
        await using var db = await CreateSeededDbAsync();
        var service = CreateService(db);
        var request = WalletRequest(Guid.NewGuid());

        var first = await service.ProcessPaymentAsync(request, "key-dup");
        var second = await service.ProcessPaymentAsync(request, "key-dup");

        Assert.Equal(first.TransactionId, second.TransactionId);
        Assert.Equal(1, await db.Transactions.CountAsync());
        Assert.Equal(435000m, await BalanceAsync(db, Rider1Id, AccountType.WalletLiability));
    }

    [Fact]
    public async Task ProcessPayment_DifferentKeySameTrip_ThrowsPaymentDuplicate()
    {
        await using var db = await CreateSeededDbAsync();
        var service = CreateService(db);
        var tripId = Guid.NewGuid();

        await service.ProcessPaymentAsync(WalletRequest(tripId), "key-a");
        var ex = await Assert.ThrowsAsync<PaymentException>(
            () => service.ProcessPaymentAsync(WalletRequest(tripId), "key-b"));

        Assert.Equal(ErrorCodes.PaymentDuplicate, ex.Code);
        Assert.Equal(435000m, await BalanceAsync(db, Rider1Id, AccountType.WalletLiability));
    }

    [Fact]
    public async Task ProcessPayment_InsufficientBalance_Throws422AndKeepsBalances()
    {
        await using var db = await CreateSeededDbAsync();
        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<PaymentException>(
            () => service.ProcessPaymentAsync(WalletRequest(Guid.NewGuid(), 10_000_000m), "key-big"));

        Assert.Equal(ErrorCodes.InsufficientBalance, ex.Code);
        Assert.Equal(500000m, await BalanceAsync(db, Rider1Id, AccountType.WalletLiability));
        Assert.Equal(0, await db.LedgerEntries.CountAsync());
        Assert.Equal(IdempotencyStatus.Failed, (await db.IdempotencyRecords.SingleAsync()).Status);
    }

    [Fact]
    public async Task ProcessPayment_Momo_DebitsEscrowNotRiderWallet()
    {
        await using var db = await CreateSeededDbAsync();
        var service = CreateService(db);

        await service.ProcessPaymentAsync(
            new ProcessPaymentRequest(Guid.NewGuid(), Rider1Id, Driver1Id, 80000m, "Momo", Rider1MomoTokenId), "key-momo");

        Assert.Equal(500000m, await BalanceAsync(db, Rider1Id, AccountType.WalletLiability));
        Assert.Equal(9920000m, await BalanceAsync(db, PlatformId, AccountType.EscrowHolding));
        Assert.Equal(164000m, await BalanceAsync(db, Driver1Id, AccountType.WalletLiability));
    }

    [Fact]
    public async Task Refund_WritesReverseEntriesAndRestoresBalances()
    {
        await using var db = await CreateSeededDbAsync();
        var service = CreateService(db);
        var payment = await service.ProcessPaymentAsync(WalletRequest(Guid.NewGuid()), "key-refund");

        var refund = await service.RefundAsync(payment.TransactionId, "Saga bu tru");

        Assert.Equal("Refunded", refund.Status);
        Assert.Equal(65000m, refund.RefundAmount);
        Assert.Equal(500000m, await BalanceAsync(db, Rider1Id, AccountType.WalletLiability));
        Assert.Equal(100000m, await BalanceAsync(db, Driver1Id, AccountType.WalletLiability));
        Assert.Equal(0m, await BalanceAsync(db, PlatformId, AccountType.PlatformRevenue));

        var entries = await db.LedgerEntries.ToListAsync();
        Assert.Equal(6, entries.Count);
        Assert.Equal(
            entries.Where(e => e.EntryType == EntryType.Debit).Sum(e => e.Amount),
            entries.Where(e => e.EntryType == EntryType.Credit).Sum(e => e.Amount));

        await Assert.ThrowsAsync<PaymentException>(() => service.RefundAsync(payment.TransactionId, "lan 2"));
    }

    [Fact]
    public async Task CreateToken_StoresMaskedTokenAndKeepsSingleDefault()
    {
        await using var db = await CreateSeededDbAsync();
        var service = CreateService(db);

        var dto = await service.CreateTokenAsync(Rider1Id, new CreatePaymentTokenRequest("vnpay", "tok_vnpay_a1b2c3d4", true));

        Assert.Equal("VNPay", dto.Provider);
        Assert.Equal("**** c3d4", dto.MaskedToken);
        Assert.Single(await db.PaymentTokens.Where(t => t.UserId == Rider1Id && t.IsDefault).ToListAsync());
    }

    [Fact]
    public async Task Consumer_GatewayKeepsFailing_MarksPaymentPendingAfter3Retries()
    {
        await using var db = await CreateSeededDbAsync();
        await CreateService(db).CreateTokenAsync(Rider1Id, new CreatePaymentTokenRequest("Stripe", "tok_fail_timeout", true));
        TripDroppedOffConsumer.BaseRetryDelay = TimeSpan.Zero;

        var tripId = Guid.NewGuid();
        var consumer = new TripDroppedOffConsumer(CreateService(db), NullLogger<TripDroppedOffConsumer>.Instance);
        var context = FakeConsumeContext.Create(
            new TripDroppedOffEvent(tripId, Rider1Id, Driver1Id, 65000m, "Stripe", $"pay_trip_{tripId}"));

        await consumer.Consume(context);

        var tx = await db.Transactions.AsNoTracking().SingleAsync();
        Assert.Equal(TransactionStatus.PaymentPending, tx.Status);
        Assert.Contains("3 lan retry", tx.ErrorDetail);
        Assert.Equal(IdempotencyStatus.Failed, (await db.IdempotencyRecords.AsNoTracking().SingleAsync()).Status);
        Assert.Single(await db.OutboxMessages.Where(m => m.EventType == nameof(PaymentFailedEvent)).ToListAsync());
        Assert.Equal(0, await db.LedgerEntries.CountAsync());
    }

    /// <summary>ConsumeContext toi thieu: consumer chi doc Message va CancellationToken.</summary>
    public class FakeConsumeContext : System.Reflection.DispatchProxy
    {
        public static ConsumeContext<TripDroppedOffEvent> Create(TripDroppedOffEvent message)
        {
            var proxy = System.Reflection.DispatchProxy.Create<ConsumeContext<TripDroppedOffEvent>, FakeConsumeContext>();
            ((FakeConsumeContext)(object)proxy)._message = message;
            return proxy;
        }

        private TripDroppedOffEvent? _message;

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name switch
            {
                "get_Message" => _message,
                "get_CancellationToken" => CancellationToken.None,
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
    }
}
