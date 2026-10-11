using Microsoft.AspNetCore.SignalR;
using TicketDeskPro.Api.Hubs;
using TicketDeskPro.Application.Interfaces;

namespace TicketDeskPro.Api.Services;

/// <summary>
/// IRealtimeNotifier backed by SignalR (FRD §8). Lives in the Api project because
/// IHubContext needs the concrete hub type; Application/Infrastructure only see
/// the interface. Payloads are small plain objects — the frontend names the events.
/// </summary>
public class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<NotificationHub> _hub;

    public SignalRRealtimeNotifier(IHubContext<NotificationHub> hub) => _hub = hub;

    public Task TicketCreatedAsync(Guid ticketId, string ticketNumber, Guid? assignedToId, CancellationToken ct = default)
    {
        var payload = new { ticketId, ticketNumber, assignedToId };
        var tasks = new List<Task>
        {
            _hub.Clients.Group("role:agents").SendAsync("TicketCreated", payload, ct)
        };
        if (assignedToId is { } id)
            tasks.Add(_hub.Clients.Group($"user:{id}").SendAsync("TicketAssigned", payload, ct));
        return Task.WhenAll(tasks);
    }

    public Task TicketAssignedAsync(Guid ticketId, string ticketNumber, Guid assignedToId, Guid previousAssigneeId, CancellationToken ct = default)
    {
        var payload = new { ticketId, ticketNumber, assignedToId, previousAssigneeId };
        return Task.WhenAll(
            _hub.Clients.Group("role:agents").SendAsync("TicketAssigned", payload, ct),
            _hub.Clients.Group($"user:{assignedToId}").SendAsync("TicketAssigned", payload, ct));
    }

    public Task TicketStatusChangedAsync(Guid ticketId, string ticketNumber, string fromStatus, string toStatus, CancellationToken ct = default) =>
        _hub.Clients.Group("role:agents").SendAsync("TicketStatusChanged",
            new { ticketId, ticketNumber, fromStatus, toStatus }, ct);

    public Task NewMessageAsync(Guid ticketId, string ticketNumber, Guid authorId, bool isInternal, CancellationToken ct = default) =>
        _hub.Clients.Group("role:agents").SendAsync("NewMessage",
            new { ticketId, ticketNumber, authorId, isInternal }, ct);

    public Task SlaBreachedAsync(Guid ticketId, string ticketNumber, string breachType, CancellationToken ct = default) =>
        _hub.Clients.Groups("role:agents").SendAsync("SlaBreached",
            new { ticketId, ticketNumber, breachType }, ct);
}
