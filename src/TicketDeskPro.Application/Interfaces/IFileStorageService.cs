namespace TicketDeskPro.Application.Interfaces;

/// <summary>Attachment storage. The stored value is opaque to consumers:
/// a relative disk path (local) or a Cloudinary secure URL (production).</summary>
public interface IFileStorageService
{
    Task<string> SaveAsync(Guid ticketId, Stream content, string fileName, CancellationToken ct = default);
    Task<(Stream Content, string StoredPath)> OpenAsync(string storedPath, CancellationToken ct = default);
    Task DeleteAsync(string storedPath, CancellationToken ct = default);
}
