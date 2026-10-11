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

    /// <summary>
    /// Non-null while the ticket waits on the customer (BR-031). On resume the pause
    /// length is pushed onto ResolutionDeadline (BR-032) and this clears.
    /// </summary>
    public DateTime? PausedAt { get; set; }

    /// <summary>BR-035: one-time 75%-elapsed warning email to the assigned agent.</summary>
    public bool IsWarningSent { get; set; }

    /// <summary>Live = not paused; deadline counting continues (first response never pauses).</summary>
    public bool IsResolutionPaused => PausedAt is not null;

    public ICollection<SlaBreach> Breaches { get; set; } = new List<SlaBreach>();
}
