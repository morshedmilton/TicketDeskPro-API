using TicketDeskPro.Domain.Enums;

namespace TicketDeskPro.Domain.Entities;

public class Notification : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public NotificationType Type { get; set; }
    public string Title { get; set; } = null!;
    public string Message { get; set; } = null!;

    /// <summary>Deep link, e.g. "/tickets/TKT-0001".</summary>
    public string? Link { get; set; }

    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
}
