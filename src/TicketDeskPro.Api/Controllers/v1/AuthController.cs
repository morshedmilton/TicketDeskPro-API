using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketDeskPro.Application.DTOs.Auth;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Shared.Wrappers;

namespace TicketDeskPro.Api.Controllers.V1;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    /// <summary>Registers a new customer account (API Spec §2.1).</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<RegisterResponse>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await _auth.RegisterAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<RegisterResponse>.Ok(result));
    }

    /// <summary>Email + password login; returns the token pair (API Spec §2.2).</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _auth.LoginAsync(request, ip, ct);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }

    /// <summary>Exchanges a valid refresh token for a fresh token pair (rotation).</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _auth.RefreshAsync(request.RefreshToken, ip, ct);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }

    /// <summary>Logs out: revokes the whole refresh-token family. Idempotent.</summary>
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(RevokeRequest request, CancellationToken ct)
    {
        await _auth.RevokeAsync(request.RefreshToken, ct);
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
}
