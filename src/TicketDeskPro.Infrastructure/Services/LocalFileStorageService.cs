using Microsoft.Extensions.Configuration;
using TicketDeskPro.Application.Interfaces;

namespace TicketDeskPro.Infrastructure.Services;

/// <summary>
/// Disk storage per FRD §9: {root}/tickets/{ticketId}/{guid}_{sanitized-filename}.
/// Root comes from config "Storage:RootPath" (default "uploads" relative to the working dir).
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _root;

    public LocalFileStorageService(IConfiguration configuration)
    {
        _root = configuration["Storage:RootPath"] ?? "uploads";
    }

    public async Task<string> SaveAsync(Guid ticketId, Stream content, string fileName, CancellationToken ct = default)
    {
        var directory = Path.Combine(_root, "tickets", ticketId.ToString());
        Directory.CreateDirectory(directory);

        var safeName = Sanitize(fileName);
        var relativePath = Path.Combine("tickets", ticketId.ToString(), $"{Guid.NewGuid():N}_{safeName}").Replace('\\', '/');
        var fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = File.Create(fullPath);
        await content.CopyToAsync(file, ct);
        return relativePath;
    }

    public Task<(Stream Content, string StoredPath)> OpenAsync(string storedPath, CancellationToken ct = default)
    {
        var fullPath = ResolveSafe(storedPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Attachment file is missing from storage.", storedPath);

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult((stream, storedPath));
    }

    public Task DeleteAsync(string storedPath, CancellationToken ct = default)
    {
        var fullPath = ResolveSafe(storedPath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    /// <summary>Path traversal guard: the stored path must stay under the root, always.</summary>
    private string ResolveSafe(string storedPath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_root, storedPath));
        var rootFull = Path.GetFullPath(_root) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootFull, StringComparison.Ordinal))
            throw new FileNotFoundException("Attachment file is missing from storage.", storedPath);
        return fullPath;
    }

    private static string Sanitize(string fileName)
    {
        var name = Path.GetFileName(fileName);
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 150 ? name[..150] : name;
    }
}
