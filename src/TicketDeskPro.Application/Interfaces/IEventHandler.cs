using TicketDeskPro.Domain.Events;

namespace TicketDeskPro.Application.Interfaces;

/// <summary>Handles one domain event type (FRD §7.7). Registered in DI; the
/// dispatcher fans each committed event out to all matching handlers.</summary>
public interface IEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct = default);
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken ct = default);
}
