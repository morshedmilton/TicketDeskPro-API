namespace TicketDeskPro.Application.Options;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string Secret { get; set; } = null!;
    public string Issuer { get; set; } = "TicketDeskPro";
    public string Audience { get; set; } = "TicketDeskProClient";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}
