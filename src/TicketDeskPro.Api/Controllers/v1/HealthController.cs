using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using TicketDeskPro.Infrastructure.Data;

namespace TicketDeskPro.Api.Controllers.V1;

/// <summary>
/// Liveness vs readiness, the distinction Render/K8s care about:
/// /healthz = "the process is up" (never touches dependencies).
/// /readyz  = "dependencies answer" (DB + Redis) — 503 when not ready.
/// </summary>
[ApiController]
// "api/v1/health" (not "api/v1"): the documented probe paths are
// /api/v1/health/healthz and /api/v1/health/readyz — Render's health check,
// UptimeRobot and the README all use them; "api/v1" 404'd and timed out deploys.
[Route("api/v1/health")]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;

    public HealthController(AppDbContext db, IConnectionMultiplexer redis)
    {
        _db = db;
        _redis = redis;
    }

    [HttpGet("healthz")]
    public IActionResult Liveness() =>
        Ok(new { status = "healthy", time = DateTime.UtcNow });

    [HttpGet("readyz")]
    public async Task<IActionResult> Readiness(CancellationToken ct)
    {
        var checks = new Dictionary<string, string>();

        try
        {
            checks["postgres"] = await _db.Database.CanConnectAsync(ct) ? "ok" : "unreachable";
        }
        catch
        {
            checks["postgres"] = "unreachable";
        }

        try
        {
            checks["redis"] = _redis.IsConnected ? "ok" : "unreachable";
        }
        catch
        {
            checks["redis"] = "unreachable";
        }

        var ready = checks.Values.All(v => v == "ok");
        return ready
            ? Ok(new { status = "ready", checks })
            : StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "degraded", checks });
    }
}
