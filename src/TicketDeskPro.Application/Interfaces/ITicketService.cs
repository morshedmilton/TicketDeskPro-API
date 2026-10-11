using TicketDeskPro.Application.DTOs.Tickets;

namespace TicketDeskPro.Application.Interfaces;

/// <summary>Ticket operations incl. thread messages and attachments (FRD Application layer).</summary>
public interface ITicketService
{
    Task<TicketListResponse> GetListAsync(TicketListQuery query, CancellationToken ct = default);
    Task<TicketDetailDto> CreateAsync(TicketCreateRequest request, CancellationToken ct = default);
    Task<TicketDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TicketDetailDto> UpdateAsync(Guid id, TicketUpdateRequest request, uint? ifMatch, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<TicketDetailDto> RestoreAsync(Guid id, CancellationToken ct = default);
    Task<TicketDetailDto> ChangeStatusAsync(Guid id, TicketStatusChangeRequest request, uint? ifMatch, CancellationToken ct = default);

    Task<MessageListResponse> GetMessagesAsync(Guid ticketId, int limit, string? cursor, CancellationToken ct = default);
    Task<TicketMessageDto> AddMessageAsync(Guid ticketId, TicketMessageCreateRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<TicketAttachmentDto>> ListAttachmentsAsync(Guid ticketId, CancellationToken ct = default);
    Task<TicketAttachmentDto> AddAttachmentAsync(Guid ticketId, Stream content, string fileName, string contentType, CancellationToken ct = default);
    Task<(Stream Content, string FileName, string ContentType)> DownloadAttachmentAsync(Guid ticketId, Guid attachmentId, CancellationToken ct = default);
    Task DeleteAttachmentAsync(Guid ticketId, Guid attachmentId, CancellationToken ct = default);
}
