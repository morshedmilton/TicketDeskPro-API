using TicketDeskPro.Domain.Enums;

namespace TicketDeskPro.Domain.Entities;

public class Ticket : BaseEntity
{
    /// <summary>Human-readable sequential id, e.g. "TKT-0001" (generated in Phase 3).</summary>
    public string TicketNumber { get; set; } = null!;

    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public TicketPriority Priority { get; set; }

    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public Guid CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;

    public Guid? AssignedToId { get; set; }
    public User? AssignedTo { get; set; }

    /// <summary>
    /// PostgreSQL xmin system column — optimistic concurrency token (replaces SQL Server ROWVERSION).
    /// Sent to clients as ETag; clients return it via If-Match on status updates.
    /// </summary>
    public uint Xmin { get; set; }

    public ICollection<TicketMessage> Messages { get; set; } = new List<TicketMessage>();
    public ICollection<TicketAttachment> Attachments { get; set; } = new List<TicketAttachment>();
    public ICollection<TicketTag> Tags { get; set; } = new List<TicketTag>();
    public ICollection<TicketStatusHistory> StatusHistory { get; set; } = new List<TicketStatusHistory>();
    public SlaTimer? SlaTimer { get; set; }
}
