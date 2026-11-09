using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaymentService.Application.Consumers;
using PaymentService.Application.DTOs;
using PaymentService.Application.Services;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;
using PaymentService.Domain.Factories;
using PaymentService.Domain.Interfaces;
using PaymentService.Infrastructure;
using PaymentService.Infrastructure.Data;
using PaymentService.Infrastructure.Repositories;
using StackExchange.Redis;
using SwiftRide.Contracts.Events;
using Xunit;
using static PaymentService.Infrastructure.Data.PaymentDbSeeder;

namespace PaymentService.Tests;

public class RedisIdempotencyGuardTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock = new();
    private readonly Mock<IDatabase> _dbMock = new();

    public RedisIdempotencyGuardTests()
    {
        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessPayment_WhenInitialDatabaseOperationFails_ReleasesLockAndAllowsSameKeyRetry(bool failSave)
    {
        var repo = new Mock<IPaymentRepository>();
        var guard = new Mock<RedisIdempotencyGuard>(_redisMock.Object);
        var locked = false;
        guard.Setup(g => g.AcquireLockAsync("retry-key", It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => { if (locked) return false; locked = true; return true; });
        guard.Setup(g => g.ReleaseLockOnFailureAsync("retry-key"))
            .Returns(() => { locked = false; return Task.CompletedTask; });
        var failure = new TimeoutException("database unavailable");
        if (failSave)
        {
            repo.Setup(r => r.GetIdempotencyRecordAsync("retry-key", It.IsAny<CancellationToken>()))
                .ReturnsAsync((IdempotencyRecord?)null);
            repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        }
        else
        {
            repo.Setup(r => r.GetIdempotencyRecordAsync("retry-key", It.IsAny<CancellationToken>()))
                .ThrowsAsync(failure);
        }
        var service = new PaymentAppService(repo.Object, new PaymentMethodFactory(),
            NullLogger<PaymentAppService>.Instance, guard.Object);
        var request = new ProcessPaymentRequest(Guid.NewGuid(), Rider1Id, Driver1Id, 50000m, "Wallet");

        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Same(failure, await Assert.ThrowsAsync<TimeoutException>(
                () => service.ProcessPaymentAsync(request, "retry-key")));

        guard.Verify(g => g.ReleaseLockOnFailureAsync("retry-key"), Times.Exactly(2));
        Assert.False(locked);
    }

    [Fact]
    public async Task AcquireLockAsync_WhenKeyNotExists_ReturnsTrue()
    {
        var key = "test-key-1";
        var ttl = TimeSpan.FromHours(24);

        _dbMock
            .Setup(db => db.StringSetAsync(
                It.Is<RedisKey>(k => k == $"payment:idemp:{key}"),
                It.Is<RedisValue>(v => v == "PROCESSING"),
                ttl,
                When.NotExists))
            .ReturnsAsync(true);

        var guard = new RedisIdempotencyGuard(_redisMock.Object);

        var acquired = await guard.AcquireLockAsync(key, ttl);

        Assert.True(acquired);
        _dbMock.Verify(db => db.StringSetAsync(
            It.Is<RedisKey>(k => k == $"payment:idemp:{key}"),
            It.Is<RedisValue>(v => v == "PROCESSING"),
            ttl,
            When.NotExists), Times.Once);
    }

    [Fact]
    public async Task ReleaseLockOnFailureAsync_DeletesKey()
    {
        var key = "test-key-fail";
        _dbMock
            .Setup(db => db.KeyDeleteAsync($"payment:idemp:{key}", CommandFlags.None))
            .ReturnsAsync(true);

        var guard = new RedisIdempotencyGuard(_redisMock.Object);

        await guard.ReleaseLockOnFailureAsync(key);

        _dbMock.Verify(db => db.KeyDeleteAsync($"payment:idemp:{key}", CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task TripDroppedOffConsumer_WhenRedisLockFails_SkipsDuplicateEventImmediately()
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new PaymentDbContext(options);
        await SeedAsync(db);

        var paymentService = new PaymentAppService(
            new PaymentRepository(db),
            new PaymentMethodFactory(),
            NullLogger<PaymentAppService>.Instance);

        var guardMock = new Mock<RedisIdempotencyGuard>(_redisMock.Object);
        var tripId = Guid.NewGuid();
        var lockKey = $"trip-pay-{tripId}";

        // Simulate duplicate event: lock is already held
        guardMock
            .Setup(g => g.AcquireLockAsync(lockKey, It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);

        var consumer = new TripDroppedOffConsumer(
            paymentService,
            NullLogger<TripDroppedOffConsumer>.Instance,
            guardMock.Object);

        var consumeContextMock = new Mock<ConsumeContext<TripDroppedOffEvent>>();
        consumeContextMock.SetupGet(c => c.Message).Returns(new TripDroppedOffEvent(
            tripId,
            Rider1Id,
            Driver1Id,
            50000m,
            "Wallet",
            $"pay_{tripId}"));

        await consumer.Consume(consumeContextMock.Object);

        // Verification: no transaction was inserted into DB because consumer skipped early
        var txExists = await db.Transactions.AnyAsync(t => t.TripId == tripId);
        Assert.False(txExists);
    }
}
