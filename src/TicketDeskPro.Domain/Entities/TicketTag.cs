namespace TicketDeskPro.Domain.Entities;

/// <summary>Join table between tickets and tags.</summary>
public class TicketTag
{
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
