using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketDeskPro.Api.Filters;
using TicketDeskPro.Application.DTOs.Admin;
using TicketDeskPro.Application.Services.Admin;
using TicketDeskPro.Shared.Constants;
using TicketDeskPro.Shared.Wrappers;

namespace TicketDeskPro.Api.Controllers.V1;

/// <summary>Admin user management (US-1.08, BR-038).</summary>
[ApiController]
[Authorize]
[Route("api/v1/users")]
public class UsersController : ControllerBase
{
    private readonly IUserAdminService _users;

    public UsersController(IUserAdminService users) => _users = users;

    [HttpGet]
    [RequirePermission(AppPermissions.UsersRead)]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? role,
        [FromQuery] int limit = 20, [FromQuery] string? cursor = null, CancellationToken ct = default) =>
        Ok(ApiResponse<UserPageDto>.Ok(await _users.GetPageAsync(search, role, limit, cursor, ct)));

    [HttpPost]
    [RequirePermission(AppPermissions.UsersCreate)]
    public async Task<IActionResult> Create(UserCreateRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, ApiResponse<UserAdminDto>.Ok(await _users.CreateAsync(request, ct)));

    [HttpPatch("{id:guid}")]
    [RequirePermission(AppPermissions.UsersUpdate)]
    public async Task<IActionResult> Update(Guid id, UserUpdateRequest request, CancellationToken ct) =>
        Ok(ApiResponse<UserAdminDto>.Ok(await _users.UpdateAsync(id, request, ct)));

    [HttpDelete("{id:guid}")]
    [RequirePermission(AppPermissions.UsersDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _users.DeleteAsync(id, ct);
        return Ok(ApiResponse<object>.Ok(new { message = "User deleted." }));
    }

    [HttpPost("{id:guid}/restore")]
    [RequirePermission(AppPermissions.UsersDelete)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct) =>
        Ok(ApiResponse<UserAdminDto>.Ok(await _users.RestoreAsync(id, ct)));
}
