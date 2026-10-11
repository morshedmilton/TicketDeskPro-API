using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Application.Options;
using TicketDeskPro.Domain.Interfaces;
using TicketDeskPro.Infrastructure.Data;
using TicketDeskPro.Infrastructure.Interceptors;
using TicketDeskPro.Infrastructure.Repositories;
using TicketDeskPro.Infrastructure.Services;

namespace TicketDeskPro.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Single entry point so Program.cs stays thin (FRD Clean Architecture wiring).</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // --- JWT settings (validated up front: a short secret is a security hole, not a warning) ---
        var jwtSection = configuration.GetSection(JwtSettings.SectionName);
        var secret = jwtSection["Secret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            throw new InvalidOperationException(
                "JwtSettings:Secret must be configured with at least 32 characters. " +
                "Copy .env.example / see README for local setup.");

        services.Configure<JwtSettings>(jwtSection);

        // --- PostgreSQL ---
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing connection string 'DefaultConnection'.");

        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<AuditInterceptor>();
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
            options.UseNpgsql(connectionString, npgsql =>
                       npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                   // Order matters: soft-delete converts DELETE→UPDATE first, so the audit
                   // interceptor records it as the "Delete" it really was.
                   .AddInterceptors(serviceProvider.GetRequiredService<SoftDeleteInterceptor>(),
                                    serviceProvider.GetRequiredService<AuditInterceptor>()));

        // --- Redis (abortConnect=false: start even if Redis is down; per-request
        //     failures surface as SYS_002 from the rate limiter instead of crashing startup) ---
        var redisConnectionString = configuration.GetSection("RedisSettings")["ConnectionString"] ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(new ConfigurationOptions
            {
                EndPoints = { redisConnectionString },
                AbortOnConnectFail = false
            }));

        // --- Repositories & unit of work ---
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IRoleRepository, EfRoleRepository>();
        services.AddScoped<IRefreshTokenRepository, EfRefreshTokenRepository>();
        services.AddScoped<ILoginAttemptRepository, EfLoginAttemptRepository>();
        services.AddScoped<IEmailOutboxRepository, EfEmailOutboxRepository>();
        services.AddScoped<ITicketRepository, EfTicketRepository>();
        services.AddScoped<ITicketMessageRepository, EfTicketMessageRepository>();
        services.AddScoped<ITicketAttachmentRepository, EfTicketAttachmentRepository>();
        services.AddScoped<ICategoryRepository, EfCategoryRepository>();
        services.AddScoped<ITagRepository, EfTagRepository>();
        services.AddScoped<ISlaPolicyRepository, EfSlaPolicyRepository>();

        // --- Security services ---
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<ILoginRateLimiter, RedisLoginRateLimiter>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();

        return services;
    }
}
