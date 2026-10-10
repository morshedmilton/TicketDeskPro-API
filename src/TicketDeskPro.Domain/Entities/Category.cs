namespace TicketDeskPro.Domain.Entities;

public class Category : BaseEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>Inactive categories are hidden from the create-ticket form but kept for old tickets.</summary>
    public bool IsActive { get; set; } = true;

    public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
}
