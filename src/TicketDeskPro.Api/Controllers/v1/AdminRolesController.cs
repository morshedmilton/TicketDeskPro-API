using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketDeskPro.Api.Filters;
using TicketDeskPro.Application.DTOs.Admin;
using TicketDeskPro.Application.Services.Admin;
using TicketDeskPro.Shared.Constants;
using TicketDeskPro.Shared.Wrappers;

namespace TicketDeskPro.Api.Controllers.V1;

/// <summary>SuperAdmin role & permission management (US-1.09). System roles are locked.</summary>
[ApiController]
[Authorize]
[Route("api/v1/admin/roles")]
public class AdminRolesController : ControllerBase
{
    private readonly IRoleAdminService _roles;

    public AdminRolesController(IRoleAdminService roles) => _roles = roles;

    [HttpGet]
    [RequirePermission(AppPermissions.RolesRead)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<RoleDto>>.Ok(await _roles.GetAllAsync(ct)));

    [HttpGet("permissions")]
    [RequirePermission(AppPermissions.RolesRead)]
    public async Task<IActionResult> Permissions(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<PermissionDto>>.Ok(await _roles.GetPermissionsAsync(ct)));

    [HttpPost]
    [RequirePermission(AppPermissions.RolesManage)]
    public async Task<IActionResult> Create(RoleCreateRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, ApiResponse<RoleDto>.Ok(await _roles.CreateAsync(request, ct)));

    [HttpPut("{id:guid}")]
    [RequirePermission(AppPermissions.RolesManage)]
    public async Task<IActionResult> Update(Guid id, RoleUpdateRequest request, CancellationToken ct) =>
        Ok(ApiResponse<RoleDto>.Ok(await _roles.UpdateAsync(id, request, ct)));

    [HttpDelete("{id:guid}")]
    [RequirePermission(AppPermissions.RolesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _roles.DeleteAsync(id, ct);
        return Ok(ApiResponse<object>.Ok(new { message = "Role deleted." }));
    }
}
