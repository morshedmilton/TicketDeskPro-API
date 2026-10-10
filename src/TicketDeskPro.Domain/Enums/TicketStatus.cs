namespace TicketDeskPro.Domain.Enums;

/// <summary>
/// Ticket lifecycle states, enforced by the state machine (Phase 3).
/// Values follow the PRD business rules BR-013..BR-019, which define a richer
/// workflow than the 4-state summary in the database design doc.
/// </summary>
public enum TicketStatus
{
    Open = 0,
    InProgress = 1,
    AwaitingCustomer = 2,
    CustomerReplied = 3,
    Resolved = 4,
    Closed = 5,
    Reopened = 6
}
