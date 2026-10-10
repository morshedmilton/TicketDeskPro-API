using TicketDeskPro.Application.DTOs.Auth;

namespace TicketDeskPro.Application.Interfaces;

public interface IAuthService
{
    Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, string ipAddress, CancellationToken ct = default);
    Task<AuthResponse> RefreshAsync(string refreshToken, string ipAddress, CancellationToken ct = default);
    Task RevokeAsync(string refreshToken, CancellationToken ct = default);
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
    Task<UserProfileResponse> GetMeAsync(CancellationToken ct = default);
    Task<UserProfileResponse> UpdateMeAsync(UpdateProfileRequest request, CancellationToken ct = default);
}
