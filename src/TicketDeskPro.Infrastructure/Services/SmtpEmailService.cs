using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TicketDeskPro.Application.Interfaces;

namespace TicketDeskPro.Infrastructure.Services;

/// <summary>
/// Plain SMTP sender (works with Resend, Brevo, Mailgun, or any provider that
/// exposes SMTP credentials). Reads SmtpSettings from configuration.
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration configuration, ILogger<SmtpEmailService> logger)
    {
        _settings = configuration.GetSection("SmtpSettings").Get<SmtpSettings>() ?? new SmtpSettings();
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.Host))
            throw new InvalidOperationException("SmtpSettings:Host is not configured.");

        using var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            EnableSsl = _settings.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        if (!string.IsNullOrWhiteSpace(_settings.Username))
            client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);

        using var message = new MailMessage
        {
            From = new MailAddress(_settings.FromAddress, _settings.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(toEmail);

        await client.SendMailAsync(message, ct);
        _logger.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
    }
}

public class SmtpSettings
{
    public const string SectionName = "SmtpSettings";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool UseSsl { get; set; } = true;
    public string FromAddress { get; set; } = "no-reply@ticketdesk.dev";
    public string FromName { get; set; } = "TicketDesk Pro";
}
