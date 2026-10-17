using TicketDeskPro.Application.DTOs.Auth;
using TicketDeskPro.Shared.Exceptions;
using Xunit;

namespace TicketDeskPro.UnitTests;

/// <summary>
/// Bucketed auth throttling (security findings BE-M1/M3): register and
/// forgot-password go through the same sliding-window limiter as login but in
/// their own per-email buckets, so a reset flood cannot lock the victim out of login.
/// </summary>
public class AuthThrottleTests : IDisposable
{
    private readonly AuthTestFactory _f = new();
    public void Dispose() => _f.Dispose();

    private static RegisterRequest NewRegister() =>
        new($"r-{Guid.NewGuid():N}@ticketdesk.test".ToLowerInvariant(), "Passw0rd!x", "Reg", "Tester", null);

    [Fact]
    public async Task Register_is_throttled_when_the_register_bucket_is_full()
    {
        await _f.SeedCustomerRoleAsync();
        _f.RateLimiter.ShouldLimit = true;

        await Assert.ThrowsAsync<RateLimitedException>(() => _f.Auth.RegisterAsync(NewRegister()));
        Assert.Equal("register", _f.RateLimiter.LastBucket);
    }

    [Fact]
    public async Task Forgot_password_is_throttled_when_the_forgot_bucket_is_full()
    {
        _f.RateLimiter.ShouldLimit = true;

        await Assert.ThrowsAsync<RateLimitedException>(
            () => _f.Auth.ForgotPasswordAsync(new ForgotPasswordRequest("victim@ticketdesk.test")));
        Assert.Equal("forgot", _f.RateLimiter.LastBucket);
    }

    [Fact]
    public async Task Forgot_password_uses_its_own_bucket_not_the_login_window()
    {
        // Not limited: register + forgot each record into distinct buckets.
        await _f.SeedCustomerRoleAsync();
        await _f.Auth.RegisterAsync(NewRegister());
        await _f.Auth.ForgotPasswordAsync(new ForgotPasswordRequest("someone@ticketdesk.test"));

        Assert.Equal(2, _f.RateLimiter.CheckCallCount);
        Assert.Equal("forgot", _f.RateLimiter.LastBucket); // distinct from "register" seen first
    }
}
