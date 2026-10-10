using Microsoft.EntityFrameworkCore;
using TicketDeskPro.Application.DTOs.Auth;
using TicketDeskPro.Application.Services;
using TicketDeskPro.Domain.Entities;
using TicketDeskPro.Shared.Exceptions;

namespace TicketDeskPro.UnitTests;

public class AuthServiceTests
{
    private static RegisterRequest ValidRegistration => new(
        "newuser@test.dev", "StrongPass1!", "New", "User", null);

    // ---------- Register ----------

    [Fact]
    public async Task Register_creates_user_with_hashed_password_and_customer_role()
    {
        using var f = new AuthTestFactory();
        await f.SeedCustomerRoleAsync();

        var result = await f.Auth.RegisterAsync(ValidRegistration);

        var user = await f.Db.Users.Include(u => u.UserRoles).FirstAsync(u => u.Id == result.UserId);
        Assert.NotEqual("StrongPass1!", user.PasswordHash);
        Assert.True(f.PasswordHasher.Verify("StrongPass1!", user.PasswordHash));
        Assert.Contains(user.UserRoles, ur => ur.Role.Name == "Customer");
    }

    [Fact]
    public async Task Register_duplicate_email_throws_Conflict()
    {
        using var f = new AuthTestFactory();
        await f.SeedCustomerRoleAsync();
        await f.Auth.RegisterAsync(ValidRegistration);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => f.Auth.RegisterAsync(ValidRegistration));
        Assert.Equal("CPL_001", ex.Code);
    }

    [Fact]
    public async Task Register_weak_password_throws_Validation()
    {
        using var f = new AuthTestFactory();
        await f.SeedCustomerRoleAsync();
        var weak = ValidRegistration with { Password = "abc" };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Auth.RegisterAsync(weak));
        Assert.Contains(ex.Failures, x => x.Field == "Password");
    }

    // ---------- Login & lockout ----------

    [Fact]
    public async Task Login_with_wrong_password_throws_Unauthorized_with_generic_message()
    {
        using var f = new AuthTestFactory();
        await f.SeedUserAsync();

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(
            () => f.Auth.LoginAsync(new LoginRequest("user@test.dev", "WrongPass1!"), "1.2.3.4"));

        Assert.Equal("AUTH_001", ex.Code);
        Assert.Equal("Invalid email or password.", ex.Message);
    }

    [Fact]
    public async Task Login_with_unknown_email_gives_same_generic_error_as_bad_password()
    {
        using var f = new AuthTestFactory();

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(
            () => f.Auth.LoginAsync(new LoginRequest("ghost@test.dev", "Whatever1!"), "1.2.3.4"));

        Assert.Equal("AUTH_001", ex.Code);
    }

    [Fact]
    public async Task Five_consecutive_failures_lock_the_account()
    {
        using var f = new AuthTestFactory();
        var user = await f.SeedUserAsync();

        for (var i = 0; i < AuthService.LockoutFailureThreshold; i++)
        {
            await Assert.ThrowsAsync<UnauthorizedException>(
                () => f.Auth.LoginAsync(new LoginRequest(user.Email, "bad"), "1.2.3.4"));
        }

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => f.Auth.LoginAsync(new LoginRequest(user.Email, "Password1!"), "1.2.3.4"));
        Assert.Equal("AUTH_005", ex.Code);
    }

    [Fact]
    public async Task Lock_still_active_at_10_minutes_when_failure_window_has_slid()
    {
        using var f = new AuthTestFactory();
        var user = await f.SeedUserAsync();

        // 5 failures 10 minutes ago: outside the 5-min counting window, but INSIDE the
        // 15-min lockout — the account must still be locked (audit bug regression test).
        for (var i = 0; i < AuthService.LockoutFailureThreshold; i++)
            f.Db.LoginAttempts.Add(new LoginAttempt
            {
                Email = user.Email,
                IsSuccess = false,
                CreatedAt = DateTime.UtcNow.AddMinutes(-10)
            });
        await f.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => f.Auth.LoginAsync(new LoginRequest(user.Email, "Password1!"), "1.2.3.4"));
        Assert.Equal("AUTH_005", ex.Code);
    }

    [Fact]
    public async Task Lock_expires_after_15_minutes_and_window_slides()
    {
        using var f = new AuthTestFactory();
        var user = await f.SeedUserAsync();

        // Old failures age out of the 5-minute window entirely.
        for (var i = 0; i < 6; i++)
            f.Db.LoginAttempts.Add(new LoginAttempt
            {
                Email = user.Email,
                IsSuccess = false,
                CreatedAt = DateTime.UtcNow.AddMinutes(-20)
            });
        await f.Db.SaveChangesAsync();

        var response = await f.Auth.LoginAsync(new LoginRequest(user.Email, "Password1!"), "1.2.3.4");
        Assert.False(string.IsNullOrEmpty(response.AccessToken));
    }

    [Fact]
    public async Task Successful_login_breaks_the_failure_streak()
    {
        using var f = new AuthTestFactory();
        var user = await f.SeedUserAsync();

        for (var i = 0; i < 4; i++)
            await Assert.ThrowsAsync<UnauthorizedException>(
                () => f.Auth.LoginAsync(new LoginRequest(user.Email, "bad"), "1.2.3.4"));

        await f.Auth.LoginAsync(new LoginRequest(user.Email, "Password1!"), "1.2.3.4"); // success resets streak
        var again = await f.Auth.LoginAsync(new LoginRequest(user.Email, "Password1!"), "1.2.3.4");

        Assert.NotNull(again.AccessToken);
    }

    [Fact]
    public async Task Login_is_rate_limited_when_limiter_says_so()
    {
        using var f = new AuthTestFactory();
        await f.SeedUserAsync();
        f.RateLimiter.ShouldLimit = true;

        var ex = await Assert.ThrowsAsync<RateLimitedException>(
            () => f.Auth.LoginAsync(new LoginRequest("user@test.dev", "Password1!"), "1.2.3.4"));
        Assert.Equal("RAT_001", ex.Code);
    }

    // ---------- Refresh rotation & reuse detection ----------

    [Fact]
    public async Task Refresh_rotates_tokens_keeps_family_and_returns_new_pair()
    {
        using var f = new AuthTestFactory();
        await f.SeedUserAsync();
        var login = await f.Auth.LoginAsync(new LoginRequest("user@test.dev", "Password1!"), "1.2.3.4");

        var refreshed = await f.Auth.RefreshAsync(login.RefreshToken, "1.2.3.4");

        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        Assert.False(string.IsNullOrEmpty(refreshed.AccessToken));

        var original = await f.Db.RefreshTokens.FirstAsync(t =>
            t.TokenHash == f.Tokens.HashToken(login.RefreshToken));
        var replacement = await f.Db.RefreshTokens.FirstAsync(t =>
            t.TokenHash == f.Tokens.HashToken(refreshed.RefreshToken));

        Assert.NotNull(original.RevokedAt);
        Assert.Equal(replacement.TokenHash, original.ReplacedByTokenHash);
        Assert.Equal(original.FamilyId, replacement.FamilyId); // same session family
    }

    [Fact]
    public async Task Reusing_a_revoked_token_kills_the_whole_family()
    {
        using var f = new AuthTestFactory();
        await f.SeedUserAsync();
        var login = await f.Auth.LoginAsync(new LoginRequest("user@test.dev", "Password1!"), "1.2.3.4");
        var refreshed = await f.Auth.RefreshAsync(login.RefreshToken, "1.2.3.4");

        // Attacker replays the old (already rotated) token.
        await Assert.ThrowsAsync<UnauthorizedException>(
            () => f.Auth.RefreshAsync(login.RefreshToken, "1.2.3.4"));

        // The legitimate new token is now dead too — theft forces a re-login.
        await Assert.ThrowsAsync<UnauthorizedException>(
            () => f.Auth.RefreshAsync(refreshed.RefreshToken, "1.2.3.4"));
    }

    [Fact]
    public async Task Revoke_kills_the_family_and_is_idempotent()
    {
        using var f = new AuthTestFactory();
        await f.SeedUserAsync();
        var login = await f.Auth.LoginAsync(new LoginRequest("user@test.dev", "Password1!"), "1.2.3.4");

        await f.Auth.RevokeAsync(login.RefreshToken);
        await f.Auth.RevokeAsync(login.RefreshToken); // second revoke = no-op, no throw

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => f.Auth.RefreshAsync(login.RefreshToken, "1.2.3.4"));
    }

    // ---------- Password reset ----------

    [Fact]
    public async Task ForgotPassword_writes_outbox_email_and_reset_flow_works_end_to_end()
    {
        using var f = new AuthTestFactory();
        await f.SeedUserAsync();
        var login = await f.Auth.LoginAsync(new LoginRequest("user@test.dev", "Password1!"), "1.2.3.4");

        await f.Auth.ForgotPasswordAsync(new ForgotPasswordRequest("user@test.dev"));

        var outboxEmail = await f.Db.EmailOutbox.SingleAsync(o => o.ToEmail == "user@test.dev");
        var rawToken = outboxEmail.Body.Split("token=")[1];

        await f.Auth.ResetPasswordAsync(new ResetPasswordRequest(rawToken, "BrandNew1!"));

        // Old password no longer works; new one does.
        await Assert.ThrowsAsync<UnauthorizedException>(
            () => f.Auth.LoginAsync(new LoginRequest("user@test.dev", "Password1!"), "1.2.3.4"));
        var relogin = await f.Auth.LoginAsync(new LoginRequest("user@test.dev", "BrandNew1!"), "1.2.3.4");
        Assert.NotNull(relogin.AccessToken);

        // Reset invalidates previously issued refresh tokens (session hijack protection).
        await Assert.ThrowsAsync<UnauthorizedException>(
            () => f.Auth.RefreshAsync(login.RefreshToken, "1.2.3.4"));
    }

    [Fact]
    public async Task Reset_with_wrong_or_expired_token_fails()
    {
        using var f = new AuthTestFactory();
        await f.SeedUserAsync();
        await f.Auth.ForgotPasswordAsync(new ForgotPasswordRequest("user@test.dev"));
        var outboxEmail = await f.Db.EmailOutbox.SingleAsync(o => o.ToEmail == "user@test.dev");
        var rawToken = outboxEmail.Body.Split("token=")[1];

        // Wrong token
        await Assert.ThrowsAsync<ValidationException>(() =>
            f.Auth.ResetPasswordAsync(new ResetPasswordRequest("deadbeef", "BrandNew1!")));

        // Expired token
        var user = await f.Db.Users.SingleAsync();
        user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() =>
            f.Auth.ResetPasswordAsync(new ResetPasswordRequest(rawToken, "BrandNew1!")));
    }

    [Fact]
    public async Task ForgotPassword_for_unknown_email_writes_nothing_and_does_not_throw()
    {
        using var f = new AuthTestFactory();

        await f.Auth.ForgotPasswordAsync(new ForgotPasswordRequest("nobody@test.dev"));

        Assert.Equal(0, await f.Db.EmailOutbox.CountAsync());
    }
}
