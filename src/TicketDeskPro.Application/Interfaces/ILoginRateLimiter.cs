namespace TicketDeskPro.Application.Interfaces;

/// <summary>Sliding-window limiter protecting the login endpoint (BR-026).</summary>
public interface ILoginRateLimiter
{
    /// <summary>Records a login attempt; throws RateLimitedException when the window is full.</summary>
    Task CheckAndRecordAsync(string ipAddress, string email, CancellationToken ct = default);

    /// <summary>Clears the window for this identity after a successful login (FRD §5: "Reset Counter").</summary>
    Task ResetAsync(string ipAddress, string email, CancellationToken ct = default);
}
