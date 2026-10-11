using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Domain.Events;

namespace TicketDeskPro.Infrastructure.Services;

/// <summary>
/// Fans committed domain events out to their registered IEventHandler&lt;T&gt; instances
/// (FRD §7.7). Invoked by AppDbContext AFTER SaveChanges succeeds.
/// Handlers are OBSERVATIONAL: a failing handler is logged and skipped — it must
/// never turn an already-committed business change into an HTTP 500 (which would
/// make clients retry and duplicate work).
/// </summary>
public class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DomainEventDispatcher> _logger;

    public DomainEventDispatcher(IServiceProvider serviceProvider, ILogger<DomainEventDispatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken ct = default)
    {
        foreach (var domainEvent in events)
        {
            var handlerType = typeof(IEventHandler<>).MakeGenericType(domainEvent.GetType());
            foreach (var handler in _serviceProvider.GetServices(handlerType))
            {
                try
                {
                    var task = (Task?)handlerType.GetMethod(nameof(IEventHandler<IDomainEvent>.HandleAsync))!
                        .Invoke(handler, [domainEvent, ct]);
                    if (task is not null)
                        await task;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Domain event handler {Handler} failed for {Event}",
                        handler.GetType().Name, domainEvent.GetType().Name);
                }
            }
        }
    }
}
