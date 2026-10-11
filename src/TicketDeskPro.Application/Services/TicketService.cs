using System.Text;
using FluentValidation;
using TicketDeskPro.Application.DTOs.Tickets;
using TicketDeskPro.Application.Extensions;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Domain.Entities;
using TicketDeskPro.Domain.Enums;
using TicketDeskPro.Domain.Interfaces;
using TicketDeskPro.Domain.Services;
using TicketDeskPro.Shared.Constants;
using TicketDeskPro.Shared.Exceptions;
using ValidationException = TicketDeskPro.Shared.Exceptions.ValidationException;
using ValidationFailure = TicketDeskPro.Shared.Exceptions.ValidationFailure;

namespace TicketDeskPro.Application.Services;

public class TicketService : ITicketService
{
    public const int MaxAttachmentsPerTicket = 5;      // BR-009
    public const long MaxAttachmentBytes = 5 * 1024 * 1024; // BR-009
    public static readonly string[] AllowedAttachmentExtensions = [".jpg", ".png", ".gif", ".pdf", ".docx", ".xlsx", ".txt"]; // BR-010

    private readonly ITicketRepository _tickets;
    private readonly ITicketMessageRepository _messages;
    private readonly ITicketAttachmentRepository _attachments;
    private readonly ICategoryRepository _categories;
    private readonly ITagRepository _tags;
    private readonly ISlaPolicyRepository _slaPolicies;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorageService _storage;
    private readonly ICurrentUserService _currentUser;
    private readonly IValidator<TicketCreateRequest> _createValidator;
    private readonly IValidator<TicketUpdateRequest> _updateValidator;
    private readonly IValidator<TicketStatusChangeRequest> _statusValidator;
    private readonly IValidator<TicketMessageCreateRequest> _messageValidator;

    public TicketService(
        ITicketRepository tickets,
        ITicketMessageRepository messages,
        ITicketAttachmentRepository attachments,
        ICategoryRepository categories,
        ITagRepository tags,
        ISlaPolicyRepository slaPolicies,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IFileStorageService storage,
        ICurrentUserService currentUser,
        IValidator<TicketCreateRequest> createValidator,
        IValidator<TicketUpdateRequest> updateValidator,
        IValidator<TicketStatusChangeRequest> statusValidator,
        IValidator<TicketMessageCreateRequest> messageValidator)
    {
        _tickets = tickets;
        _messages = messages;
        _attachments = attachments;
        _categories = categories;
        _tags = tags;
        _slaPolicies = slaPolicies;
        _users = users;
        _unitOfWork = unitOfWork;
        _storage = storage;
        _currentUser = currentUser;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _statusValidator = statusValidator;
        _messageValidator = messageValidator;
    }

    // ================= list =================

    public async Task<TicketListResponse> GetListAsync(TicketListQuery query, CancellationToken ct = default)
    {
        var viewer = await RequireViewerAsync(ct);

        // BR-022/BR-037: customers see their own live tickets; agents/admins see everything.
        Guid? createdByFilter = IsAgentLike(viewer) ? null : viewer.Id;
        if (query.OnlyDeleted && !HasPermission(viewer, AppPermissions.TicketsDelete))
            throw new ForbiddenException(ErrorCodes.MissingPermission, "You do not have permission to perform this action."); // US-2.07: deleted view is admin-only

        var limit = Math.Clamp(query.Limit, 1, 100);
        var page = await _tickets.GetPageAsync(limit, query.Cursor, query.Search,
            query.Status is null ? null : (int)query.Status,
            query.Priority is null ? null : (int)query.Priority,
            query.CategoryId, query.AssignedTo,
            createdByFilter, query.OnlyDeleted, query.Sort, query.Order, ct);

        var items = page.Items.Select(t => ToSummary(t)).ToList();
        return new TicketListResponse(items, page.NextCursor, page.HasMore);
    }

    // ================= create =================

    public async Task<TicketDetailDto> CreateAsync(TicketCreateRequest request, CancellationToken ct = default)
    {
        await _createValidator.EnsureValidAsync(request, ct);
        var creator = await RequireViewerAsync(ct);

        var category = await _categories.GetByIdAsync(request.CategoryId, ct)
            ?? throw new NotFoundException(ErrorCodes.CategoryNotFound, "The requested category was not found.");

        List<Tag>? tags = null;
        if (request.TagIds is { Count: > 0 })
        {
            var found = await _tags.GetByIdsAsync(request.TagIds.Distinct().ToList(), ct);
            if (found.Count != request.TagIds.Distinct().Count())
                throw new ValidationException("One or more validation errors occurred.",
                    [new ValidationFailure("tagIds", "One or more tags do not exist.")]);
            tags = found.ToList();
        }

        var now = DateTime.UtcNow;
        var ticket = new Ticket
        {
            TicketNumber = await NextTicketNumberAsync(ct),
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Status = TicketStatus.Open,          // BR-013
            Priority = request.Priority,
            CategoryId = category.Id,
            Category = category,
            CreatedById = creator.Id
        };

        if (tags is not null)
            foreach (var tag in tags)
                ticket.Tags.Add(new TicketTag { Ticket = ticket, Tag = tag });

        // US-5.02: the SLA stopwatch starts the moment the ticket exists.
        var policy = await _slaPolicies.GetByPriorityAsync((int)request.Priority, ct);
        if (policy is not null)
        {
            ticket.SlaTimer = new SlaTimer
            {
                SlaPolicyId = policy.Id,
                FirstResponseDeadline = now.AddMinutes(policy.FirstResponseMinutes),
                ResolutionDeadline = now.AddMinutes(policy.ResolutionMinutes)
            };
        }

        await _tickets.AddAsync(ticket, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return await GetByIdAsync(ticket.Id, ct);
    }

    // ================= read =================

    public async Task<TicketDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var viewer = await RequireViewerAsync(ct);
        var ticket = await _tickets.GetByIdAsync(id, includeDetails: true, includeDeleted: false, ct)
            ?? throw NotFound();
        EnsureCanView(viewer, ticket);
        return ToDetail(ticket);
    }

    // ================= update metadata =================

    public async Task<TicketDetailDto> UpdateAsync(Guid id, TicketUpdateRequest request, uint? ifMatch, CancellationToken ct = default)
    {
        await _updateValidator.EnsureValidAsync(request, ct);
        var viewer = await RequireViewerAsync(ct);
        var ticket = await _tickets.GetByIdAsync(id, includeDetails: true, includeDeleted: false, ct)
            ?? throw NotFound();

        EnsureCanView(viewer, ticket);
        EnsurePermission(viewer, AppPermissions.TicketsUpdate);
        EnsureEtag(ticket, ifMatch);

        // BR-021: metadata is editable only while Open or InProgress.
        if (ticket.Status is not (TicketStatus.Open or TicketStatus.InProgress))
            throw new ConflictException(ErrorCodes.TicketNotEditable,
                $"Ticket metadata can only be edited while Open or InProgress (currently {ticket.Status}).");

        ticket.Title = request.Title.Trim();
        ticket.Description = request.Description.Trim();
        ticket.Priority = request.Priority;

        var category = await _categories.GetByIdAsync(request.CategoryId, ct)
            ?? throw new NotFoundException(ErrorCodes.CategoryNotFound, "The requested category was not found.");
        ticket.CategoryId = category.Id;
        ticket.Category = category; // keep the tracked nav in sync so the re-read shows the new name

        // US-2.05: agents may also replace the tag set.
        if (request.TagIds is not null)
        {
            var distinct = request.TagIds.Distinct().ToList();
            var found = await _tags.GetByIdsAsync(distinct, ct);
            if (found.Count != distinct.Count)
                throw new ValidationException("One or more validation errors occurred.",
                    [new ValidationFailure("tagIds", "One or more tags do not exist.")]);

            ticket.Tags.Clear();
            foreach (var tag in found)
                ticket.Tags.Add(new TicketTag { Ticket = ticket, Tag = tag });
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    // ================= delete / restore =================

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var viewer = await RequireViewerAsync(ct);
        var ticket = await _tickets.GetByIdAsync(id, includeDetails: false, includeDeleted: false, ct)
            ?? throw NotFound();
        EnsurePermission(viewer, AppPermissions.TicketsDelete); // admin-only: customers/agents lack this

        _tickets.Remove(ticket); // SoftDeleteInterceptor flips this to IsDeleted=true
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<TicketDetailDto> RestoreAsync(Guid id, CancellationToken ct = default)
    {
        var viewer = await RequireViewerAsync(ct);
        EnsurePermission(viewer, AppPermissions.TicketsRestore);

        var ticket = await _tickets.GetByIdAsync(id, includeDetails: false, includeDeleted: true, ct)
            ?? throw NotFound();

        if (!ticket.IsDeleted)
            throw new ConflictException(ErrorCodes.ConcurrencyConflict, "This ticket is not deleted.");

        ticket.IsDeleted = false;
        ticket.DeletedAt = null;
        ticket.DeletedById = null;
        await _unitOfWork.SaveChangesAsync(ct);

        return await GetByIdAsync(id, ct);
    }

    // ================= status state machine =================

    public async Task<TicketDetailDto> ChangeStatusAsync(Guid id, TicketStatusChangeRequest request, uint? ifMatch, CancellationToken ct = default)
    {
        await _statusValidator.EnsureValidAsync(request, ct);
        var viewer = await RequireViewerAsync(ct);
        var ticket = await _tickets.GetByIdAsync(id, includeDetails: true, includeDeleted: false, ct)
            ?? throw NotFound();

        EnsureCanView(viewer, ticket);

        // BR-016: the customer's own reply is what moves AwaitingCustomer → CustomerReplied —
        // that single transition is owner-driven and does NOT require tickets.update.
        var isCustomerReply = request.Status == TicketStatus.CustomerReplied
                              && ticket.Status == TicketStatus.AwaitingCustomer
                              && ticket.CreatedById == viewer.Id;
        if (!isCustomerReply)
            EnsurePermission(viewer, AppPermissions.TicketsUpdate);

        EnsureEtag(ticket, ifMatch);

        if (request.Status == ticket.Status)
            throw new ConflictException(ErrorCodes.InvalidStateTransition,
                $"Ticket is already {ticket.Status}.");

        var canForceClose = HasPermission(viewer, AppPermissions.TicketsDelete);

        if (request.Status == TicketStatus.Reopened)
        {
            // BR-019: 48h window from the actual resolution moment, not the last write.
            var resolvedAt = await _tickets.GetLastResolvedAtAsync(ticket.Id, ct) ?? ticket.UpdatedAt;
            if (!TicketStateMachine.CanReopen(ticket.Status, resolvedAt, DateTime.UtcNow))
                throw new ConflictException(ErrorCodes.InvalidStateTransition,
                    "A ticket can only be reopened within 48 hours of being resolved.");
        }

        if (!TicketStateMachine.CanTransition(ticket.Status, request.Status, canForceClose))
            throw new ConflictException(ErrorCodes.InvalidStateTransition,
                $"Cannot transition ticket from {ticket.Status} to {request.Status}.");

        ticket.StatusHistory.Add(new TicketStatusHistory
        {
            Ticket = ticket,
            FromStatus = ticket.Status,
            ToStatus = request.Status,
            Reason = request.Reason?.Trim(),
            ChangedById = viewer.Id
        });
        ticket.Status = request.Status;

        await _unitOfWork.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    // ================= messages =================

    public async Task<MessageListResponse> GetMessagesAsync(Guid ticketId, int limit, string? cursor, CancellationToken ct = default)
    {
        var viewer = await RequireViewerAsync(ct);
        var ticket = await _tickets.GetByIdAsync(ticketId, includeDetails: false, includeDeleted: false, ct)
            ?? throw NotFound();
        EnsureCanView(viewer, ticket);

        // BR-023: internal notes are strictly hidden from the Customer role.
        var includeInternal = IsAgentLike(viewer);
        var capped = Math.Clamp(limit, 1, 100);
        var rows = await _messages.GetThreadAsync(ticketId, capped, cursor, includeInternal, ct);

        var hasMore = rows.Count > capped;
        var items = hasMore ? rows.Take(capped).ToList() : rows.ToList();
        var last = items.LastOrDefault();
        var nextCursor = hasMore && last is not null
            ? EncodeMessageCursor(last.CreatedAt, last.Id)
            : null;

        return new MessageListResponse(items.Select(ToMessage).ToList(), nextCursor, hasMore);
    }

    public async Task<TicketMessageDto> AddMessageAsync(Guid ticketId, TicketMessageCreateRequest request, CancellationToken ct = default)
    {
        await _messageValidator.EnsureValidAsync(request, ct);
        var viewer = await RequireViewerAsync(ct);
        var ticket = await _tickets.GetByIdAsync(ticketId, includeDetails: true, includeDeleted: false, ct)
            ?? throw NotFound();
        EnsureCanView(viewer, ticket);

        if (request.IsInternal)
            EnsurePermission(viewer, AppPermissions.MessagesInternalNote); // customers cannot post notes

        var message = new TicketMessage
        {
            Ticket = ticket,
            Content = request.Content.Trim(),
            IsInternal = request.IsInternal,
            CreatedById = viewer.Id
        };
        await _messages.AddAsync(message, ct);

        // BR-033: first public agent reply satisfies the First Response SLA.
        if (!request.IsInternal && IsAgentLike(viewer) && ticket.SlaTimer is { FirstResponseAt: null })
            ticket.SlaTimer.FirstResponseAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync(ct);
        return ToMessage(message);
    }

    // ================= attachments =================

    public async Task<IReadOnlyList<TicketAttachmentDto>> ListAttachmentsAsync(Guid ticketId, CancellationToken ct = default)
    {
        var viewer = await RequireViewerAsync(ct);
        var ticket = await _tickets.GetByIdAsync(ticketId, includeDetails: false, includeDeleted: false, ct)
            ?? throw NotFound();
        EnsureCanView(viewer, ticket);

        var attachments = await _attachments.GetByTicketAsync(ticketId, ct);
        return attachments.Select(ToAttachment).ToList();
    }

    public async Task<TicketAttachmentDto> AddAttachmentAsync(Guid ticketId, Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var viewer = await RequireViewerAsync(ct);
        var ticket = await _tickets.GetByIdAsync(ticketId, includeDetails: true, includeDeleted: false, ct)
            ?? throw NotFound();
        EnsureCanView(viewer, ticket);

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedAttachmentExtensions.Contains(extension))
            throw new ConflictException(ErrorCodes.AttachmentNotAllowed,
                $"File type not allowed. Allowed: {string.Join(", ", AllowedAttachmentExtensions)}");

        if (content.CanSeek && content.Length > MaxAttachmentBytes)
            throw new ValidationException("One or more validation errors occurred.",
                [new ValidationFailure("file", "File exceeds the maximum allowed size of 5MB.")]);

        if (await _attachments.CountByTicketAsync(ticketId, ct) >= MaxAttachmentsPerTicket)
            throw new ConflictException(ErrorCodes.AttachmentNotAllowed,
                $"A ticket can have at most {MaxAttachmentsPerTicket} attachments.");

        var safeName = Path.GetFileName(fileName);
        var storedPath = await _storage.SaveAsync(ticketId, content, safeName, ct);

        var attachment = new TicketAttachment
        {
            Ticket = ticket,
            FileName = safeName,
            StoredPath = storedPath,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            FileSizeBytes = content.CanSeek ? content.Length : 0,
            UploadedById = viewer.Id
        };
        await _attachments.AddAsync(attachment, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return ToAttachment(attachment);
    }

    public async Task<(Stream Content, string FileName, string ContentType)> DownloadAttachmentAsync(Guid ticketId, Guid attachmentId, CancellationToken ct = default)
    {
        var viewer = await RequireViewerAsync(ct);
        var ticket = await _tickets.GetByIdAsync(ticketId, includeDetails: false, includeDeleted: false, ct)
            ?? throw NotFound();
        EnsureCanView(viewer, ticket);

        var attachment = await _attachments.GetByIdAsync(attachmentId, ct);
        if (attachment is null || attachment.TicketId != ticketId)
            throw new NotFoundException(ErrorCodes.TicketNotFound, "The requested attachment was not found.");

        var (stream, _) = await _storage.OpenAsync(attachment.StoredPath, ct);
        return (stream, attachment.FileName, attachment.ContentType);
    }

    public async Task DeleteAttachmentAsync(Guid ticketId, Guid attachmentId, CancellationToken ct = default)
    {
        var viewer = await RequireViewerAsync(ct);
        var ticket = await _tickets.GetByIdAsync(ticketId, includeDetails: true, includeDeleted: false, ct)
            ?? throw NotFound();
        EnsureCanView(viewer, ticket);

        var attachment = await _attachments.GetByIdAsync(attachmentId, ct);
        if (attachment is null || attachment.TicketId != ticketId)
            throw new NotFoundException(ErrorCodes.TicketNotFound, "The requested attachment was not found.");

        // BR-025: only your own attachments.
        if (attachment.UploadedById != viewer.Id)
            throw new ForbiddenException(ErrorCodes.MissingPermission, "You can only delete attachments you uploaded.");

        // BR-026: nothing is deleted once the ticket is Resolved.
        if (ticket.Status is TicketStatus.Resolved or TicketStatus.Closed)
            throw new ConflictException(ErrorCodes.AttachmentNotAllowed,
                "Attachments cannot be deleted once a ticket is resolved.");

        _attachments.Remove(attachment);
        await _unitOfWork.SaveChangesAsync(ct);
        // ponytail: the physical file stays on disk until a cleanup job (Phase 8) sweeps unreferenced files.
    }

    // ================= helpers =================

    private async Task<User> RequireViewerAsync(CancellationToken ct)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException(ErrorCodes.InvalidTokenState, "Not authenticated.");
        return await _users.GetByIdWithRolesAsync(userId, ct)
            ?? throw new NotFoundException(ErrorCodes.UserNotFound, "The requested user was not found.");
    }

    private static bool HasPermission(User viewer, string permission) =>
        viewer.UserRoles.Any(ur => ur.Role.RolePermissions.Any(rp => rp.Permission.Name == permission));

    private static bool IsAgentLike(User viewer) => HasPermission(viewer, AppPermissions.TicketsUpdate);

    private static void EnsurePermission(User viewer, string permission)
    {
        if (!HasPermission(viewer, permission))
            throw new ForbiddenException(ErrorCodes.MissingPermission, "You do not have permission to perform this action.");
    }

    /// <summary>BR-022: customers only ever see their own tickets (404, not 403 — no existence leaks).</summary>
    private static void EnsureCanView(User viewer, Ticket ticket)
    {
        if (!IsAgentLike(viewer) && ticket.CreatedById != viewer.Id)
            throw NotFound();
    }

    private static NotFoundException NotFound() =>
        new(ErrorCodes.TicketNotFound, "The requested ticket was not found.");

    private static void EnsureEtag(Ticket ticket, uint? ifMatch)
    {
        if (ifMatch is not null && ifMatch != ticket.Xmin)
            throw new ConflictException(ErrorCodes.ConcurrencyConflict,
                "The resource has been modified by another user.");
    }

    private async Task<string> NextTicketNumberAsync(CancellationToken ct)
    {
        // TKT-0001.. sequential. ponytail: max+1 with the unique index as the safety net;
        // swap to a Postgres SEQUENCE if create-volume ever stresses this.
        var live = await _tickets.CountLiveAsync(ct);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var candidate = $"TKT-{live + 1 + attempt:D4}";
            if (!await _tickets.TicketNumberExistsAsync(candidate, ct))
                return candidate;
        }
        return $"TKT-{Guid.NewGuid():N}"[..8]; // pathological fallback; unique by construction
    }

    private static string EncodeMessageCursor(DateTime createdAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{createdAt.Ticks}:{id}"));

    private TicketSummaryDto ToSummary(Ticket t) => new(
        t.Id, t.TicketNumber, t.Title, t.Status, t.Priority,
        t.CategoryId, t.Category.Name,
        t.CreatedById, t.CreatedBy.FirstName + " " + t.CreatedBy.LastName,
        t.AssignedToId, t.AssignedTo is null ? null : t.AssignedTo.FirstName + " " + t.AssignedTo.LastName,
        t.CreatedAt, t.UpdatedAt);

    private TicketDetailDto ToDetail(Ticket t) => new(
        t.Id, t.TicketNumber, t.Title, t.Description, t.Status, t.Priority,
        t.CategoryId, t.Category.Name,
        t.CreatedById, t.CreatedBy.FirstName + " " + t.CreatedBy.LastName,
        t.AssignedToId, t.AssignedTo is null ? null : t.AssignedTo.FirstName + " " + t.AssignedTo.LastName,
        t.Tags.Select(tt => tt.Tag.Name).ToList(),
        t.Xmin,
        t.CreatedAt, t.UpdatedAt);

    private static TicketMessageDto ToMessage(TicketMessage m) => new(
        m.Id, m.TicketId, m.Content, m.IsInternal,
        m.CreatedById, m.CreatedBy.FirstName + " " + m.CreatedBy.LastName, m.CreatedAt);

    private static TicketAttachmentDto ToAttachment(TicketAttachment a) => new(
        a.Id, a.TicketId, a.FileName, a.ContentType, a.FileSizeBytes,
        a.UploadedById, a.UploadedBy.FirstName + " " + a.UploadedBy.LastName, a.CreatedAt);
}
