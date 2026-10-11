namespace TicketDeskPro.Application.Interfaces;

/// <summary>
/// Redis-backed cache-aside helper. Read path: try cache → miss → compute → store.
/// Write path: mutate the database → RemoveByPrefix so the next read recomputes.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
}
