using TicketDeskPro.Domain.Enums;

namespace TicketDeskPro.Domain.Entities;

/// <summary>
/// Permanent record that an SLA deadline passed (US-5.03 / BR-015).
/// One row per breach type per ticket — the checker flips flags on SlaTimer
/// and writes this row so breach history survives even if the timer is reset.
/// </summary>
public class SlaBreach : BaseEntity
{
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;

    /// <summary>FirstResponse or Resolution.</summary>
    public SlaBreachType BreachType { get; set; }

    public DateTime BreachedAt { get; set; }

    /// <summary>Priority after the automatic escalation (BR-034).</summary>
    public TicketPriority EscalatedToPriority { get; set; }
}
