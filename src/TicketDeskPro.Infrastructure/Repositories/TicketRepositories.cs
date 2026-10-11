using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TicketDeskPro.Domain.Entities;
using TicketDeskPro.Domain.Enums;
using TicketDeskPro.Domain.Interfaces;
using TicketDeskPro.Infrastructure.Data;

namespace TicketDeskPro.Infrastructure.Repositories;

public class EfTicketRepository : ITicketRepository
{
    private readonly AppDbContext _db;
    public EfTicketRepository(AppDbContext db) => _db = db;

    public async Task<Ticket?> GetByIdAsync(Guid id, bool includeDetails, bool includeDeleted, CancellationToken ct = default)
    {
        IQueryable<Ticket> query = _db.Tickets
            .Include(t => t.Category)
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo);

        if (includeDetails)
            query = query
                .Include(t => t.Tags).ThenInclude(tt => tt.Tag)
                .Include(t => t.SlaTimer);

        if (includeDeleted)
            query = query.IgnoreQueryFilters();

        return await query.FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<TicketPage> GetPageAsync(int limit, string? cursor, string? search, int? status, int? priority,
        Guid? categoryId, Guid? assignedToId, Guid? createdByUserId, bool onlyDeleted, string? sort, string? order, CancellationToken ct = default)
    {
        var descending = !string.Equals(order, "asc", StringComparison.OrdinalIgnoreCase);
        var q = _db.Tickets
            .AsNoTracking()
            .Include(t => t.Category)
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .AsQueryable();

        if (onlyDeleted)
            q = q.IgnoreQueryFilters().Where(t => t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Spec §3.1: search title + description + number. Escape LIKE wildcards so
            // user input matches literally (no "%" fishing for everything).
            var raw = search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var term = $"%{raw}%";
            q = q.Where(t =>
                EF.Functions.Like(t.Title, term, "\\") ||
                EF.Functions.Like(t.Description, term, "\\") ||
                EF.Functions.Like(t.TicketNumber, term, "\\"));
        }

        if (status is not null) q = q.Where(t => (int)t.Status == status);
        if (priority is not null) q = q.Where(t => (int)t.Priority == priority);
        if (categoryId is not null) q = q.Where(t => t.CategoryId == categoryId);
        if (assignedToId is not null) q = q.Where(t => t.AssignedToId == assignedToId);
        if (createdByUserId is not null) q = q.Where(t => t.CreatedById == createdByUserId);

        // ponytail: keyset (cursor) pagination on createdAt/updatedAt only. sort=priority
        // returns a single sorted page (nextCursor null) — priority cursor adds complexity
        // nobody has asked for; add a rank column later if it ever hurts.
        var cursorDate = default(DateTime);
        var cursorId = default(Guid);
        var useCursor = sort?.ToLowerInvariant() is not "priority"
                        && !string.IsNullOrEmpty(cursor)
                        && DecodeCursor(cursor, out cursorDate, out cursorId);

        if (useCursor)
        {
            q = sort?.ToLowerInvariant() switch
            {
                "updatedat" => descending
                    ? q.Where(t => t.UpdatedAt < cursorDate || (t.UpdatedAt == cursorDate && t.Id.CompareTo(cursorId) < 0))
                    : q.Where(t => t.UpdatedAt > cursorDate || (t.UpdatedAt == cursorDate && t.Id.CompareTo(cursorId) > 0)),
                _ => descending
                    ? q.Where(t => t.CreatedAt < cursorDate || (t.CreatedAt == cursorDate && t.Id.CompareTo(cursorId) < 0))
                    : q.Where(t => t.CreatedAt > cursorDate || (t.CreatedAt == cursorDate && t.Id.CompareTo(cursorId) > 0))
            };
        }

        q = sort?.ToLowerInvariant() switch
        {
            "updatedat" => descending ? q.OrderByDescending(t => t.UpdatedAt).ThenByDescending(t => t.Id)
                                      : q.OrderBy(t => t.UpdatedAt).ThenBy(t => t.Id),
            "priority" => descending ? q.OrderByDescending(t => t.Priority).ThenByDescending(t => t.CreatedAt)
                                     : q.OrderBy(t => t.Priority).ThenBy(t => t.CreatedAt),
            _ => descending ? q.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                            : q.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
        };

        var rows = await q.Take(limit + 1).ToListAsync(ct);
        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var last = rows.LastOrDefault();
        var nextCursor = hasMore && last is not null && sort?.ToLowerInvariant() is not "priority"
            ? EncodeCursor(sort?.ToLowerInvariant() == "updatedat" ? last.UpdatedAt : last.CreatedAt, last.Id)
            : null;

        return new TicketPage(rows, nextCursor, hasMore);
    }

    public async Task AddAsync(Ticket ticket, CancellationToken ct = default) =>
        await _db.Tickets.AddAsync(ticket, ct);

    public Task<int> CountLiveAsync(CancellationToken ct = default) =>
        _db.Tickets.CountAsync(ct);

    public Task<bool> TicketNumberExistsAsync(string ticketNumber, CancellationToken ct = default) =>
        // IgnoreQueryFilters: soft-deleted tickets keep their number forever (the unique
        // index is unfiltered) — reusing it would blow up the INSERT with a 500.
        _db.Tickets.IgnoreQueryFilters().AnyAsync(t => t.TicketNumber == ticketNumber, ct);

    public async Task<DateTime?> GetLastResolvedAtAsync(Guid ticketId, CancellationToken ct = default) =>
        await _db.TicketStatusHistories
            .Where(h => h.TicketId == ticketId && h.ToStatus == TicketStatus.Resolved)
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => (DateTime?)h.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public void Remove(Ticket ticket) => _db.Tickets.Remove(ticket);

    internal static string EncodeCursor(DateTime date, Guid id) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{date.Ticks}:{id}"));

    internal static bool DecodeCursor(string cursor, out DateTime date, out Guid id)
    {
        date = default; id = default;
        try
        {
            var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(':');
            date = new DateTime(long.Parse(parts[0]), DateTimeKind.Utc);
            return Guid.TryParse(parts[1], out id);
        }
        catch
        {
            return false; // malformed cursor = first page rather than a 500
        }
    }
}

public class EfTicketMessageRepository : ITicketMessageRepository
{
    private readonly AppDbContext _db;
    public EfTicketMessageRepository(AppDbContext db) => _db = db;

    public Task<TicketMessage?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.TicketMessages.Include(m => m.CreatedBy).FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<IReadOnlyList<TicketMessage>> GetThreadAsync(Guid ticketId, int limit, string? cursor, bool includeInternal, CancellationToken ct = default)
    {
        var q = _db.TicketMessages
            .AsNoTracking()
            .Include(m => m.CreatedBy)
            .Where(m => m.TicketId == ticketId);

        if (!includeInternal)
            q = q.Where(m => !m.IsInternal);

        if (!string.IsNullOrEmpty(cursor) && EfTicketRepository.DecodeCursor(cursor, out var cursorDate, out var cursorId))
            q = q.Where(m => m.CreatedAt > cursorDate || (m.CreatedAt == cursorDate && m.Id.CompareTo(cursorId) > 0));

        var rows = await q.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).Take(limit + 1).ToListAsync(ct);
        return rows;
    }

    public async Task AddAsync(TicketMessage message, CancellationToken ct = default) =>
        await _db.TicketMessages.AddAsync(message, ct);
}

public class EfTicketAttachmentRepository : ITicketAttachmentRepository
{
    private readonly AppDbContext _db;
    public EfTicketAttachmentRepository(AppDbContext db) => _db = db;

    public Task<TicketAttachment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.TicketAttachments.Include(a => a.UploadedBy).FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyList<TicketAttachment>> GetByTicketAsync(Guid ticketId, CancellationToken ct = default) =>
        await _db.TicketAttachments.AsNoTracking()
            .Include(a => a.UploadedBy)
            .Where(a => a.TicketId == ticketId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

    public Task<int> CountByTicketAsync(Guid ticketId, CancellationToken ct = default) =>
        _db.TicketAttachments.CountAsync(a => a.TicketId == ticketId, ct);

    public async Task AddAsync(TicketAttachment attachment, CancellationToken ct = default) =>
        await _db.TicketAttachments.AddAsync(attachment, ct);

    public void Remove(TicketAttachment attachment) => _db.TicketAttachments.Remove(attachment);
}

public class EfCategoryRepository : ICategoryRepository
{
    private readonly AppDbContext _db;
    public EfCategoryRepository(AppDbContext db) => _db = db;

    public Task<Category?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Category>> GetAllActiveAsync(CancellationToken ct = default) =>
        await _db.Categories.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(ct);
}

public class EfTagRepository : ITagRepository
{
    private readonly AppDbContext _db;
    public EfTagRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Tag>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        await _db.Tags.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
}

public class EfSlaPolicyRepository : ISlaPolicyRepository
{
    private readonly AppDbContext _db;
    public EfSlaPolicyRepository(AppDbContext db) => _db = db;

    public Task<SlaPolicy?> GetByPriorityAsync(int priority, CancellationToken ct = default) =>
        _db.SlaPolicies.FirstOrDefaultAsync(p => (int)p.Priority == priority && p.IsActive, ct);

    public Task<SlaPolicy?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.SlaPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<SlaPolicy>> GetAllAsync(CancellationToken ct = default) =>
        await _db.SlaPolicies.AsNoTracking().OrderBy(p => p.Priority).ToListAsync(ct);

    public Task<bool> PriorityExistsAsync(int priority, Guid? excludeId, CancellationToken ct = default) =>
        _db.SlaPolicies.AnyAsync(p => (int)p.Priority == priority && p.Id != excludeId, ct);

    public async Task AddAsync(SlaPolicy policy, CancellationToken ct = default) =>
        await _db.SlaPolicies.AddAsync(policy, ct);

    public void Remove(SlaPolicy policy) => _db.SlaPolicies.Remove(policy);
}

public class EfSlaBreachRepository : ISlaBreachRepository
{
    private readonly AppDbContext _db;
    public EfSlaBreachRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(SlaBreach breach, CancellationToken ct = default) =>
        await _db.SlaBreaches.AddAsync(breach, ct);
}

public class EfNotificationRepository : INotificationRepository
{
    private readonly AppDbContext _db;
    public EfNotificationRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(Notification notification, CancellationToken ct = default) =>
        await _db.Notifications.AddAsync(notification, ct);

    public async Task<IReadOnlyList<Notification>> GetRecentForUserAsync(Guid userId, int limit, CancellationToken ct = default) =>
        await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
}

public class EfSlaTimerRepository : ISlaTimerRepository
{
    private readonly AppDbContext _db;
    public EfSlaTimerRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<SlaTimer>> GetDueFirstResponseAsync(DateTime now, int limit, CancellationToken ct = default) =>
        await _db.SlaTimers
            .Include(t => t.Ticket).ThenInclude(t => t!.AssignedTo)
            // Closed tickets can no longer produce a first response — no breach after close.
            .Where(t => t.FirstResponseAt == null && !t.IsFirstResponseBreached
                        && t.FirstResponseDeadline < now
                        && t.Ticket!.Status != TicketStatus.Closed)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SlaTimer>> GetDueResolutionAsync(DateTime now, int limit, CancellationToken ct = default) =>
        await _db.SlaTimers
            .Include(t => t.Ticket).ThenInclude(t => t!.AssignedTo)
            // Only OPEN work can breach: resolved/closed tickets met the commitment, and
            // paused timers get their deadline pushed forward on resume (BR-031).
            .Where(t => !t.IsResolutionBreached && t.ResolutionDeadline < now
                        && t.PausedAt == null
                        && t.Ticket!.Status != TicketStatus.Resolved
                        && t.Ticket!.Status != TicketStatus.Closed)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SlaTimer>> GetWarningDueAsync(DateTime now, double warningRatio, int limit, CancellationToken ct = default)
    {
        // Server-side: still-open first-response windows only. The 75%-elapsed math
        // depends on a per-row policy value, so the final cut is computed in memory
        // on a small deadline-ordered batch.
        var candidates = await _db.SlaTimers
            .Include(t => t.Ticket).ThenInclude(t => t!.AssignedTo)
            .Include(t => t.SlaPolicy)
            // First response never pauses (BR-031 pauses only the resolution timer).
            .Where(t => !t.IsWarningSent && t.FirstResponseAt == null
                        && !t.IsFirstResponseBreached
                        && t.FirstResponseDeadline > now)
            .OrderBy(t => t.FirstResponseDeadline)
            .Take(limit)
            .ToListAsync(ct);

        // Threshold from the SNAPSHOT (deadline), not the live policy value —
        // policies may be edited after the timer was armed.
        return candidates
            .Where(t => now >= t.CreatedAt + (t.FirstResponseDeadline - t.CreatedAt) * warningRatio)
            .ToList();
    }

    public async Task<IReadOnlyList<Ticket>> GetStaleResolvedAsync(DateTime resolvedBefore, int limit, CancellationToken ct = default) =>
        await _db.Tickets
            .Where(t => t.Status == TicketStatus.Resolved
                        && !_db.TicketStatusHistories.Any(h => h.TicketId == t.Id && h.ToStatus == TicketStatus.Resolved && h.CreatedAt > resolvedBefore))
            .Take(limit)
            .ToListAsync(ct);
}
