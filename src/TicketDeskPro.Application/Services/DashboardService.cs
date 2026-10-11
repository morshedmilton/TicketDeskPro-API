using TicketDeskPro.Application.DTOs.Dashboard;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Domain.Enums;
using TicketDeskPro.Domain.Interfaces;

namespace TicketDeskPro.Application.Services;

/// <summary>
/// US-8.01/8.02 dashboard aggregates. Cache-aside via Redis: every read tries the
/// cache first; ticket mutations call invalidation (prefix "dash:") so staleness
/// is bounded by invalidation, not just TTL.
/// </summary>
public interface IDashboardService
{
    Task<DashboardKpisDto> GetKpisAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TicketTrendPointDto>> GetTrendsAsync(int days = 30, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryDistributionPointDto>> GetCategoryDistributionAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AgentPerformanceDto>> GetAgentPerformanceAsync(CancellationToken ct = default);
}

public class DashboardService : IDashboardService
{
    public const string CachePrefix = "dash:";
    public static readonly TimeSpan KpiTtl = TimeSpan.FromSeconds(30);

    private readonly IDashboardRepository _dashboard;
    private readonly ICacheService _cache;

    public DashboardService(IDashboardRepository dashboard, ICacheService cache)
    {
        _dashboard = dashboard;
        _cache = cache;
    }

    public async Task<DashboardKpisDto> GetKpisAsync(CancellationToken ct = default)
    {
        const string key = $"{CachePrefix}kpis";
        var cached = await _cache.GetAsync<DashboardKpisDto>(key, ct);
        if (cached is not null) return cached;

        var now = DateTime.UtcNow;
        var open = await _dashboard.GetOpenCountsByPriorityAsync(ct);
        var stats = await _dashboard.GetResolutionStatsAsync(now, days: 7, ct);

        // Compliance = met first-response SLA / resolved-with-timer (empty window = perfect).
        var compliance = stats.ResolvedWithTimer7d == 0
            ? 100.0
            : Math.Round(100.0 * stats.WithinSla7d / stats.ResolvedWithTimer7d, 1);

        var kpis = new DashboardKpisDto(
            new OpenByPriorityDto(
                open.FirstOrDefault(x => x.Priority == TicketPriority.Low).Count,
                open.FirstOrDefault(x => x.Priority == TicketPriority.Medium).Count,
                open.FirstOrDefault(x => x.Priority == TicketPriority.High).Count,
                open.FirstOrDefault(x => x.Priority == TicketPriority.Urgent).Count),
            stats.ResolvedDistinct7d,
            Math.Round(stats.AvgFirstResponseMinutes, 1),
            Math.Round(stats.AvgResolutionMinutes, 1),
            compliance,
            now);

        await _cache.SetAsync(key, kpis, KpiTtl, ct);
        return kpis;
    }

    public async Task<IReadOnlyList<TicketTrendPointDto>> GetTrendsAsync(int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 7, 90);
        var key = $"{CachePrefix}trends:{days}";
        var cached = await _cache.GetAsync<List<TicketTrendPointDto>>(key, ct);
        if (cached is not null) return cached;

        var points = (await _dashboard.GetDailyTrendsAsync(DateTime.UtcNow, days, ct))
            .Select(t => new TicketTrendPointDto(t.Day, t.Created, t.Resolved))
            .ToList();

        await _cache.SetAsync(key, points, KpiTtl, ct);
        return points;
    }

    public async Task<IReadOnlyList<CategoryDistributionPointDto>> GetCategoryDistributionAsync(CancellationToken ct = default)
    {
        const string key = $"{CachePrefix}categories";
        var cached = await _cache.GetAsync<List<CategoryDistributionPointDto>>(key, ct);
        if (cached is not null) return cached;

        var points = (await _dashboard.GetCategoryCountsAsync(ct))
            .Select(c => new CategoryDistributionPointDto(c.CategoryName, c.Count))
            .ToList();

        await _cache.SetAsync(key, points, KpiTtl, ct);
        return points;
    }

    public async Task<IReadOnlyList<AgentPerformanceDto>> GetAgentPerformanceAsync(CancellationToken ct = default)
    {
        const string key = $"{CachePrefix}agents";
        var cached = await _cache.GetAsync<List<AgentPerformanceDto>>(key, ct);
        if (cached is not null) return cached;

        var rows = (await _dashboard.GetAgentStatsAsync(DateTime.UtcNow, 30, ct))
            .Select(a => new AgentPerformanceDto(a.UserId, a.AgentName, a.OpenAssigned, a.Resolved30d, Math.Round(a.AvgResolutionMinutes, 1)))
            .OrderByDescending(a => a.ResolvedLast30Days)
            .ToList();

        await _cache.SetAsync(key, rows, KpiTtl, ct);
        return rows;
    }
}
