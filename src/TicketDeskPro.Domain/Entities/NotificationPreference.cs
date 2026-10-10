namespace TicketDeskPro.Domain.Entities;

/// <summary>
/// Per-user toggles (US-7.03). ponytail: if preference types multiply, swap the boolean
/// columns for a JSONB document keyed by NotificationType.
/// </summary>
public class NotificationPreference : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public bool EmailOnTicketCreated { get; set; } = true;
    public bool EmailOnTicketAssigned { get; set; } = true;
    public bool EmailOnStatusChanged { get; set; } = true;
    public bool EmailOnSlaWarning { get; set; } = true;
    public bool InAppEnabled { get; set; } = true;
}
