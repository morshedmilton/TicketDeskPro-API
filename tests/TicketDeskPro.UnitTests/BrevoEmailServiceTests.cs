using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TicketDeskPro.Infrastructure.Services;
using Xunit;

namespace TicketDeskPro.UnitTests;

/// <summary>
/// The outbox's only outbound edge — pins the Brevo request contract:
/// endpoint, api-key header, sender/to/subject/html payload, and error propagation.
/// </summary>
public class BrevoEmailServiceTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public HttpStatusCode ResponseStatus { get; set; } = HttpStatusCode.Accepted;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(ResponseStatus);
        }
    }

    private static BrevoEmailService ServiceWith(CapturingHandler handler, string? apiKey = "key-123", string? from = "desk@ticketdesk.dev")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EmailSettings:ApiKey"] = apiKey,
            ["EmailSettings:FromAddress"] = from,
            ["EmailSettings:FromName"] = "TicketDesk Pro"
        }).Build();
        return new BrevoEmailService(config, NullLogger<BrevoEmailService>.Instance, new HttpClient(handler));
    }

    [Fact]
    public async Task SendAsync_posts_brevo_payload_with_api_key_header()
    {
        var handler = new CapturingHandler();
        var service = ServiceWith(handler);

        await service.SendAsync("user@example.com", "Hello", "<b>hi</b>");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://api.brevo.com/v3/smtp/email", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("key-123", handler.LastRequest.Headers.GetValues("api-key").Single());
        Assert.Contains("\"email\":\"user@example.com\"", handler.LastBody);
        Assert.Contains("\"subject\":\"Hello\"", handler.LastBody);
        // System.Text.Json escapes HTML angles in the body — payload is JSON-equivalent either way.
        Assert.Contains("\"htmlContent\":", handler.LastBody);
        Assert.Contains("hi", handler.LastBody);
        Assert.Contains("\"email\":\"desk@ticketdesk.dev\"", handler.LastBody);
    }

    [Fact]
    public async Task SendAsync_throws_with_status_when_brevo_rejects()
    {
        var handler = new CapturingHandler { ResponseStatus = HttpStatusCode.Unauthorized };
        var service = ServiceWith(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendAsync("user@example.com", "s", "b"));

        Assert.Contains("401", ex.Message);
        Assert.Contains("Brevo", ex.Message);
    }

    [Fact]
    public async Task SendAsync_fails_fast_when_api_key_missing()
    {
        var service = ServiceWith(new CapturingHandler(), apiKey: "");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendAsync("user@example.com", "s", "b"));
    }
}
