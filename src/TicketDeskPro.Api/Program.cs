using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Microsoft.OpenApi;
using TicketDeskPro.Api.Middleware;
using TicketDeskPro.Api.Services;
using TicketDeskPro.Application;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Application.Options;
using TicketDeskPro.Infrastructure;
using TicketDeskPro.Infrastructure.Data;
using TicketDeskPro.Infrastructure.Data.Seed;

var builder = WebApplication.CreateBuilder(args);

// Structured logging (FRD §10): Serilog with JSON-ish file sink for prod,
// console for dev; CorrelationId enriched per request by the middleware.
builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "TicketDeskPro-API"));

// --- Configuration (fail fast on bad security config) ---
var jwtSection = builder.Configuration.GetSection(JwtSettings.SectionName);
var jwtSecret = jwtSection["Secret"]
    ?? throw new InvalidOperationException("JwtSettings:Secret is missing. See .env.example / README.");

// --- Services ---
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Real-time: SignalR hub + the Application-facing notifier (Phase 4).
builder.Services.AddSignalR();
builder.Services.AddSingleton<IRealtimeNotifier, TicketDeskPro.Api.Services.SignalRRealtimeNotifier>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // we emit/consume literal claim names: sub, email, role, permissions

        // Browsers cannot set an Authorization header on a WebSocket handshake —
        // the SignalR JS client sends the token as ?access_token= on /hubs/*.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = "role",
            NameClaimType = "sub"
        };
    });
builder.Services.AddAuthorization();

// CORS: the browser client (Vercel/localhost:3000) is cross-origin — without this
// every axios call and the SignalR negotiate POST dies in preflight.
var frontendUrl = builder.Configuration["FRONTEND_URL"] ?? "http://localhost:3000";
builder.Services.AddCors(options => options.AddPolicy("frontend", policy => policy
    .WithOrigins(frontendUrl)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "TicketDesk Pro API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the accessToken from POST /api/v1/auth/login (no 'Bearer ' prefix needed)."
    });
    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", null), new List<string>() }
    });
});

var app = builder.Build();

// --- Database: migrate + seed on startup (works on Render too — no manual step) ---
// Demo users are still opt-in: Development by default, or SeedDemoUsers=true in the
// environment (the live portfolio demo needs known credentials; audit rule stays
// "never implicitly in production").
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational()) // integration tests swap in InMemory, which has no migrations
        await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(
        db,
        includeDemoUsers: app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("SeedDemoUsers"));
}

// --- Pipeline (order matters: correlation first so even errors carry the id) ---
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Swagger is documentation only (every data endpoint still requires JWT), so it stays
// up in production — the live demo's "check the API" story needs it (Phase 8).
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<TicketDeskPro.Api.Hubs.NotificationHub>("/hubs/notifications");
app.MapGet("/health", () => Results.Ok(new { status = "healthy", time = DateTime.UtcNow }));

app.Run();

/// <summary>Exposes the implicit Program class to WebApplicationFactory in integration tests.</summary>
public partial class Program { }
