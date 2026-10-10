namespace TicketDeskPro.Domain.Interfaces;

/// <summary>
/// Automatic created/updated bookkeeping, maintained by AppDbContext.SaveChangesAsync.
/// </summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}
