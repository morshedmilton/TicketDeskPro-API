namespace TicketDeskPro.Domain.Entities;

/// <summary>
/// Persistent record of login attempts (US-1.07 account lockout + forensics).
/// Redis counters do the fast rate-limiting; this table is the durable audit copy.
/// CreatedAt is the attempt time.
/// </summary>
public class LoginAttempt : BaseEntity
{
    public string Email { get; set; } = null!;
    public string? IpAddress { get; set; }
    public bool IsSuccess { get; set; }
}
