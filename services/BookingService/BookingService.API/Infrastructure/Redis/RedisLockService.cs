using StackExchange.Redis;

namespace BookingService.API.Infrastructure.Redis;

public interface IRedisLockService
{
    Task<string?> AcquireAsync(string resource, TimeSpan ttl, CancellationToken ct = default);
    Task          ReleaseAsync(string resource, string lockToken, CancellationToken ct = default);
}

public class RedisLockService : IRedisLockService
{
    private readonly IDatabase _db;
    private readonly ILogger<RedisLockService> _logger;

    // Atomic Lua — only release if WE own the lock (token matches)
    // Prevents releasing another instance's lock if our TTL expired
    private const string ReleaseLua = @"
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        else
            return 0
        end";

    public RedisLockService(IConnectionMultiplexer redis, ILogger<RedisLockService> logger)
    {
        _db     = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<string?> AcquireAsync(
        string resource, TimeSpan ttl, CancellationToken ct = default)
    {
        var token = Guid.NewGuid().ToString("N");

        // SET key token NX PX {ms}
        // NX = only set if NOT exists → atomic
        // PX = auto-expire → no orphan locks if process crashes
        var acquired = await _db.StringSetAsync(resource, token, ttl, When.NotExists);

        if (!acquired)
        {
            _logger.LogWarning("Lock already held on {Resource}", resource);
            return null;
        }

        _logger.LogDebug("Lock acquired on {Resource} ttl={Ttl}", resource, ttl);
        return token;
    }

    public async Task ReleaseAsync(
        string resource, string lockToken, CancellationToken ct = default)
    {
        try
        {
            var result = (long)await _db.ScriptEvaluateAsync(
                ReleaseLua,
                keys:   new RedisKey[]   { resource  },
                values: new RedisValue[] { lockToken });

            if (result == 0)
                _logger.LogWarning(
                    "Lock release skipped on {Resource} — token mismatch or TTL expired", resource);
            else
                _logger.LogDebug("Lock released on {Resource}", resource);
        }
        catch (Exception ex)
        {
            // Do NOT rethrow — lock will self-expire via TTL
            _logger.LogError(ex, "Error releasing lock on {Resource}", resource);
        }
    }
}
