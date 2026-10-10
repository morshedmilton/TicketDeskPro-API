namespace TicketDeskPro.Domain.Interfaces;

/// <summary>
/// Entities implementing this interface are never hard-deleted.
/// A global query filter hides them; a SaveChangesInterceptor converts DELETE into an UPDATE.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    Guid? DeletedById { get; set; }
}
