namespace TicketDeskPro.Domain.Entities;

/// <summary>
/// Refresh token rotation with family tracking (FRD §5).
/// Only the SHA256 hash is stored; if a revoked token is presented again,
/// its whole FamilyId is invalidated (token reuse = theft).
/// </summary>
public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string TokenHash { get; set; } = null!;

    /// <summary>All tokens descending from the same login session.</summary>
    public Guid FamilyId { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    /// <summary>Hash of the token that replaced this one after rotation.</summary>
    public string? ReplacedByTokenHash { get; set; }

    public string? CreatedByIp { get; set; }

    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}
