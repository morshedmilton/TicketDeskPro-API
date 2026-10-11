using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketDeskPro.Api.Filters;
using TicketDeskPro.Application.DTOs.Dashboard;
using TicketDeskPro.Application.Services;
using TicketDeskPro.Shared.Constants;
using TicketDeskPro.Shared.Wrappers;

namespace TicketDeskPro.Api.Controllers.V1;

/// <summary>Role dashboards (US-8.01/8.02). Results are Redis-cached (30s TTL,
/// invalidated on every ticket mutation).</summary>
[ApiController]
[Authorize]
[Route("api/v1/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboard;

    public DashboardController(IDashboardService dashboard) => _dashboard = dashboard;

    [HttpGet("kpis")]
    [RequirePermission(AppPermissions.DashboardView)]
    public async Task<IActionResult> Kpis(CancellationToken ct) =>
        Ok(ApiResponse<DashboardKpisDto>.Ok(await _dashboard.GetKpisAsync(ct)));

    [HttpGet("ticket-trends")]
    [RequirePermission(AppPermissions.DashboardView)]
    public async Task<IActionResult> Trends([FromQuery] int days = 30, CancellationToken ct = default) =>
        Ok(ApiResponse<IReadOnlyList<TicketTrendPointDto>>.Ok(await _dashboard.GetTrendsAsync(days, ct)));

    [HttpGet("category-distribution")]
    [RequirePermission(AppPermissions.DashboardView)]
    public async Task<IActionResult> CategoryDistribution(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<CategoryDistributionPointDto>>.Ok(await _dashboard.GetCategoryDistributionAsync(ct)));

    [HttpGet("agent-performance")]
    [RequirePermission(AppPermissions.SlaRead)] // managers watch the leaderboard
    public async Task<IActionResult> AgentPerformance(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<AgentPerformanceDto>>.Ok(await _dashboard.GetAgentPerformanceAsync(ct)));
}
