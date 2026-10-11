using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketDeskPro.Api.Filters;
using TicketDeskPro.Application.DTOs.SlaPolicies;
using TicketDeskPro.Application.Services;
using TicketDeskPro.Shared.Constants;
using TicketDeskPro.Shared.Wrappers;

namespace TicketDeskPro.Api.Controllers.V1;

/// <summary>Admin CRUD for SLA policies (US-5.01). Existing tickets keep the
/// policy snapshot they were created with.</summary>
[ApiController]
[Authorize]
[Route("api/v1/admin/sla-policies")]
public class AdminSlaPoliciesController : ControllerBase
{
    private readonly ISlaPolicyService _policies;

    public AdminSlaPoliciesController(ISlaPolicyService policies) => _policies = policies;

    [HttpGet]
    [RequirePermission(AppPermissions.SlaRead)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<SlaPolicyDto>>.Ok(await _policies.GetAllAsync(ct)));

    [HttpPost]
    [RequirePermission(AppPermissions.SlaManage)]
    public async Task<IActionResult> Create(SlaPolicyCreateRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, ApiResponse<SlaPolicyDto>.Ok(await _policies.CreateAsync(request, ct)));

    [HttpPut("{id:guid}")]
    [RequirePermission(AppPermissions.SlaManage)]
    public async Task<IActionResult> Update(Guid id, SlaPolicyUpdateRequest request, CancellationToken ct) =>
        Ok(ApiResponse<SlaPolicyDto>.Ok(await _policies.UpdateAsync(id, request, ct)));

    [HttpDelete("{id:guid}")]
    [RequirePermission(AppPermissions.SlaManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _policies.DeleteAsync(id, ct);
        return Ok(ApiResponse<object>.Ok(new { message = "SLA policy deleted." }));
    }
}
