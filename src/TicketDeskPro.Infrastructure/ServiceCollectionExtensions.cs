using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketDeskPro.Infrastructure.Data;
using TicketDeskPro.Infrastructure.Interceptors;

namespace TicketDeskPro.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Single entry point so Program.cs stays thin (FRD Clean Architecture wiring).</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing connection string 'DefaultConnection'.");

        services.AddScoped<SoftDeleteInterceptor>();

        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
            options.UseNpgsql(connectionString)
                   .AddInterceptors(serviceProvider.GetRequiredService<SoftDeleteInterceptor>()));

        return services;
    }
}
