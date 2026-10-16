using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TicketDeskPro.Application.Interfaces;

namespace TicketDeskPro.Infrastructure.Services;

/// <summary>
/// Sends mail through Brevo's HTTPS API (port 443). SMTP (ports 25/465/587) is
/// blocked on Render's free tier since Sep 2025 — see render.com/changelog
/// "Free web services will no longer allow outbound traffic to SMTP ports" —
/// so the outbox delivers over HTTPS instead. Reads EmailSettings from config.
/// </summary>
public class BrevoEmailService : IEmailService
{
    // ponytail: one outbound host, one call site — a static client is enough.
    // Upgrade path: IHttpClientFactory (typed client) if a second provider ever appears.
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly HttpClient _http;
    private readonly EmailSettings _settings;
    private readonly ILogger<BrevoEmailService> _logger;

    // httpClient: test seam — production DI leaves it null and the static client is used.
    public BrevoEmailService(IConfiguration configuration, ILogger<BrevoEmailService> logger, HttpClient? httpClient = null)
    {
        _http = httpClient ?? Http;
        _settings = configuration.GetSection(EmailSettings.SectionName).Get<EmailSettings>() ?? new EmailSettings();
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            throw new InvalidOperationException("EmailSettings:ApiKey is not configured.");
        if (string.IsNullOrWhiteSpace(_settings.FromAddress))
            throw new InvalidOperationException("EmailSettings:FromAddress is not configured.");

        var payload = new
        {
            sender = new { name = _settings.FromName, email = _settings.FromAddress },
            to = new[] { new { email = toEmail } },
            subject,
            htmlContent = htmlBody
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("api-key", _settings.ApiKey);

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Brevo API returned {(int)response.StatusCode}: {(body.Length > 500 ? body[..500] : body)}");
        }

        _logger.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
    }
}

public class EmailSettings
{
    public const string SectionName = "EmailSettings";

    public string ApiKey { get; set; } = "";
    public string FromName { get; set; } = "TicketDesk Pro";
    public string FromAddress { get; set; } = "";
}
