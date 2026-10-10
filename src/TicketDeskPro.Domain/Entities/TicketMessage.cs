namespace TicketDeskPro.Domain.Entities;

public class TicketMessage : BaseEntity
{
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;

    public string Content { get; set; } = null!;

    /// <summary>Internal agent notes are hidden from the Customer role (BR-023).</summary>
    public bool IsInternal { get; set; }

    public Guid CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;
}
