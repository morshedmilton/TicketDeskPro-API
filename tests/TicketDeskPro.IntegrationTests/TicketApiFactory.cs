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
        builder.UseEnvironment("Development"); // loads appsettings.Development.json (JWT secret)

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

public class NoOpRateLimiter : ILoginRateLimiter
{
    public Task CheckAndRecordAsync(string ipAddress, string email, CancellationToken ct = default) => Task.CompletedTask;
    public Task ResetAsync(string ipAddress, string email, CancellationToken ct = default) => Task.CompletedTask;
}
