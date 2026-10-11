using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TicketDeskPro.Application.DTOs.Auth;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Application.Options;
using TicketDeskPro.Application.Services;
using TicketDeskPro.Application.Validators.Auth;
using TicketDeskPro.Domain.Interfaces;

namespace TicketDeskPro.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

        var frontendUrl = configuration["FRONTEND_URL"] ?? "http://localhost:3000";

        services.AddScoped<IAuthService>(sp => new AuthService(
            sp.GetRequiredService<IUserRepository>(),
            sp.GetRequiredService<IRoleRepository>(),
            sp.GetRequiredService<IRefreshTokenRepository>(),
            sp.GetRequiredService<ILoginAttemptRepository>(),
            sp.GetRequiredService<IEmailOutboxRepository>(),
            sp.GetRequiredService<IUnitOfWork>(),
            sp.GetRequiredService<IPasswordHasher>(),
            sp.GetRequiredService<ITokenService>(),
            sp.GetRequiredService<ILoginRateLimiter>(),
            sp.GetRequiredService<ICurrentUserService>(),
            sp.GetRequiredService<IValidator<RegisterRequest>>(),
            sp.GetRequiredService<IValidator<LoginRequest>>(),
            sp.GetRequiredService<IValidator<UpdateProfileRequest>>(),
            sp.GetRequiredService<IValidator<ResetPasswordRequest>>(),
            sp.GetRequiredService<IValidator<ForgotPasswordRequest>>(),
            sp.GetRequiredService<IOptions<JwtSettings>>(),
            frontendUrl));

        services.AddScoped<ITicketService, TicketService>();
        services.AddScoped<ISlaPolicyService, SlaPolicyService>();

        // Domain event handlers (FRD §7.7) — one class handles the ticket lifecycle events.
        services.AddScoped<Services.Notifications.NotificationDispatcher>();
        services.AddScoped<IEventHandler<Domain.Events.TicketCreatedEvent>>(sp => sp.GetRequiredService<Services.Notifications.NotificationDispatcher>());
        services.AddScoped<IEventHandler<Domain.Events.TicketStatusChangedEvent>>(sp => sp.GetRequiredService<Services.Notifications.NotificationDispatcher>());
        services.AddScoped<IEventHandler<Domain.Events.TicketAssignedEvent>>(sp => sp.GetRequiredService<Services.Notifications.NotificationDispatcher>());
        services.AddScoped<IEventHandler<Domain.Events.TicketMessageAddedEvent>>(sp => sp.GetRequiredService<Services.Notifications.NotificationDispatcher>());

        return services;
    }
}
