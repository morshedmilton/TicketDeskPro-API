using Microsoft.EntityFrameworkCore;
using TicketDeskPro.Domain.Entities;
using TicketDeskPro.Shared.Constants;

namespace TicketDeskPro.Infrastructure.Data.Seed;

/// <summary>
/// Runtime seeding: runs once on API startup, no-ops if users already exist.
/// Runtime (not EF HasData) keeps passwords hashed by BCrypt at run time and migrations clean.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, bool includeDemoUsers, CancellationToken ct = default)
    {
        // Baseline (roles, permissions, SLA policies, categories) is seeded everywhere.
        // Demo users with a known password are DEVELOPMENT ONLY — never in production (audit fix).
        if (await db.Users.AnyAsync(ct))
            return;

        var roleIdByName = SeedDefinitions.Roles.ToDictionary(r => r.Name, r => r.Id);
        var permissionIdByName = SeedDefinitions.Permissions.ToDictionary(p => p.Name, p => p.Id);

        db.AddRange(SeedDefinitions.Roles.Select(r => new Role
        {
            Id = r.Id,
            Name = r.Name,
            Description = $"{r.Name} system role",
            IsSystem = true
        }));

        db.AddRange(SeedDefinitions.Permissions.Select(p => new Permission
        {
            Id = p.Id,
            Name = p.Name,
            Module = p.Module
        }));

        await db.SaveChangesAsync(ct);

        var rolePermissions = SeedDefinitions.RolePermissions
            .SelectMany(pair => pair.Value.Select(permissionName => new RolePermission
            {
                RoleId = roleIdByName[pair.Key],
                PermissionId = permissionIdByName[permissionName]
            }));
        db.AddRange(rolePermissions);

        db.AddRange(SeedDefinitions.SlaPolicies.Select(p => new SlaPolicy
        {
            Id = p.Id,
            Name = p.Name,
            Priority = p.Priority,
            FirstResponseMinutes = p.Response,
            ResolutionMinutes = p.Resolution,
            IsActive = true
        }));

        db.AddRange(SeedDefinitions.Categories.Select(c => new Category
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description,
            IsActive = true
        }));

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(SeedDefinitions.DemoPassword);
        foreach (var (id, email, firstName, lastName, roleName) in SeedDefinitions.Users)
        {
            if (!includeDemoUsers) break;

            var user = new User
            {
                Id = id,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                PasswordHash = passwordHash,
                IsActive = true
            };
            user.UserRoles.Add(new UserRole { UserId = id, RoleId = roleIdByName[roleName] });
            db.Add(user);
        }

        await db.SaveChangesAsync(ct);
    }
}
