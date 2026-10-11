using Microsoft.EntityFrameworkCore;
using TicketDeskPro.Application.Services.Admin;
using TicketDeskPro.Domain.Entities;
using TicketDeskPro.Domain.Enums;
using TicketDeskPro.Domain.Interfaces;
using TicketDeskPro.Infrastructure.Data;

namespace TicketDeskPro.Infrastructure.Repositories;

/// <summary>Dashboard read models (US-8.01). Server-side filtering + grouping; small
/// result sets are finished in memory (portable across providers, no raw SQL).</summary>
public class EfDashboardRepository : IDashboardRepository
{
    private static readonly int[] OpenStatuses =
        [(int)TicketStatus.Open, (int)TicketStatus.InProgress, (int)TicketStatus.AwaitingCustomer,
         (int)TicketStatus.CustomerReplied, (int)TicketStatus.Reopened];

    private readonly AppDbContext _db;
    public EfDashboardRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<(TicketPriority Priority, int Count)>> GetOpenCountsByPriorityAsync(CancellationToken ct = default)
    {
        var rows = await _db.Tickets.AsNoTracking()
            .Where(t => OpenStatuses.Contains((int)t.Status))
            .GroupBy(t => t.Priority)
            .Select(g => new { Priority = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return rows.Select(r => (r.Priority, r.Count)).ToList();
    }

    public async Task<(int ResolvedDistinct7d, int ResolvedWithTimer7d, int WithinSla7d, double AvgFirstResponseMinutes, double AvgResolutionMinutes)> GetResolutionStatsAsync(DateTime now, int days, CancellationToken ct = default)
    {
        var since = now.AddDays(-days);

        var resolved = await _db.TicketStatusHistories.AsNoTracking()
            .Where(h => h.ToStatus == TicketStatus.Resolved && h.CreatedAt >= since)
            .Select(h => new { h.TicketId, h.CreatedAt })
            .ToListAsync(ct);

        var ticketIds = resolved.Select(h => h.TicketId).Distinct().ToList();
        var tickets = await _db.Tickets.AsNoTracking()
            .Where(t => ticketIds.Contains(t.Id))
            .Select(t => new { t.Id, t.CreatedAt })
            .ToListAsync(ct);
        var ticketMap = tickets.ToDictionary(t => t.Id, t => t.CreatedAt);

        var timers = await _db.SlaTimers.AsNoTracking()
            .Where(s => ticketIds.Contains(s.TicketId))
            .Select(s => new { s.TicketId, s.FirstResponseAt, s.FirstResponseDeadline, s.CreatedAt })
            .ToListAsync(ct);

        // SLA compliance: first response landed before its deadline (among resolved tickets with timers).
        var withinSla = timers.Count(s => s.FirstResponseAt != null && s.FirstResponseAt <= s.FirstResponseDeadline);
        var resolvedDistinct = ticketIds.Count;

        var avgFirstResponse = timers.Where(s => s.FirstResponseAt != null)
            .Select(s => (s.FirstResponseAt!.Value - s.CreatedAt).TotalMinutes)
            .DefaultIfEmpty(0)
            .Average();

        var avgResolution = resolved
            .Where(h => ticketMap.ContainsKey(h.TicketId))
            .Select(h => (h.CreatedAt - ticketMap[h.TicketId]).TotalMinutes)
            .DefaultIfEmpty(0)
            .Average();

        return (resolvedDistinct, timers.Count, withinSla, Math.Round(avgFirstResponse, 1), Math.Round(avgResolution, 1));
    }

    public async Task<IReadOnlyList<(DateTime Day, int Created, int Resolved)>> GetDailyTrendsAsync(DateTime now, int days, CancellationToken ct = default)
    {
        var since = now.Date.AddDays(-(days - 1));

        var created = await _db.Tickets.AsNoTracking()
            .Where(t => t.CreatedAt >= since)
            .Select(t => t.CreatedAt)
            .ToListAsync(ct);

        var resolved = await _db.TicketStatusHistories.AsNoTracking()
            .Where(h => h.ToStatus == TicketStatus.Resolved && h.CreatedAt >= since)
            .Select(h => h.CreatedAt)
            .ToListAsync(ct);

        var createdMap = created.GroupBy(d => d.Date).ToDictionary(g => g.Key, g => g.Count());
        var resolvedMap = resolved.GroupBy(d => d.Date).ToDictionary(g => g.Key, g => g.Count());

        var points = new List<(DateTime Day, int Created, int Resolved)>();
        for (var day = since; day <= now.Date; day = day.AddDays(1))
            points.Add((day, createdMap.GetValueOrDefault(day), resolvedMap.GetValueOrDefault(day)));
        return points;
    }

    public async Task<IReadOnlyList<(string CategoryName, int Count)>> GetCategoryCountsAsync(CancellationToken ct = default)
    {
        var rows = await _db.Tickets.AsNoTracking()
            .GroupBy(t => t.Category.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(ct);
        return rows.Select(r => (r.Name, r.Count)).ToList();
    }

    public async Task<IReadOnlyList<(Guid UserId, string AgentName, int OpenAssigned, int Resolved30d, double AvgResolutionMinutes)>> GetAgentStatsAsync(DateTime now, int days, CancellationToken ct = default)
    {
        var since = now.AddDays(-days);

        var agentRows = await (
            from u in _db.Users.AsNoTracking()
            where u.IsActive && u.UserRoles.Any(ur => ur.Role!.Name == "Agent")
            select new
            {
                u.Id,
                Name = u.FirstName + " " + u.LastName,
                Open = _db.Tickets.Count(t => t.AssignedToId == u.Id && OpenStatuses.Contains((int)t.Status))
            }
        ).ToListAsync(ct);

        var resolvedByAgent = await _db.TicketStatusHistories.AsNoTracking()
            .Where(h => h.ToStatus == TicketStatus.Resolved && h.CreatedAt >= since && h.Ticket!.AssignedToId != null)
            .Select(h => new { AgentId = h.Ticket!.AssignedToId!.Value, h.CreatedAt, TicketCreated = h.Ticket.CreatedAt })
            .ToListAsync(ct);

        return agentRows.Select(a =>
        {
            var mine = resolvedByAgent.Where(r => r.AgentId == a.Id).ToList();
            var avg = mine.Count == 0 ? 0.0 : Math.Round(mine.Average(r => (r.CreatedAt - r.TicketCreated).TotalMinutes), 1);
            return (a.Id, a.Name, a.Open, mine.Count, avg);
        }).ToList();
    }
}

/// <summary>Filterable, cursor-paginated audit log viewer (US-9.02).</summary>
public class EfAuditLogRepository : IAuditLogRepository
{
    private readonly AppDbContext _db;
    public EfAuditLogRepository(AppDbContext db) => _db = db;

    public async Task<AuditLogPage> GetPageAsync(Guid? userId, string? entityType, string? action,
        DateTime? from, DateTime? to, int limit, string? cursor, CancellationToken ct = default)
    {
        var q = _db.AuditLogs.AsNoTracking().Include(a => a.User).AsQueryable();

        if (userId is not null) q = q.Where(a => a.UserId == userId);
        if (!string.IsNullOrWhiteSpace(entityType)) q = q.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action == action);
        if (from is not null) q = q.Where(a => a.Timestamp >= from);
        if (to is not null) q = q.Where(a => a.Timestamp <= to);

        if (!string.IsNullOrEmpty(cursor) && EfTicketRepository.DecodeCursor(cursor, out var cursorDate, out var cursorId))
            q = q.Where(a => a.Timestamp < cursorDate || (a.Timestamp == cursorDate && a.Id.CompareTo(cursorId) < 0));

        var rows = await q.OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id)
            .Take(limit + 1).ToListAsync(ct);
        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var last = rows.LastOrDefault();
        var next = hasMore && last is not null ? EfTicketRepository.EncodeCursor(last.Timestamp, last.Id) : null;
        return new AuditLogPage(rows, next, hasMore);
    }
}

/// <summary>Permission catalog lookup for role management.</summary>
public class EfPermissionLookup : IPermissionLookup
{
    private readonly AppDbContext _db;
    public EfPermissionLookup(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Permissions.AsNoTracking().OrderBy(p => p.Module).ThenBy(p => p.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<Permission>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        await _db.Permissions.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
}
