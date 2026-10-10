using TicketDeskPro.Domain.Enums;

namespace TicketDeskPro.Domain.Entities;

public class TicketStatusHistory : BaseEntity
{
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;

    public TicketStatus FromStatus { get; set; }
    public TicketStatus ToStatus { get; set; }
    public string? Reason { get; set; }

    public Guid ChangedById { get; set; }
    public User ChangedBy { get; set; } = null!;

    // CreatedAt (from BaseEntity) is the moment the transition happened.
}
