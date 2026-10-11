using TicketDeskPro.Application.DTOs.Admin;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Domain.Interfaces;
using TicketDeskPro.Shared.Constants;
using TicketDeskPro.Shared.Exceptions;

namespace TicketDeskPro.Application.Services.Admin;

/// <summary>Searchable audit trail viewer (US-9.02 / BR-024).</summary>
public interface IAuditLogService
{
    Task<AuditLogPageDto> GetPageAsync(Guid? userId, string? entityType, string? action,
        DateTime? from, DateTime? to, int limit, string? cursor, CancellationToken ct = default);
}

public class AuditLogService : IAuditLogService
{
    private readonly IAuditLogRepository _auditLogs;

    public AuditLogService(IAuditLogRepository auditLogs) => _auditLogs = auditLogs;

    public async Task<AuditLogPageDto> GetPageAsync(Guid? userId, string? entityType, string? action,
        DateTime? from, DateTime? to, int limit, string? cursor, CancellationToken ct = default)
    {
        var page = await _auditLogs.GetPageAsync(userId, entityType, action, from, to,
            Math.Clamp(limit, 1, 100), cursor, ct);

        var items = page.Items.Select(a => new AuditLogDto(
            a.Id, a.UserId, a.User is null ? null : a.User.FirstName + " " + a.User.LastName,
            a.Action, a.EntityType, a.EntityId, a.BeforeJson, a.AfterJson, a.Timestamp)).ToList();

        return new AuditLogPageDto(items, page.NextCursor, page.HasMore);
    }
}

/// <summary>Own notification toggles (US-7.03).</summary>
public interface INotificationPreferenceService
{
    Task<NotificationPreferenceDto> GetMineAsync(CancellationToken ct = default);
    Task<NotificationPreferenceDto> UpdateMineAsync(NotificationPreferenceDto request, CancellationToken ct = default);
}

public class NotificationPreferenceService : INotificationPreferenceService
{
    private readonly IUserRepository _users;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _unitOfWork;

    public NotificationPreferenceService(IUserRepository users, ICurrentUserService currentUser, IUnitOfWork unitOfWork)
    {
        _users = users;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationPreferenceDto> GetMineAsync(CancellationToken ct = default)
    {
        var preference = await _users.GetNotificationPreferenceAsync(await RequireUserIdAsync(ct), ct);
        return preference is null
            ? new NotificationPreferenceDto(true, true, true, true, true) // defaults = entity defaults
            : ToDto(preference);
    }

    public async Task<NotificationPreferenceDto> UpdateMineAsync(NotificationPreferenceDto request, CancellationToken ct = default)
    {
        var userId = await RequireUserIdAsync(ct);
        var preference = await _users.GetNotificationPreferenceTrackedAsync(userId, ct);

        if (preference is null)
        {
            preference = new Domain.Entities.NotificationPreference { UserId = userId };
            await _users.SetNotificationPreferenceAsync(preference, ct);
        }

        preference.EmailOnTicketCreated = request.EmailOnTicketCreated;
        preference.EmailOnTicketAssigned = request.EmailOnTicketAssigned;
        preference.EmailOnStatusChanged = request.EmailOnStatusChanged;
        preference.EmailOnSlaWarning = request.EmailOnSlaWarning;
        preference.InAppEnabled = request.InAppEnabled;

        await _unitOfWork.SaveChangesAsync(ct);
        return ToDto(preference);
    }

    private async Task<Guid> RequireUserIdAsync(CancellationToken ct) =>
        _currentUser.UserId
        ?? throw new UnauthorizedException(ErrorCodes.InvalidTokenState, "Not authenticated.");

    private static NotificationPreferenceDto ToDto(Domain.Entities.NotificationPreference p) =>
        new(p.EmailOnTicketCreated, p.EmailOnTicketAssigned, p.EmailOnStatusChanged, p.EmailOnSlaWarning, p.InAppEnabled);
}
