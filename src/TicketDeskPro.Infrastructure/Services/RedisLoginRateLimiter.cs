using StackExchange.Redis;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Shared.Constants;
using TicketDeskPro.Shared.Exceptions;

namespace TicketDeskPro.Infrastructure.Services;

/// <summary>
/// Sliding window in Redis: one sorted set per (IP, email), scores are unix-ms timestamps.
/// The whole check runs as ONE Lua script so concurrent requests cannot race past the limit
/// (trim + count + add must be atomic). Fail closed: any Redis failure blocks login (SYS_002).
/// </summary>
public class RedisLoginRateLimiter : ILoginRateLimiter
{
    public const int WindowMinutes = 5;
    public const int MaxAttemptsPerWindow = 10;

    private readonly IConnectionMultiplexer _redis;

    public RedisLoginRateLimiter(IConnectionMultiplexer redis) => _redis = redis;

    // KEYS[1]=window key, ARGV[1]=window start (ms), ARGV[2]=now (ms), ARGV[3]=limit, ARGV[4]=member, ARGV[5]=window ttl seconds
    // Returns 1 when allowed, 0 when the limit is exhausted.
    private const string SlidingWindowScript = """
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[1])
        if redis.call('ZCARD', KEYS[1]) >= tonumber(ARGV[3]) then
            return 0
        end
        redis.call('ZADD', KEYS[1], ARGV[2], ARGV[4])
        redis.call('EXPIRE', KEYS[1], ARGV[5])
        return 1
        """;

    public Task CheckAndRecordAsync(string ipAddress, string email, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = BuildKey(ipAddress, email);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var windowStart = now - WindowMinutes * 60_000L;

            var allowed = (int?)db.ScriptEvaluate(
                SlidingWindowScript,
                keys: [(RedisKey)key],
                values:
                [
                    (RedisValue)windowStart,
                    (RedisValue)now,
                    (RedisValue)MaxAttemptsPerWindow,
                    (RedisValue)Guid.NewGuid().ToString("N"),
                    (RedisValue)(WindowMinutes * 60)
                ]);

            if (allowed == 0)
                throw new RateLimitedException(ErrorCodes.AuthRateLimited, "Too many login attempts. Try again later.");

            return Task.CompletedTask;
        }
        catch (RateLimitedException)
        {
            throw;
        }
        catch (Exception)
        {
            // Fail closed: if we cannot count attempts, we do not authenticate (FRD §15 SYS_002).
            throw new ServiceUnavailableException(ErrorCodes.RedisUnavailable, "Auth service is temporarily unavailable. Try again shortly.");
        }
    }

    public async Task ResetAsync(string ipAddress, string email, CancellationToken ct = default)
    {
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(BuildKey(ipAddress, email));
        }
        catch (Exception)
        {
            // A failed reset only makes the window stricter — never block a successful login here.
        }
    }

    private static string BuildKey(string ipAddress, string email) =>
        $"rl:login:{ipAddress}:{email.ToLowerInvariant()}";
}
