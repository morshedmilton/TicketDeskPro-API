using TicketDeskPro.Domain.Entities;

namespace TicketDeskPro.Domain.Interfaces;

/// <summary>
/// Repository contracts live in Domain; EF implementations live in Infrastructure.
/// Application depends only on these — never on EF Core (FRD Clean Architecture).
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IUserRepository
{
    Task<User?> GetByEmailWithRolesAsync(string email, CancellationToken ct = default);
    Task<User?> GetByIdWithRolesAsync(Guid id, CancellationToken ct = default);
    Task<bool> AnyByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> GetByPasswordResetHashAsync(string tokenHash, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
}

public interface IRoleRepository
{
    Task<Role?> GetByNameAsync(string name, CancellationToken ct = default);
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyAsync(Guid familyId, CancellationToken ct = default);
    Task AddAsync(RefreshToken token, CancellationToken ct = default);
    Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Rotation in one atomic unit of work: revoke the old token, insert its replacement,
    /// link them for audit. Returns false when the token was already revoked — i.e. a
    /// concurrent request won the race (BR-004 single-use guarantee).
    /// </summary>
    Task<bool> RotateAsync(string oldTokenHash, string newTokenHash, Guid userId, Guid familyId, string? createdByIp, DateTime expiresAt, CancellationToken ct = default);
}

public interface ILoginAttemptRepository
{
    Task AddAsync(LoginAttempt attempt, CancellationToken ct = default);
    Task<IReadOnlyList<LoginAttempt>> GetByEmailSinceAsync(string email, DateTime sinceUtc, CancellationToken ct = default);
}

public interface IEmailOutboxRepository
{
    Task AddAsync(EmailOutbox email, CancellationToken ct = default);
}
