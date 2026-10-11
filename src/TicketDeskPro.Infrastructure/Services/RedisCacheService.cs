using System.Text.Json;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using TicketDeskPro.Application.Interfaces;

namespace TicketDeskPro.Infrastructure.Services;

/// <summary>Redis cache-aside implementation. Prefix invalidation scans keys —
/// never the blocking KEYS command.
/// </summary>
public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    // The cache is an OPTIMIZATION, never a dependency: every failure degrades to
    // "cache miss / invalidation skipped" — a Redis outage must never 500 a request
    // (especially not a mutation that already committed).
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var value = await _redis.GetDatabase().StringGetAsync(key);
            return value.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>((string)value!);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache read failed for {Key} — falling through to the database", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            await _redis.GetDatabase().StringSetAsync(key, JsonSerializer.Serialize(value), ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache write failed for {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache delete failed for {Key}", key);
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            foreach (var server in _redis.GetServers())
            {
                if (!server.IsConnected) continue;
                foreach (var key in server.Keys(pattern: $"{prefix}*"))
                    await db.KeyDeleteAsync(key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache prefix invalidation failed for {Prefix}", prefix);
        }
    }
}
