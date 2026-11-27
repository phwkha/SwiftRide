using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace SwiftRide.Messaging;

public static class DatabaseStartup
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(3);

    public static async Task RunAsync(
        string redisConnection,
        string service,
        Func<CancellationToken, Task> initialize,
        ILogger logger)
    {
        using var redis = await ConnectionMultiplexer.ConnectAsync(redisConnection);
        var database = redis.GetDatabase();
        var key = $"swiftride:database-startup:{service}";
        var owner = Guid.NewGuid().ToString("N");
        using var timeout = new CancellationTokenSource(Timeout);

        while (!await database.LockTakeAsync(key, owner, LeaseDuration))
            await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);

        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            await initialize(timeout.Token);
        }
        finally
        {
            try
            {
                await database.LockReleaseAsync(key, owner);
            }
            catch (RedisException exception)
            {
                logger.LogWarning(exception, "Could not release the database startup lock for {Service}.", service);
            }
        }
    }
}
