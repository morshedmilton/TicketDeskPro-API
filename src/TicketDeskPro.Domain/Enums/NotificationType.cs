namespace TicketDeskPro.Domain.Enums;

public enum NotificationType
{
    General = 0,
    TicketCreated = 1,
    TicketAssigned = 2,
    TicketStatusChanged = 3,
    TicketNewMessage = 4,
    SlaWarning = 5,
    SlaBreached = 6
}
