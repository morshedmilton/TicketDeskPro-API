using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Domain.Entities;
using TicketDeskPro.Domain.Enums;
using TicketDeskPro.Domain.Events;
using TicketDeskPro.Domain.Interfaces;
using TicketDeskPro.Shared.Constants;

namespace TicketDeskPro.Application.Services.Notifications;

/// <summary>
/// One handler per domain event (FRD §7.7): writes in-app notification rows,
/// queues customer/agent emails through the outbox (BR-040 — same transaction
/// as the business change, because the dispatcher runs right after the commit
/// inside the same scoped DbContext), and pushes real-time SignalR frames.
/// Respects each user's NotificationPreference toggles.
/// </summary>
public class NotificationDispatcher :
    IEventHandler<TicketCreatedEvent>,
    IEventHandler<TicketStatusChangedEvent>,
    IEventHandler<TicketAssignedEvent>,
    IEventHandler<TicketMessageAddedEvent>
{
    private readonly IUserRepository _users;
    private readonly INotificationRepository _notifications;
    private readonly IEmailOutboxRepository _outbox;
    private readonly IRealtimeNotifier _realtime;
    private readonly IUnitOfWork _unitOfWork;

    public NotificationDispatcher(
        IUserRepository users,
        INotificationRepository notifications,
        IEmailOutboxRepository outbox,
        IRealtimeNotifier realtime,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _notifications = notifications;
        _outbox = outbox;
        _realtime = realtime;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(TicketCreatedEvent e, CancellationToken ct = default)
    {
        if (e.AssignedToId is { } assigneeId)
        {
            await NotifyAsync(assigneeId, NotificationType.TicketAssigned,
                $"Ticket {e.TicketNumber} assigned to you", "A new ticket is now in your queue.", $"/tickets/{e.TicketNumber}", ct);
        }

        var creator = await _users.GetByIdWithRolesAsync(e.CreatedById, ct);
        if (creator is not null && await WantsEmailAsync(creator.Id, p => p.EmailOnTicketCreated, ct))
        {
            await QueueEmailAsync(creator.Email,
                $"[TicketDesk] We received your ticket {e.TicketNumber}",
                $"<p>Hi {creator.FirstName},</p><p>We received your request <b>{e.TicketNumber}</b> and will reply as soon as possible.</p>",
                ct);
        }

        await _realtime.TicketCreatedAsync(e.TicketId, e.TicketNumber, e.AssignedToId, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task HandleAsync(TicketStatusChangedEvent e, CancellationToken ct = default)
    {
        // Nobody gets notified about their own action (ActorId = who changed it).
        if (e.CreatedById != e.ActorId)
            await NotifyAsync(e.CreatedById, NotificationType.TicketStatusChanged,
                $"Ticket {e.TicketNumber} is now {e.ToStatus}", $"Status changed from {e.FromStatus} to {e.ToStatus}.", $"/tickets/{e.TicketNumber}", ct);

        if (e.AssignedToId is { } assigneeId && assigneeId != e.ActorId)
            await NotifyAsync(assigneeId, NotificationType.TicketStatusChanged,
                $"Ticket {e.TicketNumber} is now {e.ToStatus}", $"Status changed from {e.FromStatus} to {e.ToStatus}.", $"/tickets/{e.TicketNumber}", ct);

        // Customers get an email on the milestones that matter (BR-018).
        if (e.ToStatus is nameof(TicketStatus.Resolved) or nameof(TicketStatus.Closed))
        {
            var creator = await _users.GetByIdWithRolesAsync(e.CreatedById, ct);
            if (creator is not null && await WantsEmailAsync(creator.Id, p => p.EmailOnStatusChanged, ct))
            {
                await QueueEmailAsync(creator.Email,
                    $"[TicketDesk] Ticket {e.TicketNumber} {e.ToStatus.ToLowerInvariant()}",
                    $"<p>Hi {creator.FirstName},</p><p>Your ticket <b>{e.TicketNumber}</b> is now <b>{e.ToStatus}</b>.</p>",
                    ct);
            }
        }

        await _realtime.TicketStatusChangedAsync(e.TicketId, e.TicketNumber, e.FromStatus, e.ToStatus, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task HandleAsync(TicketAssignedEvent e, CancellationToken ct = default)
    {
        await NotifyAsync(e.AssignedToId, NotificationType.TicketAssigned,
            $"Ticket {e.TicketNumber} assigned to you", "You are now the owner of this ticket.", $"/tickets/{e.TicketNumber}", ct);

        if (e.PreviousAssigneeId is { } previous && previous != e.AssignedToId)
        {
            await NotifyAsync(previous, NotificationType.TicketStatusChanged,
                $"Ticket {e.TicketNumber} reassigned", "This ticket was reassigned to another agent.", $"/tickets/{e.TicketNumber}", ct);
        }

        await _realtime.TicketAssignedAsync(e.TicketId, e.TicketNumber, e.AssignedToId, e.PreviousAssigneeId ?? Guid.Empty, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task HandleAsync(TicketMessageAddedEvent e, CancellationToken ct = default)
    {
        // Internal notes notify agents; public replies may come from the customer,
        // so only the assignee (or the agent pool when unassigned) is pinged.
        var recipients = new HashSet<Guid>();
        if (e.AssignedToId is { } assigneeId && assigneeId != e.MessageAuthorId)
            recipients.Add(assigneeId);
        else if (e.AssignedToId is null)
        {
            // Unassigned: ping the whole active agent pool so someone picks it up.
            foreach (var agent in await _users.GetActiveAgentsByLoadAsync(ct: ct))
                recipients.Add(agent.Id);
        }

        foreach (var recipient in recipients)
        {
            await NotifyAsync(recipient, NotificationType.TicketNewMessage,
                $"New message on {e.TicketNumber}", e.IsInternal ? "An internal note was added." : "A new reply was posted.", $"/tickets/{e.TicketNumber}", ct);
        }

        await _realtime.NewMessageAsync(e.TicketId, e.TicketNumber, e.MessageAuthorId, e.IsInternal, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Called directly by the SLA checker worker (breach is detected, not raised as an entity event).</summary>
    public async Task OnSlaBreachedAsync(Guid ticketId, string ticketNumber, Guid? assignedToId, string breachType, CancellationToken ct = default)
    {
        if (assignedToId is { } assigneeId)
        {
            await NotifyAsync(assigneeId, NotificationType.SlaBreached,
                $"SLA breached on {ticketNumber}", $"{breachType} deadline has passed.", $"/tickets/{ticketNumber}", ct);

            var assignee = await _users.GetByIdWithRolesAsync(assigneeId, ct);
            if (assignee is not null && await WantsEmailAsync(assignee.Id, p => p.EmailOnSlaWarning, ct))
            {
                await QueueEmailAsync(assignee.Email,
                    $"[TicketDesk][URGENT] SLA breached on {ticketNumber}",
                    $"<p>The <b>{breachType}</b> SLA deadline has passed on ticket <b>{ticketNumber}</b>. Please act immediately.</p>",
                    ct);
            }
        }

        foreach (var admin in await _users.GetActiveByRoleAsync(AppRoles.Admin, ct))
        {
            await NotifyAsync(admin.Id, NotificationType.SlaBreached,
                $"SLA breached on {ticketNumber}", $"{breachType} deadline has passed.", $"/tickets/{ticketNumber}", ct);

            // US-5.03: management also gets an email, independent of the agent's toggles.
            await QueueEmailAsync(admin.Email,
                $"[TicketDesk][URGENT] SLA breached on {ticketNumber}",
                $"<p>The <b>{breachType}</b> SLA deadline has passed on ticket <b>{ticketNumber}</b>. Priority was escalated automatically.</p>",
                ct);
        }

        await _realtime.SlaBreachedAsync(ticketId, ticketNumber, breachType, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>BR-035: one-time 75%-elapsed nudge to the assigned agent (in-app + email).</summary>
    public async Task OnSlaWarningAsync(Guid ticketId, string ticketNumber, Guid? assignedToId, CancellationToken ct = default)
    {
        if (assignedToId is not { } assigneeId) return;

        await NotifyAsync(assigneeId, NotificationType.SlaWarning,
            $"SLA warning on {ticketNumber}", "75% of the first-response window has elapsed.", $"/tickets/{ticketNumber}", ct);

        var assignee = await _users.GetByIdWithRolesAsync(assigneeId, ct);
        if (assignee is not null && await WantsEmailAsync(assignee.Id, p => p.EmailOnSlaWarning, ct))
        {
            await QueueEmailAsync(assignee.Email,
                $"[TicketDesk] SLA warning on {ticketNumber}",
                $"<p>75% of the first-response SLA window for ticket <b>{ticketNumber}</b> has elapsed. Please reply soon to avoid a breach.</p>",
                ct);
        }
    }

    private async Task NotifyAsync(Guid userId, NotificationType type, string title, string message, string? link, CancellationToken ct)
    {
        if (!await WantsInAppAsync(userId, ct)) return;

        await _notifications.AddAsync(new Notification
        {
            UserId = userId,
            Type = type,
            Title = title,
            Message = message,
            Link = link
        }, ct);
    }

    private Task QueueEmailAsync(string toEmail, string subject, string html, CancellationToken ct) =>
        _outbox.AddAsync(new EmailOutbox
        {
            ToEmail = toEmail,
            Subject = subject,
            Body = html
        }, ct);

    private async Task<bool> WantsEmailAsync(Guid userId, Func<Domain.Entities.NotificationPreference, bool> toggle, CancellationToken ct)
    {
        var preference = await _users.GetNotificationPreferenceAsync(userId, ct);
        return preference is null || toggle(preference);
    }

    private async Task<bool> WantsInAppAsync(Guid userId, CancellationToken ct)
    {
        var preference = await _users.GetNotificationPreferenceAsync(userId, ct);
        return preference is null || preference.InAppEnabled;
    }
}
