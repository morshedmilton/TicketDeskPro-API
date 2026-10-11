using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketDeskPro.Api.Filters;
using TicketDeskPro.Application.DTOs.Admin;
using TicketDeskPro.Application.Services.Admin;
using TicketDeskPro.Shared.Constants;
using TicketDeskPro.Shared.Wrappers;

namespace TicketDeskPro.Api.Controllers.V1;

/// <summary>Category management (US-10.01). List is open to all authenticated users
/// (the create-ticket form needs it); mutations are admin-gated.</summary>
[ApiController]
[Authorize]
[Route("api/v1/categories")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryTagService _service;

    public CategoriesController(ICategoryTagService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<CategoryDto>>.Ok(await _service.GetCategoriesAsync(ct)));

    [HttpPost]
    [RequirePermission(AppPermissions.CategoriesManage)]
    public async Task<IActionResult> Create(CategoryCreateRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, ApiResponse<CategoryDto>.Ok(await _service.CreateCategoryAsync(request, ct)));

    [HttpPut("{id:guid}")]
    [RequirePermission(AppPermissions.CategoriesManage)]
    public async Task<IActionResult> Update(Guid id, CategoryUpdateRequest request, CancellationToken ct) =>
        Ok(ApiResponse<CategoryDto>.Ok(await _service.UpdateCategoryAsync(id, request, ct)));

    [HttpDelete("{id:guid}")]
    [RequirePermission(AppPermissions.CategoriesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _service.DeleteCategoryAsync(id, ct);
        return Ok(ApiResponse<object>.Ok(new { message = "Category deleted." }));
    }
}
