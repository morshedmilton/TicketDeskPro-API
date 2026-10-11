using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Domain.Entities;
using TicketDeskPro.Domain.Enums;
using TicketDeskPro.Domain.Interfaces;

namespace TicketDeskPro.Infrastructure.BackgroundJobs;

/// <summary>
/// Outbox pattern worker (FRD §7.4): every 10 seconds pick due Pending rows and
/// send them via SMTP. Failures retry with exponential backoff (2^attempts minutes,
/// capped) until MaxAttempts, then park as Failed. Old rows are pruned per the
/// DB design retention rules (sent 7d, failed 30d).
/// </summary>
public class OutboxProcessorJob : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    public const int BatchSize = 50;
    public const int MaxAttempts = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OutboxProcessorJob> _logger;

    public OutboxProcessorJob(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<OutboxProcessorJob> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox processor started (every {Seconds}s, batch {Batch})", PollInterval.TotalSeconds, BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Outbox processing pass failed");
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

    internal async Task ProcessOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IEmailOutboxRepository>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var now = DateTime.UtcNow;
        await PruneAsync(scope.ServiceProvider, ct); // retention runs even on idle passes

        var due = await outbox.GetDuePendingAsync(now, BatchSize, ct);
        if (due.Count == 0) return;

        // Dev convenience: no SMTP host configured → park without burning attempts.
        if (string.IsNullOrWhiteSpace(_configuration.GetSection("SmtpSettings")?["Host"]))
        {
            _logger.LogWarning("SmtpSettings:Host not configured — {Count} outbox email(s) held as Pending", due.Count);
            return;
        }

        foreach (var row in due)
        {
            try
            {
                await email.SendAsync(row.ToEmail, row.Subject, row.Body, ct);
                row.Status = OutboxStatus.Sent;
                row.SentAt = DateTime.UtcNow;
                row.Attempts++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                row.Attempts++;
                row.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                if (row.Attempts >= MaxAttempts)
                {
                    row.Status = OutboxStatus.Failed;
                    _logger.LogError(ex, "Outbox email to {To} failed permanently after {Attempts} attempts", row.ToEmail, row.Attempts);
                }
                else
                {
                    // exponential backoff: 2, 4 minutes (then give up on the 3rd).
                    row.NextAttemptAt = DateTime.UtcNow.AddMinutes(Math.Pow(2, row.Attempts));
                }
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    private static async Task PruneAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<Data.AppDbContext>();
        var now = DateTime.UtcNow;

        var stale = await db.EmailOutbox
            .Where(o => (o.Status == OutboxStatus.Sent && o.SentAt < now.AddDays(-7))
                        || (o.Status == OutboxStatus.Failed && o.CreatedAt < now.AddDays(-30)))
            .ToListAsync(ct);

        if (stale.Count > 0)
        {
            db.EmailOutbox.RemoveRange(stale); // hard delete: retention rules in DB design §4
            await db.SaveChangesAsync(ct);
        }
    }
}
