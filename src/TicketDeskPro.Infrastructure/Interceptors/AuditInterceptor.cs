using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Domain.Entities;

namespace TicketDeskPro.Infrastructure.Interceptors;

/// <summary>
/// FRD §7.3 / BR-023: every Create/Update/Delete is captured into AuditLogs with a
/// before/after JSON snapshot and the acting user. Runs inside the same SaveChanges
/// as the business change — the audit row commits atomically with what it describes.
/// Skips AuditLog rows themselves (they are append-only and would recurse).
/// </summary>
public class AuditInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUser;

    public AuditInterceptor(ICurrentUserService currentUser) => _currentUser = currentUser;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>High-churn, low-business-value entities that would only bloat the audit trail
    /// (every login/refresh/email-poll writes one). Business entities stay audited.</summary>
    private static readonly Type[] NotAuditedEntityTypes =
    [
        typeof(AuditLog), typeof(LoginAttempt), typeof(RefreshToken), typeof(EmailOutbox), typeof(Notification)
    ];

    private void Collect(DbContext? context)
    {
        if (context is null) return;

        // No authenticated actor = seeding/system writes — not a user action worth auditing.
        if (_currentUser.UserId is null) return;

        // Materialize first — adding AuditLog rows mutates the change tracker we are enumerating.
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                        && !NotAuditedEntityTypes.Contains(e.Entity.GetType()))
            .ToList();

        foreach (var entry in entries)
        {
            var action = ResolveAction(entry);

            context.Set<AuditLog>().Add(new AuditLog
            {
                UserId = _currentUser.UserId,
                Action = action,
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = entry.Metadata.FindPrimaryKey() is { } key
                    ? string.Join(",", key.Properties.Select(p => entry.Properties.First(p2 => p2.Metadata.Name == p.Name).CurrentValue))
                    : "?",
                BeforeJson = action == "Create" ? null : Snapshot(entry.OriginalValues),
                AfterJson = action == "Delete" ? null : Snapshot(entry.CurrentValues),
                Timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>A real DELETE never reaches here (soft-delete interceptor converts it first),
    /// so a delete shows up as Modified with IsDeleted false→true.</summary>
    private static string ResolveAction(EntityEntry entry) => entry.State switch
    {
        EntityState.Added => "Create",
        EntityState.Deleted => "Delete",
        _ => entry.Properties.Any(p => p.Metadata.Name == "IsDeleted" && (bool?)p.OriginalValue == false && (bool?)p.CurrentValue == true)
            ? "Delete"
            : "Update"
    };

    /// <summary>Credential material must never enter the (indefinitely retained) audit trail.</summary>
    private static readonly string[] SensitiveProperties =
        ["PasswordHash", "TokenHash", "PasswordResetTokenHash", "ReplacedByTokenHash"];

    private static string? Snapshot(PropertyValues values)
    {
        var dict = values.Properties
            .Where(p => p.Name is not ("RowVersion" or "Xmin") && !SensitiveProperties.Contains(p.Name))
            .ToDictionary(p => p.Name, p => values[p.Name]?.ToString());
        return JsonSerializer.Serialize(dict);
    }
}
