namespace TicketDeskPro.Application.Interfaces;

/// <summary>Stores ticket files on disk (FRD §9): uploads/tickets/{ticketId}/{guid}_{filename}.</summary>
public interface IFileStorageService
{
    Task<string> SaveAsync(Guid ticketId, Stream content, string fileName, CancellationToken ct = default);
    Task<(Stream Content, string StoredPath)> OpenAsync(string storedPath, CancellationToken ct = default);
    void Delete(string storedPath);
}
