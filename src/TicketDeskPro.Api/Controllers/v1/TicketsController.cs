using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketDeskPro.Application.DTOs.Tickets;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Api.Filters;
using TicketDeskPro.Shared.Constants;
using TicketDeskPro.Shared.Wrappers;

namespace TicketDeskPro.Api.Controllers.V1;

/// <summary>
/// Ticket lifecycle + threaded messages + attachments (API Spec §3/§4).
/// Ownership rules (BR-022) and permission gates are enforced inside TicketService.
/// Status/update endpoints take an If-Match ETag for optimistic concurrency (FRD §6).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/[controller]")]
public class TicketsController : ControllerBase
{
    private readonly ITicketService _tickets;

    public TicketsController(ITicketService tickets) => _tickets = tickets;

    // ---------- list / create ----------

    [HttpGet]
    [RequirePermission(AppPermissions.TicketsRead)]
    public async Task<IActionResult> List([FromQuery] TicketListQuery query, CancellationToken ct) =>
        Ok(ApiResponse<TicketListResponse>.Ok(await _tickets.GetListAsync(query, ct)));

    [HttpPost]
    [RequirePermission(AppPermissions.TicketsCreate)]
    public async Task<IActionResult> Create(TicketCreateRequest request, CancellationToken ct)
    {
        var created = await _tickets.CreateAsync(request, ct);
        return TicketWithEtag(StatusCodes.Status201Created, created);
    }

    // ---------- read / update / delete ----------

    [HttpGet("{id:guid}")]
    [RequirePermission(AppPermissions.TicketsRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var ticket = await _tickets.GetByIdAsync(id, ct);
        return Ok(ApiResponse<TicketDetailDto>.Ok(ticket));
    }

    [HttpPatch("{id:guid}")]
    [RequirePermission(AppPermissions.TicketsUpdate)]
    public async Task<IActionResult> Update(Guid id, TicketUpdateRequest request, CancellationToken ct) =>
        TicketWithEtag(StatusCodes.Status200OK, await _tickets.UpdateAsync(id, request, ReadIfMatch(), ct));

    [HttpDelete("{id:guid}")]
    [RequirePermission(AppPermissions.TicketsDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _tickets.DeleteAsync(id, ct);
        return Ok(ApiResponse<object>.Ok(new { message = "Ticket deleted." }));
    }

    [HttpPost("{id:guid}/restore")]
    [RequirePermission(AppPermissions.TicketsRestore)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct) =>
        Ok(ApiResponse<TicketDetailDto>.Ok(await _tickets.RestoreAsync(id, ct)));

    // ---------- status state machine ----------
    // No [RequirePermission] here on purpose: BR-016 lets a customer move their OWN ticket
    // AwaitingCustomer→CustomerReplied without tickets.update — the service enforces the
    // fine-grained rule per transition instead of a blanket endpoint gate.

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid id, TicketStatusChangeRequest request, CancellationToken ct) =>
        TicketWithEtag(StatusCodes.Status200OK, await _tickets.ChangeStatusAsync(id, request, ReadIfMatch(), ct));

    // ---------- message thread ----------

    [HttpGet("{ticketId:guid}/messages")]
    [RequirePermission(AppPermissions.MessagesRead)]
    public async Task<IActionResult> GetMessages(Guid ticketId, [FromQuery] int limit = 50, [FromQuery] string? cursor = null, CancellationToken ct = default) =>
        Ok(ApiResponse<MessageListResponse>.Ok(await _tickets.GetMessagesAsync(ticketId, limit, cursor, ct)));

    [HttpPost("{ticketId:guid}/messages")]
    [RequirePermission(AppPermissions.MessagesCreate)]
    public async Task<IActionResult> AddMessage(Guid ticketId, TicketMessageCreateRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, ApiResponse<TicketMessageDto>.Ok(await _tickets.AddMessageAsync(ticketId, request, ct)));

    // ---------- attachments ----------
    // Upload/download/delete intentionally have no [RequirePermission]: the service enforces
    // view-access, owner-only delete (BR-025) and resolved-lock (BR-026) per attachment.

    [HttpGet("{ticketId:guid}/attachments")]
    [RequirePermission(AppPermissions.MessagesRead)]
    public async Task<IActionResult> ListAttachments(Guid ticketId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<TicketAttachmentDto>>.Ok(await _tickets.ListAttachmentsAsync(ticketId, ct)));

    [HttpPost("{ticketId:guid}/attachments")]
    [RequestSizeLimit(6 * 1024 * 1024)] // BR-009: 5MB business cap + multipart overhead
    public async Task<IActionResult> UploadAttachment(Guid ticketId, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail([new ApiError(ErrorCodes.Validation, "A non-empty file is required.", "file")]));

        await using var stream = file.OpenReadStream();
        var result = await _tickets.AddAttachmentAsync(ticketId, stream, file.FileName, file.ContentType, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<TicketAttachmentDto>.Ok(result));
    }

    [HttpGet("{ticketId:guid}/attachments/{attachmentId:guid}/download")]
    [RequirePermission(AppPermissions.MessagesRead)]
    public async Task<IActionResult> DownloadAttachment(Guid ticketId, Guid attachmentId, CancellationToken ct)
    {
        var (content, fileName, contentType) = await _tickets.DownloadAttachmentAsync(ticketId, attachmentId, ct);
        return File(content, contentType, fileName);
    }

    [HttpDelete("{ticketId:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> DeleteAttachment(Guid ticketId, Guid attachmentId, CancellationToken ct)
    {
        await _tickets.DeleteAttachmentAsync(ticketId, attachmentId, ct);
        return Ok(ApiResponse<object>.Ok(new { message = "Attachment deleted." }));
    }

    /// <summary>If-Match: "12345" or bare 12345 → uint xmin. Absent = no check (per spec);
    /// present but unparseable = 422 (never silently skip the precondition).</summary>
    private uint? ReadIfMatch()
    {
        var header = Request.Headers.IfMatch.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header)) return null;

        var trimmed = header.Trim().Trim('"');
        if (!uint.TryParse(trimmed, out var xmin))
            throw new TicketDeskPro.Shared.Exceptions.ValidationException("One or more validation errors occurred.",
                [new TicketDeskPro.Shared.Exceptions.ValidationFailure("If-Match", "If-Match must contain the ticket's numeric ETag, e.g. If-Match: \"1234\".")]);

        return xmin;
    }

    /// <summary>Every mutation response echoes the fresh ETag so clients can chain updates.</summary>
    private IActionResult TicketWithEtag(int statusCode, TicketDetailDto ticket)
    {
        Response.Headers.ETag = $"\"{ticket.Etag}\"";
        return StatusCode(statusCode, ApiResponse<TicketDetailDto>.Ok(ticket));
    }
}
