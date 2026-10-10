namespace TicketDeskPro.Domain.Entities;

/// <summary>
/// Append-only audit trail (BR-023, BR-036). Never updated, never deleted — so it does
/// NOT inherit BaseEntity (no UpdatedAt/soft-delete fields that would be meaningless here).
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Null for anonymous/system actions.</summary>
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Create / Update / Delete.</summary>
    public string Action { get; set; } = null!;

    public string EntityType { get; set; } = null!;

    /// <summary>Entity id stored as string — ids are Guids but this keeps the table generic.</summary>
    public string EntityId { get; set; } = null!;

    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
