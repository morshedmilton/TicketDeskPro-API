namespace TicketDeskPro.Domain.Entities;

/// <summary>
/// One timer per ticket, created together with the ticket (US-5.02).
/// ponytail: paused-duration tracking (AwaitingCustomer pauses, BR-031/032) is deferred to
/// Phase 4 — the SLA checker job will add PausedAt/ResumedAt bookkeeping then.
/// </summary>
public class SlaTimer : BaseEntity
{
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;

    public Guid SlaPolicyId { get; set; }
    public SlaPolicy SlaPolicy { get; set; } = null!;

    public DateTime FirstResponseDeadline { get; set; }
    public DateTime ResolutionDeadline { get; set; }

    public bool IsFirstResponseBreached { get; set; }
    public bool IsResolutionBreached { get; set; }

    /// <summary>Set when the first public agent reply is posted (BR-033).</summary>
    public DateTime? FirstResponseAt { get; set; }
}
