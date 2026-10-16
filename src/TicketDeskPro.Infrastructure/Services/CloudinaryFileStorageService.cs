using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TicketDeskPro.Application.Interfaces;

namespace TicketDeskPro.Infrastructure.Services;

/// <summary>
/// Attachment storage on Cloudinary's free tier (25 GB) — Render's free disk is
/// ephemeral, so local files vanish on every redeploy. Signed uploads over
/// HTTPS (port 443, same constraint as the Brevo email service): no SDK, just
/// the documented REST endpoints. The DB stores the returned secure_url as the
/// opaque "stored path". Selected in DI only when Cloudinary:CloudName is set;
/// dev/tests keep LocalFileStorageService.
/// </summary>
public class CloudinaryFileStorageService : IFileStorageService
{
    // ponytail: one outbound host, one call site — a static client is enough
    // (same reasoning as BrevoEmailService). Uploads up to 5 MB, downloads stream.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private readonly HttpClient _http;
    private readonly string _cloudName;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly ILogger<CloudinaryFileStorageService> _logger;

    // httpClient: test seam — production DI leaves it null and the static client is used.
    public CloudinaryFileStorageService(IConfiguration configuration, ILogger<CloudinaryFileStorageService> logger, HttpClient? httpClient = null)
    {
        _http = httpClient ?? Http;
        _cloudName = configuration["Cloudinary:CloudName"] ?? throw new InvalidOperationException("Cloudinary:CloudName is not configured.");
        _apiKey = configuration["Cloudinary:ApiKey"] ?? throw new InvalidOperationException("Cloudinary:ApiKey is not configured.");
        _apiSecret = configuration["Cloudinary:ApiSecret"] ?? throw new InvalidOperationException("Cloudinary:ApiSecret is not configured.");
        _logger = logger;
    }

    public async Task<string> SaveAsync(Guid ticketId, Stream content, string fileName, CancellationToken ct = default)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var folder = $"tickets/{ticketId:N}";
        // public_id is a bare guid: the download filename comes from the DB row,
        // so the original name never needs to live in the URL.
        var publicId = $"{Guid.NewGuid():N}";

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(_apiKey), "api_key");
        form.Add(new StringContent(timestamp.ToString()), "timestamp");
        form.Add(new StringContent(folder), "folder");
        form.Add(new StringContent(publicId), "public_id");
        form.Add(new StringContent(Sign($"folder={folder}&public_id={publicId}&timestamp={timestamp}")), "signature");
        // "auto" resource type lets Cloudinary pick image/video/raw from the bytes.
        using var file = new StreamContent(content);
        form.Add(file, "file", fileName);

        using var response = await _http.PostAsync(
            $"https://api.cloudinary.com/v1_1/{_cloudName}/auto/upload", form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Cloudinary upload returned {(int)response.StatusCode}: {(body.Length > 500 ? body[..500] : body)}");

        using var doc = JsonDocument.Parse(body);
        var secureUrl = doc.RootElement.GetProperty("secure_url").GetString()
            ?? throw new InvalidOperationException("Cloudinary upload response had no secure_url.");
        _logger.LogInformation("Attachment stored on Cloudinary for ticket {TicketId}: {FileName}", ticketId, fileName);
        return secureUrl;
    }

    public async Task<(Stream Content, string StoredPath)> OpenAsync(string storedPath, CancellationToken ct = default)
    {
        using var probe = await _http.GetAsync(storedPath, HttpCompletionOption.ResponseHeadersRead, ct);
        if (probe.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new FileNotFoundException("Attachment file is missing from storage.", storedPath);
        probe.EnsureSuccessStatusCode();
        // Buffer the payload (not just the headers): disposing the probe response
        // would kill any stream returned by ReadAsStreamAsync, and MVC reads the
        // stream AFTER this method returns. Attachments are capped at 5 MB
        // (TicketService BR-009), so in-memory buffering is the safe, simple call.
        var bytes = await probe.Content.ReadAsByteArrayAsync(ct);
        return (new MemoryStream(bytes, writable: false), storedPath);
    }

    public async Task DeleteAsync(string storedPath, CancellationToken ct = default)
    {
        // The DB stores the secure_url; destroy wants the resource type
        // (image/video/raw — taken from the URL, not guessed) + public_id.
        var (publicId, resourceType) = Extract(storedPath);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000;
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(_apiKey), "api_key");
        form.Add(new StringContent(timestamp.ToString()), "timestamp");
        form.Add(new StringContent(publicId), "public_id");
        form.Add(new StringContent(Sign($"public_id={publicId}&timestamp={timestamp}")), "signature");

        using var response = await _http.PostAsync(
            $"https://api.cloudinary.com/v1_1/{_cloudName}/{resourceType}/destroy", form, ct);
        // Idempotent like the local impl: missing file is success ("not found").
        if (!response.IsSuccessStatusCode)
            _logger.LogWarning("Cloudinary destroy returned {Status} for {PublicId}", response.StatusCode, publicId);
    }

    /// <summary>Recovers (public_id, resource_type) from a stored secure_url —
    /// e.g. .../image/upload/v123/tickets/abc/pub.png → ("tickets/abc/pub", "image").</summary>
    public static (string PublicId, string ResourceType) Extract(string secureUrl)
    {
        var uri = new Uri(secureUrl);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var uploadIndex = Array.IndexOf(segments, "upload");
        if (uploadIndex < 1 || uploadIndex + 1 >= segments.Length)
            throw new InvalidOperationException($"Unrecognized Cloudinary URL shape: {secureUrl}");
        var resourceType = segments[uploadIndex - 1]; // image | video | raw
        var withFolder = string.Join('/', segments[(uploadIndex + 1)..]).TrimEnd('/');
        // Drop the vNNNN version segment if present (first path segment only).
        var parts = withFolder.Split('/');
        if (parts.Length > 1 && parts[0].Length > 1 && parts[0][0] == 'v' && parts[0][1..].All(char.IsDigit))
            withFolder = string.Join('/', parts.Skip(1));
        // Strip the file extension Cloudinary appended.
        var slash = withFolder.LastIndexOf('/');
        var dot = withFolder.LastIndexOf('.');
        var publicId = dot > slash ? withFolder[..dot] : withFolder;
        return (publicId, resourceType);
    }

    /// <summary>Cloudinary signature: SHA1 of the alphabetically-sorted params + api secret.</summary>
    private string Sign(string sortedParams) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sortedParams + _apiSecret))).ToLowerInvariant();
}
