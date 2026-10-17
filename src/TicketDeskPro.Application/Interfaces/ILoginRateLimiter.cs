namespace TicketDeskPro.Application.Interfaces;

/// <summary>Sliding-window limiter protecting the login endpoint (BR-026).</summary>
public interface ILoginRateLimiter
{
    /// <summary>Records a login attempt; throws RateLimitedException when the window is full.</summary>
    Task CheckAndRecordAsync(string ipAddress, string email, CancellationToken ct = default);

    /// <summary>Records an attempt in a named bucket ("login", "forgot", "register") so
    /// different auth actions get independent windows per email. Throws RateLimitedException
    /// when that bucket's window is full.</summary>
    Task CheckAsync(string bucket, string email, CancellationToken ct = default);

    /// <summary>Clears the window for this identity after a successful login (FRD §5: "Reset Counter").</summary>
    Task ResetAsync(string ipAddress, string email, CancellationToken ct = default);
}
