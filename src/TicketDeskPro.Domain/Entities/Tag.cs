namespace TicketDeskPro.Domain.Entities;

public class Tag : BaseEntity
{
    public string Name { get; set; } = null!;

    public ICollection<TicketTag> TicketTags { get; set; } = new List<TicketTag>();
}
