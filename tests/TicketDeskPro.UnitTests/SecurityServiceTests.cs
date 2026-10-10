using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TicketDeskPro.Application.Options;
using TicketDeskPro.Infrastructure.Services;

namespace TicketDeskPro.UnitTests;

public class SecurityServiceTests
{
    private static TokenService CreateTokenService(int minutes = 15) => new(Options.Create(new JwtSettings
    {
        Secret = new string('k', 64),
        Issuer = "TicketDeskPro",
        Audience = "TicketDeskProClient",
        AccessTokenMinutes = minutes
    }));

    [Fact]
    public void AccessToken_contains_sub_email_role_and_permission_claims()
    {
        var service = CreateTokenService();

        var access = service.GenerateAccessToken(Guid.NewGuid(), "a@b.dev",
            ["Agent", "Admin"], ["tickets.read", "tickets.assign"]);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(access.Token);
        Assert.Equal("TicketDeskPro", jwt.Issuer);
        Assert.Contains(jwt.Claims, c => c.Type == "sub");
        Assert.Contains(jwt.Claims, c => c.Type == "email" && c.Value == "a@b.dev");
        Assert.Equal(2, jwt.Claims.Count(c => c.Type == "role"));
        Assert.Equal(2, jwt.Claims.Count(c => c.Type == "permissions"));
    }

    [Fact]
    public void AccessToken_expires_after_configured_minutes()
    {
        var service = CreateTokenService(minutes: 15);
        var before = DateTime.UtcNow;

        var access = service.GenerateAccessToken(Guid.NewGuid(), "a@b.dev", [], []);

        var expected = before.AddMinutes(15);
        Assert.True(access.ExpiresAt >= expected.AddSeconds(-30) && access.ExpiresAt <= expected.AddSeconds(30));
    }

    [Fact]
    public void AccessToken_is_HMAC_SHA256_signed()
    {
        var service = CreateTokenService();
        var access = service.GenerateAccessToken(Guid.NewGuid(), "a@b.dev", [], []);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(access.Token);
        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.SignatureAlgorithm);
    }

    [Fact]
    public void RefreshTokens_are_unique_and_hash_is_stable_sha256()
    {
        var service = CreateTokenService();

        var t1 = service.GenerateRefreshToken();
        var t2 = service.GenerateRefreshToken();

        Assert.NotEqual(t1, t2);
        Assert.Equal(64, service.HashToken(t1).Length); // sha256 hex = 64 chars
        Assert.Equal(service.HashToken(t1), service.HashToken(t1));
        Assert.NotEqual(service.HashToken(t1), service.HashToken(t2));
    }
}
