using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TicketDeskPro.IntegrationTests;

/// <summary>
/// Cookie-only refresh contract (HttpOnly hardening): the tdp_refresh token rides
/// in an HttpOnly Set-Cookie scoped to /api/v1/auth and must never appear in JSON;
/// refresh/revoke read the cookie; refresh without it is a clean 401.
/// </summary>
public class AuthCookieTests : IClassFixture<TicketApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly TicketApiFactory _factory;

    public AuthCookieTests(TicketApiFactory factory) => _factory = factory;

    /// <summary>Registers a fresh user and logs in. Returns the raw tdp_refresh Set-Cookie string.</summary>
    private async Task<(HttpClient Client, string AccessToken, string SetCookie)> LoginAsync(HttpClient? client = null)
    {
        client ??= _factory.CreateClient();
        var email = $"cookie-{Guid.NewGuid():N}@ticketdesk.test";
        var password = "Passw0rd!x";

        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { email, password, firstName = "Cookie", lastName = "Tester" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var json = await login.Content.ReadAsStringAsync();
        Assert.DoesNotContain("refreshToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("accessToken", json, StringComparison.Ordinal);

        var doc = JsonSerializer.Deserialize<JsonElement>(json, Json);
        var accessToken = doc.GetProperty("data").GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        var setCookie = login.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("tdp_refresh=", StringComparison.Ordinal));
        return (client, accessToken!, setCookie);
    }

    private static string CookieHeader(string setCookie) => setCookie.Split(';', 2)[0];

    [Fact]
    public async Task Login_sets_httponly_scoped_cookie_and_keeps_refresh_token_out_of_json()
    {
        var (_, _, setCookie) = await LoginAsync();

        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/api/v1/auth", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_behind_tls_proxy_sets_secure_partitioned_none_cookie()
    {
        // Production path: Render terminates TLS and ForwardedHeaders restores
        // IsHttps → the cookie MUST become Secure+None+Partitioned (cross-site).
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        var (_, _, setCookie) = await LoginAsync(client);

        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=none", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("partitioned", setCookie, StringComparison.OrdinalIgnoreCase);

        // The delete cookie must also be partitioned or CHIPS jars keep the token.
        client.DefaultRequestHeaders.Add("Cookie", CookieHeader(setCookie));
        var revoke = await client.PostAsync("/api/v1/auth/revoke", null);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        var cleared = ExtractRefreshCookie(revoke);
        Assert.StartsWith("tdp_refresh=;", cleared, StringComparison.Ordinal);
        Assert.Contains("partitioned", cleared, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_without_cookie_is_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_with_cookie_rotates_both_tokens()
    {
        var (client, accessToken, setCookie) = await LoginAsync();
        client.DefaultRequestHeaders.Add("Cookie", CookieHeader(setCookie));

        var response = await client.PostAsync("/api/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("refreshToken", json, StringComparison.OrdinalIgnoreCase);
        var doc = JsonSerializer.Deserialize<JsonElement>(json, Json);
        var fresh = doc.GetProperty("data").GetProperty("accessToken").GetString();
        Assert.NotEqual(accessToken, fresh);

        // Rotation ships a NEW cookie value too.
        Assert.NotEqual(CookieHeader(setCookie), CookieHeader(ExtractRefreshCookie(response)));
    }

    [Fact]
    public async Task Revoke_expires_the_cookie_and_kills_the_family()
    {
        var (client, _, setCookie) = await LoginAsync();
        var cookie = CookieHeader(setCookie);
        client.DefaultRequestHeaders.Add("Cookie", cookie);

        var response = await client.PostAsync("/api/v1/auth/revoke", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cleared = ExtractRefreshCookie(response);
        Assert.StartsWith("tdp_refresh=;", cleared, StringComparison.Ordinal);
        Assert.Contains("1970", cleared, StringComparison.OrdinalIgnoreCase); // expiry in the past

        // Second revoke with the (now dead) cookie still returns 200 — idempotent.
        var again = await client.PostAsync("/api/v1/auth/revoke", null);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        // The revoked token cannot refresh: the whole family is dead.
        var refresh = await client.PostAsync("/api/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    private static string ExtractRefreshCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("tdp_refresh=", StringComparison.Ordinal));
}
