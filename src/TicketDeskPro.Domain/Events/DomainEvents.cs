namespace TicketDeskPro.Domain.Events;

/// <summary>
/// Domain events (FRD §7.7): something happened inside the domain that outside
/// world cares about — email, in-app notifications, real-time pushes. Raised on
/// entities, dispatched by AppDbContext right AFTER the transaction commits, so
/// handlers can safely assume the data is durable.
/// </summary>
public interface IDomainEvent
{
    DateTime OccurredAt { get; }
}

public record TicketCreatedEvent(Guid TicketId, string TicketNumber, Guid CreatedById, Guid? AssignedToId) : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public record TicketStatusChangedEvent(Guid TicketId, string TicketNumber, Guid CreatedById, Guid? AssignedToId, Guid ActorId, string FromStatus, string ToStatus) : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public record TicketAssignedEvent(Guid TicketId, string TicketNumber, Guid AssignedToId, Guid? PreviousAssigneeId) : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public record TicketMessageAddedEvent(Guid TicketId, string TicketNumber, Guid CreatedById, Guid? AssignedToId, Guid MessageAuthorId, bool IsInternal) : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
