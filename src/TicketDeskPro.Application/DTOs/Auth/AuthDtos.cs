namespace TicketDeskPro.Application.DTOs.Auth;

public record RegisterRequest(string Email, string Password, string FirstName, string LastName, string? Phone);

public record LoginRequest(string Email, string Password);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Token, string NewPassword);

public record UpdateProfileRequest(string FirstName, string LastName, string? Phone);

public record AuthUserDto(Guid Id, string Email, string FirstName, string LastName, IReadOnlyList<string> Roles);

public record AuthResponse(
    string AccessToken,
    // Cookie-only transport: XSS can never read the refresh token from JSON.
    // Stays a property for the service layer/unit tests; the controller ships
    // it in an HttpOnly Set-Cookie header instead.
    [property: System.Text.Json.Serialization.JsonIgnore] string RefreshToken,
    AuthUserDto User);

public record RegisterResponse(Guid UserId, string Message);

public record UserProfileResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? Phone,
    bool IsActive,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    DateTime CreatedAt);
