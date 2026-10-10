using System.Security.Cryptography;
using FluentValidation;
using Microsoft.Extensions.Options;
using TicketDeskPro.Application.DTOs.Auth;
using TicketDeskPro.Application.Extensions;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Application.Options;
using TicketDeskPro.Domain.Entities;
using TicketDeskPro.Domain.Interfaces;
using TicketDeskPro.Shared.Constants;
using TicketDeskPro.Shared.Exceptions;
using ValidationException = TicketDeskPro.Shared.Exceptions.ValidationException;
using ValidationFailure = TicketDeskPro.Shared.Exceptions.ValidationFailure;

namespace TicketDeskPro.Application.Services;

public class AuthService : IAuthService
{
    // BR-005: five consecutive failures inside this window lock the account for 15 minutes.
    public const int LockoutFailureThreshold = 5;
    public const double LockoutWindowMinutes = 5;
    public const double LockoutDurationMinutes = 15;

    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ILoginAttemptRepository _loginAttempts;
    private readonly IEmailOutboxRepository _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly ILoginRateLimiter _rateLimiter;
    private readonly ICurrentUserService _currentUser;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IValidator<UpdateProfileRequest> _updateProfileValidator;
    private readonly IValidator<ResetPasswordRequest> _resetPasswordValidator;
    private readonly IValidator<ForgotPasswordRequest> _forgotPasswordValidator;
    private readonly JwtSettings _jwt;
    private readonly string _frontendUrl;

    public AuthService(
        IUserRepository users,
        IRoleRepository roles,
        IRefreshTokenRepository refreshTokens,
        ILoginAttemptRepository loginAttempts,
        IEmailOutboxRepository outbox,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        ILoginRateLimiter rateLimiter,
        ICurrentUserService currentUser,
        IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator,
        IValidator<UpdateProfileRequest> updateProfileValidator,
        IValidator<ResetPasswordRequest> resetPasswordValidator,
        IValidator<ForgotPasswordRequest> forgotPasswordValidator,
        IOptions<JwtSettings> jwt,
        string frontendUrl)
    {
        _users = users;
        _roles = roles;
        _refreshTokens = refreshTokens;
        _loginAttempts = loginAttempts;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _rateLimiter = rateLimiter;
        _currentUser = currentUser;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _updateProfileValidator = updateProfileValidator;
        _resetPasswordValidator = resetPasswordValidator;
        _forgotPasswordValidator = forgotPasswordValidator;
        _jwt = jwt.Value;
        _frontendUrl = frontendUrl;
        _dummyPasswordHash = passwordHasher.Hash(Guid.NewGuid().ToString());
    }

    /// <summary>
    /// Real BCrypt hash (of a random throwaway value) verified against when the email is
    /// unknown, so the unknown-email path costs the same CPU time as a wrong password.
    /// </summary>
    private readonly string _dummyPasswordHash;

    /// <summary>How far back lockout analysis must look: failure window + lockout duration.</summary>
    private static readonly TimeSpan LockoutLookback = TimeSpan.FromMinutes(LockoutWindowMinutes + LockoutDurationMinutes + 1);

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        await _registerValidator.EnsureValidAsync(request, ct);

        var email = NormalizeEmail(request.Email);

        if (await _users.AnyByEmailAsync(email, ct))
            throw new ConflictException(ErrorCodes.EmailExists, "A user with this email already exists.");

        var customerRole = await _roles.GetByNameAsync(AppRoles.Customer, ct)
            ?? throw new InvalidOperationException("Customer role is missing — seed data must run before registration.");

        var user = new User
        {
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Phone = request.Phone,
            IsActive = true
        };
        user.UserRoles.Add(new UserRole { User = user, Role = customerRole });

        await _users.AddAsync(user, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return new RegisterResponse(user.Id, "Registration successful.");
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string ipAddress, CancellationToken ct = default)
    {
        await _loginValidator.ValidateAndThrowAsync(request, ct);

        var email = NormalizeEmail(request.Email);

        await _rateLimiter.CheckAndRecordAsync(ipAddress, email, ct);

        var user = await _users.GetByEmailWithRolesAsync(email, ct);

        // Same generic error for unknown email and wrong password — never leak which one was wrong.
        var invalid = new UnauthorizedException(ErrorCodes.InvalidCredentials, "Invalid email or password.");

        if (user is null)
        {
            // Burn the same BCrypt cost as a real verification — timing must not reveal valid emails.
            _passwordHasher.Verify(request.Password, _dummyPasswordHash);
            await RecordAttemptAsync(email, ipAddress, success: false, ct);
            throw invalid;
        }

        if (!user.IsActive)
        {
            await RecordAttemptAsync(email, ipAddress, success: false, ct);
            throw new ForbiddenException(ErrorCodes.UserLocked, "This account has been deactivated.");
        }

        await ThrowIfLockedAsync(email, ct);

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            await RecordAttemptAsync(email, ipAddress, success: false, ct);
            throw invalid;
        }

        await RecordAttemptAsync(email, ipAddress, success: true, ct);
        await _rateLimiter.ResetAsync(ipAddress, email, ct); // FRD §5: reset counter on success
        return await IssueTokensAsync(user, ipAddress, familyId: null, ct);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, string ipAddress, CancellationToken ct = default)
    {
        var hash = _tokenService.HashToken(refreshToken);
        var stored = await _refreshTokens.GetByHashAsync(hash, ct);
        var invalidState = new UnauthorizedException(ErrorCodes.InvalidTokenState, "Invalid token state. Please log in again.");

        if (stored is null || stored.User is null || stored.User.IsDeleted)
            throw invalidState;

        // Reuse of an already-rotated token = the token leaked. Kill the whole family.
        if (stored.RevokedAt is not null)
        {
            var family = await _refreshTokens.GetActiveByFamilyAsync(stored.FamilyId, ct);
            foreach (var token in family)
                token.RevokedAt ??= DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
            throw invalidState;
        }

        if (stored.ExpiresAt <= DateTime.UtcNow)
            throw new UnauthorizedException(ErrorCodes.TokenExpired, "Token has expired. Please log in again.");

        if (!stored.User.IsActive)
            throw new ForbiddenException(ErrorCodes.UserLocked, "This account has been deactivated.");

        var user = await _users.GetByIdWithRolesAsync(stored.User.Id, ct)
            ?? throw invalidState;

        // Rotation is one atomic unit of work in the repository (xmin concurrency guard):
        // exactly one of two parallel refreshes with the same token wins; the loser gets 401 (BR-004).
        var rawRefresh = _tokenService.GenerateRefreshToken();
        var rotated = await _refreshTokens.RotateAsync(
            hash, _tokenService.HashToken(rawRefresh), user.Id, stored.FamilyId, ipAddress,
            DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays), ct);

        if (!rotated)
        {
            var family = await _refreshTokens.GetActiveByFamilyAsync(stored.FamilyId, ct);
            foreach (var token in family)
                token.RevokedAt ??= DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
            throw invalidState;
        }

        var roles = user.UserRoles.Select(ur => ur.Role.Name).Distinct().ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Name)
            .Distinct()
            .ToList();
        var access = _tokenService.GenerateAccessToken(user.Id, user.Email, roles, permissions);

        return new AuthResponse(access.Token, rawRefresh,
            new AuthUserDto(user.Id, user.Email, user.FirstName, user.LastName, roles));
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = _tokenService.HashToken(refreshToken);
        var stored = await _refreshTokens.GetByHashAsync(hash, ct);
        if (stored is null) return; // idempotent logout

        var family = await _refreshTokens.GetActiveByFamilyAsync(stored.FamilyId, ct);
        var now = DateTime.UtcNow;
        foreach (var token in family)
            token.RevokedAt ??= now;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        await _forgotPasswordValidator.ValidateAndThrowAsync(request, ct);

        var email = NormalizeEmail(request.Email);
        var user = await _users.GetByEmailWithRolesAsync(email, ct);

        if (user is not null)
        {
            var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

            user.PasswordResetTokenHash = _tokenService.HashToken(rawToken);
            user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddHours(1); // BR-006

            // Written to the outbox in the same transaction; the worker sends it (Phase 4).
            await _outbox.AddAsync(new EmailOutbox
            {
                ToEmail = user.Email,
                Subject = "Reset your TicketDesk password",
                Body = $"Reset your password within 1 hour: {_frontendUrl}/reset-password?token={rawToken}"
            }, ct);

            await _unitOfWork.SaveChangesAsync(ct);
        }

        // Always the same response — never reveal whether the email exists.
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        await _resetPasswordValidator.EnsureValidAsync(request, ct);

        var hash = _tokenService.HashToken(request.Token);
        var user = await _users.GetByPasswordResetHashAsync(hash, ct);

        if (user is null || user.PasswordResetTokenExpiresAt is null || user.PasswordResetTokenExpiresAt <= DateTime.UtcNow)
            throw new ValidationException("One or more validation errors occurred.",
                [new ValidationFailure("token", "Password reset link is invalid or has expired.")]);

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAt = null;

        // Changing the password invalidates every active session (BR security baseline).
        var activeTokens = await _refreshTokens.GetActiveByUserAsync(user.Id, ct);
        var now = DateTime.UtcNow;
        foreach (var token in activeTokens)
            token.RevokedAt = now;

        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<UserProfileResponse> GetMeAsync(CancellationToken ct = default)
    {
        var user = await LoadCurrentUserAsync(ct);
        return ToProfile(user);
    }

    public async Task<UserProfileResponse> UpdateMeAsync(UpdateProfileRequest request, CancellationToken ct = default)
    {
        await _updateProfileValidator.EnsureValidAsync(request, ct);

        var user = await LoadCurrentUserAsync(ct);
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Phone = request.Phone;
        await _unitOfWork.SaveChangesAsync(ct);

        return ToProfile(user);
    }

    // ---- helpers ----

    private async Task<AuthResponse> IssueTokensAsync(User user, string ipAddress, Guid? familyId, CancellationToken ct)
    {
        var roles = user.UserRoles.Select(ur => ur.Role.Name).Distinct().ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Name)
            .Distinct()
            .ToList();

        var access = _tokenService.GenerateAccessToken(user.Id, user.Email, roles, permissions);

        var rawRefresh = _tokenService.GenerateRefreshToken();
        await _refreshTokens.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _tokenService.HashToken(rawRefresh),
            FamilyId = familyId ?? Guid.NewGuid(),
            ExpiresAt = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays),
            CreatedByIp = ipAddress
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return new AuthResponse(access.Token, rawRefresh,
            new AuthUserDto(user.Id, user.Email, user.FirstName, user.LastName, roles));
    }

    private async Task ThrowIfLockedAsync(string email, CancellationToken ct)
    {
        // Look back far enough that the lock survives the failure-window sliding away:
        // the lock is based on the newest failure + 15 min, so we must see failures up to
        // (window + duration) old — otherwise a lock evaporates after 5 minutes (audit fix).
        var since = DateTime.UtcNow - LockoutLookback;
        var attempts = await _loginAttempts.GetByEmailSinceAsync(email, since, ct);
        if (attempts.Count == 0) return;

        var ordered = attempts.OrderByDescending(a => a.CreatedAt).ToList();

        var consecutiveFailures = 0;
        foreach (var attempt in ordered)
        {
            if (attempt.IsSuccess) break;
            consecutiveFailures++;
        }

        if (consecutiveFailures < LockoutFailureThreshold) return;

        var lockedUntil = ordered.First(a => !a.IsSuccess).CreatedAt.AddMinutes(LockoutDurationMinutes);
        if (DateTime.UtcNow < lockedUntil)
        {
            var minutes = Math.Ceiling((lockedUntil - DateTime.UtcNow).TotalMinutes);
            throw new ForbiddenException(ErrorCodes.UserLocked,
                $"Account locked due to too many failed attempts. Try again in {minutes} minute(s).");
        }
    }

    private async Task RecordAttemptAsync(string email, string ipAddress, bool success, CancellationToken ct)
    {
        await _loginAttempts.AddAsync(new LoginAttempt
        {
            Email = email,
            IpAddress = ipAddress,
            IsSuccess = success
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<User> LoadCurrentUserAsync(CancellationToken ct)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException(ErrorCodes.InvalidTokenState, "Not authenticated.");

        return await _users.GetByIdWithRolesAsync(userId, ct)
            ?? throw new NotFoundException(ErrorCodes.UserNotFound, "The requested user was not found.");
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static UserProfileResponse ToProfile(User user)
    {
        var roles = user.UserRoles.Select(ur => ur.Role.Name).Distinct().ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Name)
            .Distinct()
            .ToList();

        return new UserProfileResponse(user.Id, user.Email, user.FirstName, user.LastName,
            user.Phone, user.IsActive, roles, permissions, user.CreatedAt);
    }
}
