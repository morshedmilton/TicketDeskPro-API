using Microsoft.EntityFrameworkCore;
using TicketDeskPro.Domain.Entities;
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
}
