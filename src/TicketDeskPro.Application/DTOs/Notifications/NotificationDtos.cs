using TicketDeskPro.Domain.Enums;

namespace TicketDeskPro.Application.DTOs.Notifications;

public record NotificationDto(Guid Id, NotificationType Type, string Title, string Message, string? Link, bool IsRead, DateTime CreatedAt);
