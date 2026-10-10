using TicketDeskPro.Application.Interfaces;

namespace TicketDeskPro.Infrastructure.Services;

/// <summary>BCrypt with a per-hash salt — the industry default for password storage.</summary>
public class BcryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    public bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);
}
