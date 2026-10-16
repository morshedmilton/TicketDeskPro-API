using System.Net;
using Xunit;

namespace TicketDeskPro.IntegrationTests;

/// <summary>
/// The credentialed CORS contract the cookie flow depends on: the frontend
/// origin is echoed with Access-Control-Allow-Credentials, foreign origins get
/// nothing. Policy: FRONTEND_URL ?? http://localhost:3000 (Program.cs).
/// </summary>
public class CorsTests : IClassFixture<TicketApiFactory>
{
    private const string FrontendOrigin = "http://localhost:3000";
    private readonly TicketApiFactory _factory;

    public CorsTests(TicketApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Preflight_from_frontend_origin_is_allowed_with_credentials()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        request.Headers.Add("Origin", FrontendOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await client.SendAsync(request);

        Assert.True(response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK);
        Assert.Equal(FrontendOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        Assert.Contains("POST", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
    }

    [Fact]
    public async Task Preflight_from_foreign_origin_gets_no_allow_origin()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        request.Headers.Add("Origin", "https://evil.com");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.TryGetValues("Access-Control-Allow-Origin", out _));
    }

    [Fact]
    public async Task Simple_request_from_frontend_origin_carries_credential_headers()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/health/healthz");
        request.Headers.Add("Origin", FrontendOrigin);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(FrontendOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
    }
}
