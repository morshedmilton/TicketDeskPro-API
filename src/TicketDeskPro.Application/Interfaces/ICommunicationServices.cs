namespace TicketDeskPro.Application.Interfaces;

/// <summary>Outgoing email (backed by SMTP; rows usually go through the outbox first).</summary>
public interface IEmailService
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}

/// <summary>
/// Real-time push abstraction (SignalR in production, no-op in tests).
/// Group vocabulary: "user:{id}" for a person, "role:agents" for the whole queue.
/// </summary>
public interface IRealtimeNotifier
{
    Task TicketCreatedAsync(Guid ticketId, string ticketNumber, Guid? assignedToId, CancellationToken ct = default);
    Task TicketAssignedAsync(Guid ticketId, string ticketNumber, Guid assignedToId, Guid previousAssigneeId, CancellationToken ct = default);
    Task TicketStatusChangedAsync(Guid ticketId, string ticketNumber, string fromStatus, string toStatus, CancellationToken ct = default);
    Task NewMessageAsync(Guid ticketId, string ticketNumber, Guid authorId, bool isInternal, CancellationToken ct = default);
    Task SlaBreachedAsync(Guid ticketId, string ticketNumber, string breachType, CancellationToken ct = default);
}
