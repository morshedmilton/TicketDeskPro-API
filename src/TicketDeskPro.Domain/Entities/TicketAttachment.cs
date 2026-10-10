namespace TicketDeskPro.Domain.Entities;

public class TicketAttachment : BaseEntity
{
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;

    /// <summary>Original file name as uploaded by the user.</summary>
    public string FileName { get; set; } = null!;

    /// <summary>On-disk name/path: uploads/tickets/{ticketId}/{guid}_{filename}.</summary>
    public string StoredPath { get; set; } = null!;

    public string ContentType { get; set; } = null!;
    public long FileSizeBytes { get; set; }

    public Guid UploadedById { get; set; }
    public User UploadedBy { get; set; } = null!;
}
