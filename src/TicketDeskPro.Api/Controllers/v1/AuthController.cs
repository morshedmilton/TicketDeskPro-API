using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TicketDeskPro.Application.DTOs.Auth;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Application.Options;
using TicketDeskPro.Shared.Constants;
using TicketDeskPro.Shared.Exceptions;
using TicketDeskPro.Shared.Wrappers;

namespace TicketDeskPro.Api.Controllers.V1;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    // Cookie-only refresh token: JavaScript can never read it (XSS hardening).
    // Scoped to /api/v1/auth so it only travels to login/refresh/revoke.
    private const string RefreshCookieName = "tdp_refresh";
    private const string RefreshCookiePath = "/api/v1/auth";

    private readonly IAuthService _auth;
    private readonly JwtSettings _jwt;
    private readonly IConfiguration _configuration;

    public AuthController(IAuthService auth, IOptions<JwtSettings> jwt, IConfiguration configuration)
    {
        _auth = auth;
        _jwt = jwt.Value;
        _configuration = configuration;
    }

    /// <summary>Registers a new customer account (API Spec §2.1).</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<RegisterResponse>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await _auth.RegisterAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<RegisterResponse>.Ok(result));
    }

    /// <summary>Email + password login (API Spec §2.2). The refresh token is set as an
    /// HttpOnly cookie — the JSON body carries only the access token and user.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        RejectForeignOrigin(); // login CSRF: an attacker page must not force-login a victim
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _auth.LoginAsync(request, ip, ct);
        WriteRefreshCookie(result.RefreshToken);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }

    /// <summary>Exchanges the refresh cookie for a fresh token pair (rotation).
    /// No body — the browser sends the HttpOnly cookie automatically.</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        // The SameSite=None cookie rides on simple POSTs, so a foreign page could
        // force-refresh (rotate) our tokens — CSRF. Only our client may talk here.
        RejectForeignOrigin();
        if (!Request.Cookies.TryGetValue(RefreshCookieName, out var refresh) ||
            string.IsNullOrWhiteSpace(refresh))
        {
            throw new UnauthorizedException(ErrorCodes.InvalidTokenState, "No session. Please log in again.");
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _auth.RefreshAsync(refresh, ip, ct);
        // Rotation: overwrite the cookie with the new token.
        // ponytail: a failed refresh leaves the stale cookie in place — harmless,
        // the next attempt 401s and login overwrites it. Add a clear-on-401 hook
        // only if that ever becomes annoying.
        WriteRefreshCookie(result.RefreshToken);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }

    /// <summary>Logs out: revokes the family behind the refresh cookie and clears it.
    /// Idempotent — a missing cookie still returns 200.</summary>
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(CancellationToken ct)
    {
        RejectForeignOrigin(); // force-logout CSRF via a foreign page's POST
        if (Request.Cookies.TryGetValue(RefreshCookieName, out var refresh) &&
            !string.IsNullOrWhiteSpace(refresh))
        {
            await _auth.RevokeAsync(refresh, ct);
        }

        if (Request.IsHttps)
        {
            // Delete must carry the same 'partitioned' attribute the cookie was
            // set with (CHIPS), or it only clears the unpartitioned jar.
            Response.Headers.Append("Set-Cookie",
                $"{RefreshCookieName}=; expires=Thu, 01 Jan 1970 00:00:00 GMT; " +
                $"path={RefreshCookiePath}; samesite=none; secure; httponly; partitioned");
        }
        else
        {
            Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = RefreshCookiePath });
        }
        return Ok(ApiResponse<object>.Ok(new { message = "Token revoked." }));
    }

    /// <summary>Starts password reset; always 200 whether or not the email exists.</summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await _auth.ForgotPasswordAsync(request, ct);
        return Ok(ApiResponse<object>.Ok(new { message = "If that email is registered, a reset link has been sent." }));
    }

    /// <summary>Completes password reset with the emailed token.</summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await _auth.ResetPasswordAsync(request, ct);
        return Ok(ApiResponse<object>.Ok(new { message = "Password updated. Please log in." }));
    }

    /// <summary>Current user profile: roles + permissions (US-1.06).</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct) =>
        Ok(ApiResponse<UserProfileResponse>.Ok(await _auth.GetMeAsync(ct)));

    /// <summary>Updates the current user's name/phone (US-1.06).</summary>
    [Authorize]
    [HttpPatch("me")]
    public async Task<IActionResult> UpdateMe(UpdateProfileRequest request, CancellationToken ct) =>
        Ok(ApiResponse<UserProfileResponse>.Ok(await _auth.UpdateMeAsync(request, ct)));

    private void WriteRefreshCookie(string token)
    {
        // Render/Vercel are different sites → cross-site XHR needs None+Secure.
        // Local http://localhost:3000→5000 is the SAME site (port is irrelevant to
        // SameSite) and Chrome rejects None without Secure → Lax there.
        var expires = DateTimeOffset.UtcNow.AddDays(_jwt.RefreshTokenDays);
        if (Request.IsHttps)
        {
            // Raw header: .NET 10.0.401 has no CookieOptions.Partitioned, but the
            // 'partitioned' attribute (CHIPS) keeps this cookie alive as browsers
            // tighten third-party rules (Chrome/Firefox). The token is base64url
            // — no escaping needed. Switch to CookieOptions.Partitioned when the
            // SDK ships it.
            // ponytail ceiling: Safari drops third-party cookies entirely —
            // make this first-party (client on the same apex domain, or proxy
            // /auth/* through the Next.js server) if Safari users matter.
            Response.Headers.Append("Set-Cookie",
                $"{RefreshCookieName}={token}; expires={expires.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}; " +
                $"path={RefreshCookiePath}; samesite=none; secure; httponly; partitioned");
            return;
        }

        Response.Cookies.Append(RefreshCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Path = RefreshCookiePath,
            Expires = expires,
            SameSite = SameSiteMode.Lax
        });
    }

    /// <summary>Browser POSTs carry an Origin header; only our frontend (or this
    /// API itself, e.g. Swagger UI) may call the cookie endpoints. Non-browser
    /// clients (no Origin: curl, tests) are allowed — they cannot carry cookies.
    /// CSRF defense for the SameSite=None cookie (auth is exempt from CORS
    /// preflight because these are body-less simple requests).</summary>
    private void RejectForeignOrigin()
    {
        if (!Request.Headers.TryGetValue("Origin", out var origin)) return;
        var frontend = _configuration["FRONTEND_URL"]?.TrimEnd('/');
        var self = $"{Request.Scheme}://{Request.Host}";
        if (origin != frontend && origin != self)
            throw new ForbiddenException(ErrorCodes.CsrfOriginRejected, "Origin not allowed.");
    }
}
