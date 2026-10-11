using TicketDeskPro.Domain.Events;
using TicketDeskPro.Domain.Interfaces;

namespace TicketDeskPro.Domain.Entities;

public abstract class BaseEntity : IAuditable, ISoftDeletable
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }

    /// <summary>Domain events raised on this entity (FRD §7.7). Dispatched by
    /// AppDbContext AFTER the transaction commits; never stored in the database.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
