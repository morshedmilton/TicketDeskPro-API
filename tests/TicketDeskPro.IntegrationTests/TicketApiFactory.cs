using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Domain.Entities;
using TicketDeskPro.Domain.Interfaces;
using TicketDeskPro.Infrastructure.Data;
using TicketDeskPro.Infrastructure.Data.Seed;
using TicketDeskPro.Infrastructure.Interceptors;
using TicketDeskPro.Infrastructure.Services;

namespace TicketDeskPro.IntegrationTests;

/// <summary>
/// Boots the real API pipeline (controllers, middleware, auth) against an InMemory database
/// and a temp file-storage folder. Redis is replaced by a no-op limiter.
/// </summary>
public class TicketApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"it-{Guid.NewGuid()}";
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"tdp-it-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // dev-only behaviors (demo-user seed gate)

        // Hermetic: appsettings.Development.json is gitignored, so CI runners have no JWT
        // secret and the factory must not depend on ambient machine state.
        builder.UseSetting("JwtSettings:Secret",
            "integration-test-signing-key-0123456789abcdef0123456789abcdef0123456789abcdef");
        // CorsTests asserts against this exact origin; pin it so ambient FRONTEND_URL
        // (or a future Cloudinary__* env var leaking into DI) cannot flip the policy.
        builder.UseSetting("FRONTEND_URL", "http://localhost:3000");

        builder.ConfigureServices(services =>
        {
            // AddDbContext APPENDS its options actions — removing only DbContextOptions isn't
            // enough or both providers stay registered. Clear the config objects entirely.
            services.RemoveAll(typeof(Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<AppDbContext>));

            services.AddDbContext<AppDbContext>((sp, options) =>
                options.UseInMemoryDatabase(_dbName)
                       .AddInterceptors(sp.GetRequiredService<SoftDeleteInterceptor>(),
                                        sp.GetRequiredService<AuditInterceptor>()));

            services.AddLogging(b => b.AddProvider(new CapturingLoggerProvider()));

            // No Redis in tests.
            services.RemoveAll(typeof(ILoginRateLimiter));
            services.AddSingleton<ILoginRateLimiter, NoOpRateLimiter>();
            services.RemoveAll(typeof(ICacheService));
            services.AddSingleton<ICacheService, NoOpCacheService>();

            // Always local disk in tests, even if the machine has Cloudinary__* env vars.
            services.RemoveAll(typeof(IFileStorageService));
            services.AddScoped<IFileStorageService, LocalFileStorageService>();
        });

        builder.UseSetting("Storage:RootPath", StorageRoot);
    }

    public async Task<string> TokenForAsync(string email)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var user = await db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .ThenInclude(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstAsync(u => u.Email == email);

        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
        var perms = user.UserRoles.SelectMany(ur => ur.Role.RolePermissions).Select(rp => rp.Permission.Name).Distinct().ToList();
        return tokens.GenerateAccessToken(user.Id, user.Email, roles, perms).Token;
    }

    public AppDbContext CreateScopeDb() => throw new NotSupportedException("Resolve AppDbContext from a scoped provider instead.");
}

public class NoOpCacheService : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult<T?>(default);
    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) => Task.CompletedTask;
    public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
    public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default) => Task.CompletedTask;
}

public class NoOpRateLimiter : ILoginRateLimiter
{
    public Task CheckAndRecordAsync(string ipAddress, string email, CancellationToken ct = default) => Task.CompletedTask;
    public Task CheckAsync(string bucket, string email, CancellationToken ct = default) => Task.CompletedTask;
    public Task ResetAsync(string ipAddress, string email, CancellationToken ct = default) => Task.CompletedTask;
}
