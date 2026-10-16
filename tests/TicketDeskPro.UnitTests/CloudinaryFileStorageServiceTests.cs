using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TicketDeskPro.Infrastructure.Services;
using Xunit;

namespace TicketDeskPro.UnitTests;

/// <summary>
/// Pins the Cloudinary REST contract: signed upload fields (SHA1 params+secret),
/// secure_url as the stored value, and public_id recovery from a stored URL.
/// </summary>
public class CloudinaryFileStorageServiceTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public HttpStatusCode ResponseStatus { get; set; } = HttpStatusCode.OK;
        public string ResponseJson { get; set; } =
            """{"public_id":"tickets/abc123def4567890abcdef1234567890a","secure_url":"https://res.cloudinary.com/demo/image/upload/v1712345678/tickets/abc123def4567890abcdef1234567890a/deadbeef.png"}""";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(ResponseStatus)
            {
                Content = new StringContent(ResponseJson)
            };
        }
    }

    private static CloudinaryFileStorageService ServiceWith(CapturingHandler handler)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cloudinary:CloudName"] = "demo",
            ["Cloudinary:ApiKey"] = "key123",
            ["Cloudinary:ApiSecret"] = "shhh-secret"
        }).Build();
        return new CloudinaryFileStorageService(config, NullLogger<CloudinaryFileStorageService>.Instance, new HttpClient(handler));
    }

    private static string Field(string body, string name)
    {
        // Parts may carry extra headers (e.g. Content-Type on StringContent) —
        // skip everything up to the blank line before the value.
        // .NET omits quotes around simple names (name=api_key) — match both forms.
        var match = System.Text.RegularExpressions.Regex.Match(
            body, $"name=\"?{name}\"?[\\s\\S]*?\\r?\\n\\r?\\n([\\s\\S]*?)\\r?\\n--");
        return match.Success
            ? match.Groups[1].Value.Trim()
            : throw new Xunit.Sdk.XunitException($"field {name} not found. Body starts: {body[..Math.Min(500, body.Length)].Replace("\r", "\\r").Replace("\n", "\\n")}");
    }

    [Fact]
    public async Task SaveAsync_posts_signed_upload_and_returns_secure_url()
    {
        var handler = new CapturingHandler();
        var service = ServiceWith(handler);
        var ticketId = Guid.NewGuid();

        await using var content = new MemoryStream("hello attachment"u8.ToArray());
        var stored = await service.SaveAsync(ticketId, content, "photo.png");

        Assert.StartsWith("https://res.cloudinary.com/demo/", stored);
        Assert.Equal("https://res.cloudinary.com/demo/image/upload/v1712345678/tickets/abc123def4567890abcdef1234567890a/deadbeef.png", stored);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal($"https://api.cloudinary.com/v1_1/demo/auto/upload", handler.LastRequest!.RequestUri!.ToString());
        var body = handler.LastBody!;
        Assert.Equal("key123", Field(body, "api_key"));
        Assert.Equal($"tickets/{ticketId:N}", Field(body, "folder"));
        Assert.Contains("photo.png", body); // original filename rides as the file part

        // Signature = SHA1 of the sorted params + secret, computed independently.
        var parameters = $"folder={Field(body, "folder")}&public_id={Field(body, "public_id")}&timestamp={Field(body, "timestamp")}";
        var expected = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(parameters + "shhh-secret"))).ToLowerInvariant();
        Assert.Equal(expected, Field(body, "signature"));
    }

    [Fact]
    public async Task SaveAsync_surfaces_the_status_when_cloudinary_rejects()
    {
        var handler = new CapturingHandler
        {
            ResponseStatus = HttpStatusCode.Unauthorized,
            ResponseJson = """{"error":{"message":"Invalid api_key"}}"""
        };
        var service = ServiceWith(handler);

        await using var content = new MemoryStream("x"u8.ToArray());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SaveAsync(Guid.NewGuid(), content, "a.txt"));

        Assert.Contains("401", ex.Message);
        Assert.Contains("Cloudinary", ex.Message);
    }

    [Theory]
    [InlineData("https://res.cloudinary.com/demo/image/upload/v1712345678/tickets/abc/pub.png", "tickets/abc/pub", "image")]
    [InlineData("https://res.cloudinary.com/demo/raw/upload/tickets/abc/pub.txt", "tickets/abc/pub", "raw")]
    [InlineData("https://res.cloudinary.com/demo/image/upload/v1/pub.png", "pub", "image")]
    public void Extract_recovers_public_id_and_resource_type_from_a_stored_url(string url, string expectedId, string expectedType)
    {
        var (publicId, resourceType) = CloudinaryFileStorageService.Extract(url);
        Assert.Equal(expectedId, publicId);
        Assert.Equal(expectedType, resourceType);
    }
}
