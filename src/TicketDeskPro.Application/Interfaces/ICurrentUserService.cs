namespace TicketDeskPro.Application.Interfaces;

/// <summary>Who is calling right now (from the JWT on the request).</summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
}
