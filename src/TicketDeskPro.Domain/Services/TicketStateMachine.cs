using TicketDeskPro.Domain.Enums;

namespace TicketDeskPro.Domain.Services;

/// <summary>
/// The ticket workflow (PRD US-2.06, BR-013..BR-021). Every status change must pass
/// through here — invalid paths are rejected with a ConflictException by the service.
///
///   Open → InProgress → AwaitingCustomer → CustomerReplied → InProgress → Resolved → Closed
///   Resolved → Reopened (within 48h) → InProgress        Closed is terminal
///   Closed is also reachable from ANY state — Admin force-close only (BR-018).
/// </summary>
public static class TicketStateMachine
{
    private static readonly Dictionary<TicketStatus, TicketStatus[]> Transitions = new()
    {
        // BR-013: a ticket is created Open
        [TicketStatus.Open] = [TicketStatus.InProgress],
        // BR-014/BR-015/BR-017
        [TicketStatus.InProgress] = [TicketStatus.AwaitingCustomer, TicketStatus.Resolved],
        // BR-016
        [TicketStatus.AwaitingCustomer] = [TicketStatus.CustomerReplied],
        // customer reply brings it back to work
        [TicketStatus.CustomerReplied] = [TicketStatus.InProgress],
        // BR-018/BR-019: Resolved → Closed (auto/manual) or Reopened within 48h
        [TicketStatus.Resolved] = [TicketStatus.Closed, TicketStatus.Reopened],
        // BR-014: reopened tickets go back to work
        [TicketStatus.Reopened] = [TicketStatus.InProgress],
        // terminal
        [TicketStatus.Closed] = []
    };

    public static IReadOnlyList<TicketStatus> GetAllowedTransitions(TicketStatus from) => Transitions[from];

    public static bool CanTransition(TicketStatus from, TicketStatus to, bool isAdminForceClose) =>
        to == TicketStatus.Closed
            ? isAdminForceClose || Transitions[from].Contains(to)
            : Transitions[from].Contains(to);

    /// <summary>Resolves who may reopen a resolved ticket: BR-019 gives a 48h window.</summary>
    public static bool CanReopen(TicketStatus from, DateTime? resolvedAt, DateTime now) =>
        from == TicketStatus.Resolved &&
        resolvedAt is not null &&
        now - resolvedAt.Value <= TimeSpan.FromHours(48);
}
