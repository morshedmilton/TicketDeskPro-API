using Microsoft.EntityFrameworkCore;
using TicketDeskPro.Domain.Entities;
using TicketDeskPro.Domain.Enums;
using TicketDeskPro.Domain.Interfaces;
using TicketDeskPro.Infrastructure.Data;

namespace TicketDeskPro.Infrastructure.Repositories;

/// <summary>Thin EF implementations of the Domain repository contracts.</summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;
    public UnitOfWork(AppDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}

public class EfUserRepository : IUserRepository
{
    private readonly AppDbContext _db;
    public EfUserRepository(AppDbContext db) => _db = db;

    public Task<User?> GetByEmailWithRolesAsync(string email, CancellationToken ct = default) =>
        WithRoles().FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<User?> GetByIdWithRolesAsync(Guid id, CancellationToken ct = default) =>
        WithRoles().FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<bool> AnyByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Users.AnyAsync(u => u.Email == email, ct);

    public Task<User?> GetByPasswordResetHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.PasswordResetTokenHash == tokenHash, ct);

    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await _db.Users.AddAsync(user, ct);

    public async Task<IReadOnlyList<User>> GetActiveAgentsByLoadAsync(Guid? categoryId = null, CancellationToken ct = default)
    {
        var agentRole = _db.Roles.First(r => r.Name == "Agent").Id;
        // Open = not Closed (same definition the assignment pool always used).
        return await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.UserRoles.Any(ur => ur.RoleId == agentRole))
            .Select(u => new
            {
                User = u,
                // Category-aware first: spread each category's work evenly —
                // the agent with the FEWEST open tickets in this category wins.
                // Written without a conditional so Npgsql translates it for sure;
                // a null categoryId simply never matches an FK, giving everyone 0.
                CategoryOpen = _db.Tickets.Count(t => t.AssignedToId == u.Id
                                                  && t.CategoryId == categoryId
                                                  && t.Status != Domain.Enums.TicketStatus.Closed),
                OpenTickets = _db.Tickets.Count(t => t.AssignedToId == u.Id && t.Status != Domain.Enums.TicketStatus.Closed)
            })
            .OrderBy(x => x.CategoryOpen)         // category load decides…
            .ThenBy(x => x.OpenTickets)           // …then overall load…
            .ThenBy(x => x.User.CreatedAt)        // …then seniority (stable, fair)
            .Select(x => x.User)
            .Take(50)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<User>> GetActiveByRoleAsync(string roleName, CancellationToken ct = default) =>
        await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.UserRoles.Any(ur => ur.Role!.Name == roleName))
            .ToListAsync(ct);

    public Task<NotificationPreference?> GetNotificationPreferenceAsync(Guid userId, CancellationToken ct = default) =>
        _db.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

    public Task<NotificationPreference?> GetNotificationPreferenceTrackedAsync(Guid userId, CancellationToken ct = default) =>
        _db.NotificationPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);

    public async Task SetNotificationPreferenceAsync(NotificationPreference preference, CancellationToken ct = default) =>
        await _db.NotificationPreferences.AddAsync(preference, ct);

    public async Task<UserPage> GetPageAsync(string? search, string? roleName, int limit, string? cursor, CancellationToken ct = default)
    {
        var q = _db.Users.AsNoTracking().Include(u => u.UserRoles).ThenInclude(ur => ur.Role).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim().Replace("%", "\\%").Replace("_", "\\_")}%";
            q = q.Where(u => EF.Functions.Like(u.Email, term, "\\") || EF.Functions.Like(u.FirstName, term, "\\") || EF.Functions.Like(u.LastName, term, "\\") || EF.Functions.Like(u.Phone ?? "", term, "\\"));
        }
        if (!string.IsNullOrWhiteSpace(roleName))
            q = q.Where(u => u.UserRoles.Any(ur => ur.Role!.Name == roleName));

        if (!string.IsNullOrEmpty(cursor) && EfTicketRepository.DecodeCursor(cursor, out var cursorDate, out var cursorId))
            q = q.Where(u => u.CreatedAt < cursorDate || (u.CreatedAt == cursorDate && u.Id.CompareTo(cursorId) < 0));

        var rows = await q.OrderByDescending(u => u.CreatedAt).ThenByDescending(u => u.Id)
            .Take(limit + 1).ToListAsync(ct);
        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var last = rows.LastOrDefault();
        var next = hasMore && last is not null ? EfTicketRepository.EncodeCursor(last.CreatedAt, last.Id) : null;
        return new UserPage(rows, next, hasMore);
    }

    public Task<User?> GetByIdWithRolesTrackedAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default)
    {
        var q = WithRoles();
        if (includeDeleted) q = q.IgnoreQueryFilters();
        return q.FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public void Remove(User user) => _db.Users.Remove(user);

    private IQueryable<User> WithRoles() =>
        _db.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .ThenInclude(r => r.RolePermissions)
            .ThenInclude(rp => rp.Permission);
}

public class EfRoleRepository : IRoleRepository
{
    private readonly AppDbContext _db;
    public EfRoleRepository(AppDbContext db) => _db = db;

    public Task<Role?> GetByNameAsync(string name, CancellationToken ct = default) =>
        _db.Roles.FirstOrDefaultAsync(r => r.Name == name, ct);

    public async Task<IReadOnlyList<Role>> GetAllWithPermissionsAsync(CancellationToken ct = default) =>
        await _db.Roles.AsNoTracking()
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

    public Task<Role?> GetByIdWithPermissionsAsync(Guid id, CancellationToken ct = default) =>
        _db.Roles
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<bool> NameExistsAsync(string name, Guid? excludeId, CancellationToken ct = default) =>
        _db.Roles.AnyAsync(r => r.Name == name && r.Id != excludeId, ct);

    public async Task AddAsync(Role role, CancellationToken ct = default) =>
        await _db.Roles.AddAsync(role, ct);

    public void Remove(Role role) => _db.Roles.Remove(role);
}

public class EfRefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _db;
    public EfRefreshTokenRepository(AppDbContext db) => _db = db;

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyAsync(Guid familyId, CancellationToken ct = default) =>
        await _db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(ct);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, CancellationToken ct = default) =>
        await _db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);

    public async Task AddAsync(RefreshToken token, CancellationToken ct = default) =>
        await _db.RefreshTokens.AddAsync(token, ct);

    public async Task<bool> RotateAsync(string oldTokenHash, string newTokenHash, Guid userId, Guid familyId, string? createdByIp, DateTime expiresAt, CancellationToken ct = default)
    {
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == oldTokenHash, ct);
        if (stored is null || stored.RevokedAt is not null)
            return false; // already rotated/revoked — reuse or lost race

        stored.RevokedAt = DateTime.UtcNow;
        stored.ReplacedByTokenHash = newTokenHash;
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = newTokenHash,
            FamilyId = familyId,
            ExpiresAt = expiresAt,
            CreatedByIp = createdByIp
        });

        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // xmin guard: a parallel refresh rotated this token first. Lose cleanly.
            return false;
        }
    }
}

public class EfLoginAttemptRepository : ILoginAttemptRepository
{
    private readonly AppDbContext _db;
    public EfLoginAttemptRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(LoginAttempt attempt, CancellationToken ct = default) =>
        await _db.LoginAttempts.AddAsync(attempt, ct);

    public async Task<IReadOnlyList<LoginAttempt>> GetByEmailSinceAsync(string email, DateTime sinceUtc, CancellationToken ct = default) =>
        await _db.LoginAttempts
            .Where(a => a.Email == email && a.CreatedAt >= sinceUtc)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
}

public class EfEmailOutboxRepository : IEmailOutboxRepository
{
    private readonly AppDbContext _db;
    public EfEmailOutboxRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(EmailOutbox email, CancellationToken ct = default) =>
        await _db.EmailOutbox.AddAsync(email, ct);

    public async Task<IReadOnlyList<EmailOutbox>> GetDuePendingAsync(DateTime now, int limit, CancellationToken ct = default) =>
        await _db.EmailOutbox
            .Where(o => o.Status == OutboxStatus.Pending && o.NextAttemptAt <= now)
            .OrderBy(o => o.NextAttemptAt)
            .Take(limit)
            .ToListAsync(ct);
}
