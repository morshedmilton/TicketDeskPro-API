using TicketDeskPro.Domain.Enums;

namespace TicketDeskPro.Domain.Entities;

public class TicketStatusHistory : BaseEntity
{
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;

    public TicketStatus FromStatus { get; set; }
    public TicketStatus ToStatus { get; set; }
    public string? Reason { get; set; }

    /// <summary>Null for system transitions (e.g. the 48h auto-close worker).</summary>
    public Guid? ChangedById { get; set; }
    public User? ChangedBy { get; set; }

    // CreatedAt (from BaseEntity) is the moment the transition happened.
}
