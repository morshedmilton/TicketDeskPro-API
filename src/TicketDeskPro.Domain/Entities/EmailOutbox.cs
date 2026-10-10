using TicketDeskPro.Domain.Enums;

namespace TicketDeskPro.Domain.Entities;

/// <summary>
/// Outbox pattern (BR-040): emails are written here in the same transaction as the business
/// change, then a background worker (Phase 4) sends them with retries.
/// </summary>
public class EmailOutbox : BaseEntity
{
    public string ToEmail { get; set; } = null!;
    public string Subject { get; set; } = null!;
    public string Body { get; set; } = null!;

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime? SentAt { get; set; }
}
