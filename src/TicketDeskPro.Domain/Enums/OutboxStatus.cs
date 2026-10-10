namespace TicketDeskPro.Domain.Enums;

/// <summary>
/// Stored as string in the database (Pending/Sent/Failed) so ops can read outbox rows directly.
/// </summary>
public enum OutboxStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2
}
