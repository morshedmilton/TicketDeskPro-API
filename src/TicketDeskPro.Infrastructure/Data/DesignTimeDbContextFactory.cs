using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TicketDeskPro.Infrastructure.Data;

/// <summary>
/// Lets `dotnet ef migrations add` run without booting the whole API.
/// The connection string is never used at design time — it just needs to exist.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=TicketDeskDesignTime;Username=postgres;Password=postgres")
            .Options;

        return new AppDbContext(options);
    }
}
