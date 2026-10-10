using Microsoft.EntityFrameworkCore;
using TicketDeskPro.Infrastructure;
using TicketDeskPro.Infrastructure.Data;
using TicketDeskPro.Infrastructure.Data.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Apply migrations + seed baseline data on startup (dev convenience; replaced by
// explicit migration step in the Render deploy pipeline in Phase 8).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy", time = DateTime.UtcNow }));

app.Run();
