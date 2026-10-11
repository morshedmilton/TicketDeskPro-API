using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketDeskPro.Api.Filters;
using TicketDeskPro.Application.DTOs.Admin;
using TicketDeskPro.Application.Services.Admin;
using TicketDeskPro.Shared.Constants;
using TicketDeskPro.Shared.Wrappers;

namespace TicketDeskPro.Api.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/tags")]
public class TagsController : ControllerBase
{
    private readonly ICategoryTagService _service;

    public TagsController(ICategoryTagService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<TagDto>>.Ok(await _service.GetTagsAsync(ct)));

    [HttpPost]
    [RequirePermission(AppPermissions.TagsManage)]
    public async Task<IActionResult> Create(TagCreateRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, ApiResponse<TagDto>.Ok(await _service.CreateTagAsync(request, ct)));

    [HttpDelete("{id:guid}")]
    [RequirePermission(AppPermissions.TagsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _service.DeleteTagAsync(id, ct);
        return Ok(ApiResponse<object>.Ok(new { message = "Tag deleted." }));
    }
}
