using TicketDeskPro.Domain.Interfaces;

namespace TicketDeskPro.Domain.Entities;

// ponytail: Domain events (IDomainEvent collection on BaseEntity) are deferred to Phase 4,
// where the SLA engine and notification workers actually need them.
public abstract class BaseEntity : IAuditable, ISoftDeletable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }
}
