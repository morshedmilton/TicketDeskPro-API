using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketDeskPro.Application.Services.Notifications;
using TicketDeskPro.Domain.Entities;
using TicketDeskPro.Domain.Enums;
using TicketDeskPro.Domain.Interfaces;

namespace TicketDeskPro.Infrastructure.BackgroundJobs;

/// <summary>
/// SLA watchdog (PRD US-5.02/5.03, BR-015/018/034/035). Every 60 seconds:
///  1. 75%-elapsed first-response warning → one-time email to the assigned agent (BR-035)
///  2. First-response breach → flag + priority escalation + breach record + alerts
///  3. Resolution breach → same (skips tickets still paused on the customer)
///  4. Auto-close tickets Resolved for over 48h (BR-018)
/// </summary>
public class SlaCheckerJob : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    public const int BatchSize = 100;
    public const double WarningRatio = 0.75;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SlaCheckerJob> _logger;

    public SlaCheckerJob(IServiceScopeFactory scopeFactory, ILogger<SlaCheckerJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SLA checker started (every {Seconds}s)", PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "SLA check pass failed");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
        }
    }

    internal async Task CheckOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var timers = sp.GetRequiredService<ISlaTimerRepository>();
        var tickets = sp.GetRequiredService<ITicketRepository>();
        var breaches = sp.GetRequiredService<ISlaBreachRepository>();
        var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
        var notifier = sp.GetRequiredService<NotificationDispatcher>();
        var now = DateTime.UtcNow;

        // 1) BR-035: 75% first-response warning, once.
        foreach (var timer in await timers.GetWarningDueAsync(now, WarningRatio, BatchSize, ct))
        {
            timer.IsWarningSent = true;
            if (timer.Ticket is not null)
                await notifier.OnSlaWarningAsync(timer.Ticket.Id, timer.Ticket.TicketNumber, timer.Ticket.AssignedToId, ct);
        }
        await unitOfWork.SaveChangesAsync(ct);

        // 2) First-response breach (BR-015/BR-034).
        //    Flags + escalation + breach row + notifications commit TOGETHER (the
        //    dispatcher's SaveChanges) — a notifier crash retries next pass.
        foreach (var timer in await timers.GetDueFirstResponseAsync(now, BatchSize, ct))
        {
            timer.IsFirstResponseBreached = true;
            await EscalateAsync(timer, SlaBreachType.FirstResponse, breaches, ct);

            if (timer.Ticket is not null)
                await notifier.OnSlaBreachedAsync(timer.Ticket.Id, timer.Ticket.TicketNumber, timer.Ticket.AssignedToId,
                    SlaBreachType.FirstResponse.ToString(), ct);
            else
                await unitOfWork.SaveChangesAsync(ct);
        }

        // 3) Resolution breach — paused timers already had their deadline pushed forward (BR-031).
        foreach (var timer in await timers.GetDueResolutionAsync(now, BatchSize, ct))
        {
            timer.IsResolutionBreached = true;
            await EscalateAsync(timer, SlaBreachType.Resolution, breaches, ct);

            if (timer.Ticket is not null)
                await notifier.OnSlaBreachedAsync(timer.Ticket.Id, timer.Ticket.TicketNumber, timer.Ticket.AssignedToId,
                    SlaBreachType.Resolution.ToString(), ct);
            else
                await unitOfWork.SaveChangesAsync(ct);
        }

        // 4) BR-018: auto-close anything Resolved for more than 48 hours.
        //    History row (system actor = null) + status event so the customer is emailed.
        var stale = await timers.GetStaleResolvedAsync(now.AddHours(-48), BatchSize, ct);
        if (stale.Count > 0)
        {
            foreach (var ticket in stale)
            {
                ticket.StatusHistory.Add(new TicketStatusHistory
                {
                    Ticket = ticket,
                    FromStatus = TicketStatus.Resolved,
                    ToStatus = TicketStatus.Closed,
                    Reason = "Auto-closed 48h after resolution",
                    ChangedById = null
                });
                ticket.Status = TicketStatus.Closed;
                ticket.AddDomainEvent(new Domain.Events.TicketStatusChangedEvent(
                    ticket.Id, ticket.TicketNumber, ticket.CreatedById, ticket.AssignedToId,
                    Guid.Empty, // system actor: no recipient equals actor, all interested parties get notified
                    nameof(TicketStatus.Resolved), nameof(TicketStatus.Closed)));
            }
            await unitOfWork.SaveChangesAsync(ct);
            _logger.LogInformation("Auto-closed {Count} resolved ticket(s) past the 48h window", stale.Count);
        }
    }

    private static async Task EscalateAsync(SlaTimer timer, SlaBreachType type, ISlaBreachRepository breaches, CancellationToken ct)
    {
        if (timer.Ticket is null) return;

        // BR-034: escalate one level, capped at Urgent.
        var current = timer.Ticket.Priority;
        var escalated = current >= TicketPriority.Urgent ? current : current + 1;
        timer.Ticket.Priority = escalated;

        await breaches.AddAsync(new SlaBreach
        {
            TicketId = timer.TicketId,
            BreachType = type,
            BreachedAt = DateTime.UtcNow,
            EscalatedToPriority = escalated
        }, ct);
    }
}
