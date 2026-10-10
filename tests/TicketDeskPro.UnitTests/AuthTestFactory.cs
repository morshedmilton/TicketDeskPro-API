using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketDeskPro.Application.DTOs.Auth;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Application.Options;
using TicketDeskPro.Application.Services;
using TicketDeskPro.Application.Validators.Auth;
using TicketDeskPro.Domain.Entities;
using TicketDeskPro.Domain.Interfaces;
using TicketDeskPro.Infrastructure.Data;
using TicketDeskPro.Infrastructure.Repositories;
using TicketDeskPro.Infrastructure.Services;
using TicketDeskPro.Shared.Constants;

namespace TicketDeskPro.UnitTests;

/// <summary>Builds a real AuthService over an EF InMemory database — no mocks of DbContext.</summary>
public sealed class AuthTestFactory : IDisposable
{
    public AppDbContext Db { get; }
    public AuthService Auth { get; }
    public TokenService Tokens { get; }
    public BcryptPasswordHasher PasswordHasher { get; }
    public FakeRateLimiter RateLimiter { get; } = new();
    public FakeCurrentUser CurrentUser { get; } = new();

    private readonly string _dbName = $"tests-{Guid.NewGuid()}";

    public AuthTestFactory()
    {
        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options);

        var jwt = Options.Create(new JwtSettings
        {
            Secret = new string('s', 64),
            Issuer = "TicketDeskPro",
            Audience = "TicketDeskProClient",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7
        });

        Tokens = new TokenService(jwt);
        PasswordHasher = new BcryptPasswordHasher();

        Auth = new AuthService(
            new EfUserRepository(Db),
            new EfRoleRepository(Db),
            new EfRefreshTokenRepository(Db),
            new EfLoginAttemptRepository(Db),
            new EfEmailOutboxRepository(Db),
            new UnitOfWork(Db),
            PasswordHasher,
            Tokens,
            RateLimiter,
            CurrentUser,
            new RegisterRequestValidator(),
            new LoginRequestValidator(),
            new UpdateProfileRequestValidator(),
            new ResetPasswordRequestValidator(),
            new ForgotPasswordRequestValidator(),
            jwt,
            "http://localhost:3000");
    }

    /// <summary>Seeds the Customer role (as the real seeder does) and returns it.</summary>
    public async Task<Role> SeedCustomerRoleAsync()
    {
        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = AppRoles.Customer,
            IsSystem = true
        };
        Db.Roles.Add(role);
        await Db.SaveChangesAsync();
        return role;
    }

    public async Task<User> SeedUserAsync(string email = "user@test.dev", string password = "Password1!")
    {
        var role = await Db.Roles.FirstOrDefaultAsync(r => r.Name == AppRoles.Customer)
                   ?? await SeedCustomerRoleAsync();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = PasswordHasher.Hash(password),
            FirstName = "Test",
            LastName = "User",
            IsActive = true
        };
        user.UserRoles.Add(new UserRole { User = user, Role = role });
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        return user;
    }

    public void Dispose() => Db.Dispose();
}

public class FakeRateLimiter : ILoginRateLimiter
{
    public bool ShouldLimit { get; set; }
    public int ResetCallCount { get; private set; }

    public Task CheckAndRecordAsync(string ipAddress, string email, CancellationToken ct = default) =>
        ShouldLimit
            ? throw new TicketDeskPro.Shared.Exceptions.RateLimitedException(ErrorCodes.AuthRateLimited, "Too many login attempts. Try again later.")
            : Task.CompletedTask;

    public Task ResetAsync(string ipAddress, string email, CancellationToken ct = default)
    {
        ResetCallCount++;
        return Task.CompletedTask;
    }
}

public class FakeCurrentUser : ICurrentUserService
{
    public Guid? UserId { get; set; }
    public string? Email { get; set; }
    public bool IsAuthenticated => UserId is not null;
}
