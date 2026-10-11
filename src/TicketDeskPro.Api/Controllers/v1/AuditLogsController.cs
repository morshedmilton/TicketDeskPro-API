using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketDeskPro.Api.Filters;
using TicketDeskPro.Application.DTOs.Admin;
using TicketDeskPro.Application.Services.Admin;
using TicketDeskPro.Shared.Constants;
using TicketDeskPro.Shared.Wrappers;

namespace TicketDeskPro.Api.Controllers.V1;

/// <summary>Searchable audit trail (US-9.02 / BR-024). Append-only — no write endpoints exist.</summary>
[ApiController]
[Authorize]
[Route("api/v1/audit-logs")]
public class AuditLogsController : ControllerBase
{
    private readonly IAuditLogService _auditLogs;

    public AuditLogsController(IAuditLogService auditLogs) => _auditLogs = auditLogs;

    [HttpGet]
    [RequirePermission(AppPermissions.AuditRead)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? userId,
        [FromQuery] string? entityType,
        [FromQuery] string? action,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken ct = default) =>
        Ok(ApiResponse<AuditLogPageDto>.Ok(await _auditLogs.GetPageAsync(userId, entityType, action, from, to, limit, cursor, ct)));
}
