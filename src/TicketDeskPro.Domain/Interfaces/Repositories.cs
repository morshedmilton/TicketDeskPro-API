using TicketDeskPro.Domain.Entities;
using TicketDeskPro.Domain.Enums;

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
    /// <summary>Active users holding the Agent role (assignment pool). With a categoryId,
    /// agents least-loaded IN THAT CATEGORY come first (US-6.02 category-aware
    /// round-robin); ties fall back to overall open-ticket load, then seniority.</summary>
    Task<IReadOnlyList<User>> GetActiveAgentsByLoadAsync(Guid? categoryId = null, CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetActiveByRoleAsync(string roleName, CancellationToken ct = default);
    Task<NotificationPreference?> GetNotificationPreferenceAsync(Guid userId, CancellationToken ct = default);
    Task<NotificationPreference?> GetNotificationPreferenceTrackedAsync(Guid userId, CancellationToken ct = default);
    Task SetNotificationPreferenceAsync(NotificationPreference preference, CancellationToken ct = default);

    // Admin management (US-1.08)
    Task<UserPage> GetPageAsync(string? search, string? roleName, int limit, string? cursor, CancellationToken ct = default);
    Task<User?> GetByIdWithRolesTrackedAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default);
    void Remove(User user);
}

public record UserPage(IReadOnlyList<User> Items, string? NextCursor, bool HasMore);

public interface IRoleRepository
{
    Task<Role?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetAllWithPermissionsAsync(CancellationToken ct = default);
    Task<Role?> GetByIdWithPermissionsAsync(Guid id, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, Guid? excludeId, CancellationToken ct = default);
    Task AddAsync(Role role, CancellationToken ct = default);
    void Remove(Role role);
}

public interface IAuditLogRepository
{
    Task<AuditLogPage> GetPageAsync(Guid? userId, string? entityType, string? action,
        DateTime? from, DateTime? to, int limit, string? cursor, CancellationToken ct = default);
}

public record AuditLogPage(IReadOnlyList<AuditLog> Items, string? NextCursor, bool HasMore);

/// <summary>Read-model queries behind the dashboard (Redis caches their results).</summary>
public interface IDashboardRepository
{
    Task<IReadOnlyList<(TicketPriority Priority, int Count)>> GetOpenCountsByPriorityAsync(CancellationToken ct = default);
    Task<(int ResolvedDistinct7d, int ResolvedWithTimer7d, int WithinSla7d, double AvgFirstResponseMinutes, double AvgResolutionMinutes)> GetResolutionStatsAsync(DateTime now, int days, CancellationToken ct = default);
    Task<IReadOnlyList<(DateTime Day, int Created, int Resolved)>> GetDailyTrendsAsync(DateTime now, int days, CancellationToken ct = default);
    Task<IReadOnlyList<(string CategoryName, int Count)>> GetCategoryCountsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<(Guid UserId, string AgentName, int OpenAssigned, int Resolved30d, double AvgResolutionMinutes)>> GetAgentStatsAsync(DateTime now, int days, CancellationToken ct = default);
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
    Task<IReadOnlyList<EmailOutbox>> GetDuePendingAsync(DateTime now, int limit, CancellationToken ct = default);
}

/// <summary>Cursor page of tickets plus the cursor for the next page (null = no more).</summary>
public record TicketPage(IReadOnlyList<Ticket> Items, string? NextCursor, bool HasMore);

public interface ITicketRepository
{
    Task<Ticket?> GetByIdAsync(Guid id, bool includeDetails, bool includeDeleted, CancellationToken ct = default);
    Task<TicketPage> GetPageAsync(int limit, string? cursor, string? search, int? status, int? priority,
        Guid? categoryId, Guid? assignedToId, Guid? createdByUserId, bool onlyDeleted, string? sort, string? order, CancellationToken ct = default);
    Task AddAsync(Ticket ticket, CancellationToken ct = default);
    void Remove(Ticket ticket);
    Task<int> CountLiveAsync(CancellationToken ct = default);
    Task<bool> TicketNumberExistsAsync(string ticketNumber, CancellationToken ct = default);
    Task<DateTime?> GetLastResolvedAtAsync(Guid ticketId, CancellationToken ct = default);
}

public interface ITicketMessageRepository
{
    Task<TicketMessage?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>Returns up to limit+1 messages (ascending) so the caller can derive hasMore.</summary>
    Task<IReadOnlyList<TicketMessage>> GetThreadAsync(Guid ticketId, int limit, string? cursor, bool includeInternal, CancellationToken ct = default);
    Task AddAsync(TicketMessage message, CancellationToken ct = default);
}

public interface ITicketAttachmentRepository
{
    Task<TicketAttachment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TicketAttachment>> GetByTicketAsync(Guid ticketId, CancellationToken ct = default);
    Task<int> CountByTicketAsync(Guid ticketId, CancellationToken ct = default);
    Task AddAsync(TicketAttachment attachment, CancellationToken ct = default);
    void Remove(TicketAttachment attachment);
}

public interface ICategoryRepository
{
    Task<Category?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Category>> GetAllActiveAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, Guid? excludeId, CancellationToken ct = default);
    Task<bool> IsReferencedByTicketsAsync(Guid categoryId, CancellationToken ct = default);
    Task AddAsync(Category category, CancellationToken ct = default);
    void Remove(Category category);
}

public interface ITagRepository
{
    Task<IReadOnlyList<Tag>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken ct = default);
    Task<Tag?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, Guid? excludeId, CancellationToken ct = default);
    Task<bool> IsReferencedByTicketsAsync(Guid tagId, CancellationToken ct = default);
    Task AddAsync(Tag tag, CancellationToken ct = default);
    void Remove(Tag tag);
}

public interface ISlaPolicyRepository
{
    Task<SlaPolicy?> GetByPriorityAsync(int priority, CancellationToken ct = default);
    Task<SlaPolicy?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SlaPolicy>> GetAllAsync(CancellationToken ct = default);
    Task<bool> PriorityExistsAsync(int priority, Guid? excludeId, CancellationToken ct = default);
    Task AddAsync(SlaPolicy policy, CancellationToken ct = default);
    void Remove(SlaPolicy policy);
}

public interface ISlaBreachRepository
{
    Task AddAsync(SlaBreach breach, CancellationToken ct = default);
}

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken ct = default);
    Task<IReadOnlyList<Notification>> GetRecentForUserAsync(Guid userId, int limit, CancellationToken ct = default);
}

public interface ISlaTimerRepository
{
    Task<IReadOnlyList<SlaTimer>> GetDueFirstResponseAsync(DateTime now, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<SlaTimer>> GetDueResolutionAsync(DateTime now, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<SlaTimer>> GetWarningDueAsync(DateTime now, double warningRatio, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<Ticket>> GetStaleResolvedAsync(DateTime resolvedBefore, int limit, CancellationToken ct = default);
}

