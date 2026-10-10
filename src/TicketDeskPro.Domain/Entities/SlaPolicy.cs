using TicketDeskPro.Domain.Enums;

namespace TicketDeskPro.Domain.Entities;

public class SlaPolicy : BaseEntity
{
    public string Name { get; set; } = null!;

    /// <summary>One policy per priority (unique).</summary>
    public TicketPriority Priority { get; set; }

    public int FirstResponseMinutes { get; set; }
    public int ResolutionMinutes { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<SlaTimer> SlaTimers { get; set; } = new List<SlaTimer>();
}
