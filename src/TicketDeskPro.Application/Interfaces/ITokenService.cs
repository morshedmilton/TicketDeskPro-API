namespace TicketDeskPro.Application.Interfaces;

public record AccessToken(string Token, DateTime ExpiresAt);

public interface ITokenService
{
    /// <summary>Creates a signed JWT with sub/email/role/permission claims (HMAC-SHA256).</summary>
    AccessToken GenerateAccessToken(Guid userId, string email, IEnumerable<string> roles, IEnumerable<string> permissions);

    /// <summary>Cryptographically random opaque refresh token (raw value shown to the client once).</summary>
    string GenerateRefreshToken();

    /// <summary>SHA256 hex of a token — only this is stored in the database.</summary>
    string HashToken(string token);
}
