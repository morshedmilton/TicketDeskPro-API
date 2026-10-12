using TicketDeskPro.Domain.Enums;

namespace TicketDeskPro.Application.DTOs.Tickets;

// ---- requests ----

public record TicketCreateRequest(string Title, string Description, TicketPriority Priority, Guid CategoryId, IReadOnlyList<Guid>? TagIds = null);

public record TicketUpdateRequest(string Title, string Description, TicketPriority Priority, Guid CategoryId, IReadOnlyList<Guid>? TagIds = null);

public record TicketStatusChangeRequest(TicketStatus Status, string? Reason = null);

public record TicketAssignRequest(Guid AssignedToId);

public record TicketListQuery(
    int Limit = 20,
    string? Cursor = null,
    string? Search = null,
    TicketStatus? Status = null,
    TicketPriority? Priority = null,
    Guid? CategoryId = null,
    Guid? AssignedTo = null,   // spec §3.1 query name: assignedTo
    bool OnlyDeleted = false,
    string? Sort = null,       // createdAt | updatedAt | priority
    string? Order = null);     // asc | desc

// ---- responses ----

public record TicketSummaryDto(
    Guid Id,
    string TicketNumber,
    string Title,
    TicketStatus Status,
    TicketPriority Priority,
    Guid CategoryId,
    string CategoryName,
    Guid CreatedById,
    string CreatedByName,
    Guid? AssignedToId,
    string? AssignedToName,
    uint Etag,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>SLA state embedded in the ticket detail (US-2.04 / US-5.03 visual badge).</summary>
public record SlaSummaryDto(
    DateTime FirstResponseDeadline,
    DateTime ResolutionDeadline,
    bool IsPaused,
    bool IsFirstResponseBreached,
    bool IsResolutionBreached,
    DateTime? FirstResponseAt);

public record TicketDetailDto(
    Guid Id,
    string TicketNumber,
    string Title,
    string Description,
    TicketStatus Status,
    TicketPriority Priority,
    Guid CategoryId,
    string CategoryName,
    Guid CreatedById,
    string CreatedByName,
    Guid? AssignedToId,
    string? AssignedToName,
    IReadOnlyList<string> Tags,
    SlaSummaryDto? Sla,
    uint Etag,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record TicketListResponse(IReadOnlyList<TicketSummaryDto> Items, string? NextCursor, bool HasMore);

// ---- messages ----

public record TicketMessageCreateRequest(string Content, bool IsInternal = false);

public record TicketMessageDto(
    Guid Id,
    Guid TicketId,
    string Content,
    bool IsInternal,
    Guid CreatedById,
    string CreatedByName,
    DateTime CreatedAt);

public record MessageListResponse(
    IReadOnlyList<TicketMessageDto> Items,
    string? NextCursor,
    bool HasMore);

// ---- attachments ----

public record TicketAttachmentDto(
    Guid Id,
    Guid TicketId,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    Guid UploadedById,
    string UploadedByName,
    DateTime CreatedAt);
