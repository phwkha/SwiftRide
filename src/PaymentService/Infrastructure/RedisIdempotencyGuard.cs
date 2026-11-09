using StackExchange.Redis;

namespace PaymentService.Infrastructure;

public class RedisIdempotencyGuard
{
    private readonly IConnectionMultiplexer _redis;

    public RedisIdempotencyGuard(IConnectionMultiplexer redis) => _redis = redis;

    /// <summary>
    /// Trả về true nếu đây là lần đầu xử lý giao dịch.
    /// Trả về false nếu giao dịch này đang được xử lý hoặc đã xử lý rồi.
    /// </summary>
    public virtual async Task<bool> AcquireLockAsync(string idempotencyKey, TimeSpan ttl)
    {
        var db = _redis.GetDatabase();
        var key = $"payment:idemp:{idempotencyKey}";
        return await db.StringSetAsync(key, "PROCESSING", ttl, When.NotExists);
    }

    public virtual async Task ReleaseLockOnFailureAsync(string idempotencyKey)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"payment:idemp:{idempotencyKey}");
    }
}
