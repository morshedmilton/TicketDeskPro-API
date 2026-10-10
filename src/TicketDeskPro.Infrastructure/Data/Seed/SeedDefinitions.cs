using TicketDeskPro.Domain.Enums;
using TicketDeskPro.Shared.Constants;

namespace TicketDeskPro.Infrastructure.Data.Seed;

/// <summary>
/// Baseline data required by the app to function (DB Design §5).
/// Deterministic Guids so re-seeding / environments never drift.
/// </summary>
public static class SeedDefinitions
{
    public static readonly (Guid Id, string Name)[] Roles =
    [
        (Guid.Parse("00000000-0000-0000-0000-000000000001"), AppRoles.Customer),
        (Guid.Parse("00000000-0000-0000-0000-000000000002"), AppRoles.Agent),
        (Guid.Parse("00000000-0000-0000-0000-000000000003"), AppRoles.Admin),
        (Guid.Parse("00000000-0000-0000-0000-000000000004"), AppRoles.SuperAdmin)
    ];

    public static readonly (Guid Id, string Name, string Module)[] Permissions =
    [
        (Guid.Parse("00000000-0000-0000-0000-000000000010"), AppPermissions.TicketsRead, "tickets"),
        (Guid.Parse("00000000-0000-0000-0000-000000000011"), AppPermissions.TicketsCreate, "tickets"),
        (Guid.Parse("00000000-0000-0000-0000-000000000012"), AppPermissions.TicketsUpdate, "tickets"),
        (Guid.Parse("00000000-0000-0000-0000-000000000013"), AppPermissions.TicketsDelete, "tickets"),
        (Guid.Parse("00000000-0000-0000-0000-000000000014"), AppPermissions.TicketsAssign, "tickets"),
        (Guid.Parse("00000000-0000-0000-0000-000000000015"), AppPermissions.TicketsRestore, "tickets"),
        (Guid.Parse("00000000-0000-0000-0000-000000000020"), AppPermissions.MessagesRead, "messages"),
        (Guid.Parse("00000000-0000-0000-0000-000000000021"), AppPermissions.MessagesCreate, "messages"),
        (Guid.Parse("00000000-0000-0000-0000-000000000022"), AppPermissions.MessagesInternalNote, "messages"),
        (Guid.Parse("00000000-0000-0000-0000-000000000030"), AppPermissions.CategoriesRead, "categories"),
        (Guid.Parse("00000000-0000-0000-0000-000000000031"), AppPermissions.CategoriesManage, "categories"),
        (Guid.Parse("00000000-0000-0000-0000-000000000032"), AppPermissions.TagsRead, "tags"),
        (Guid.Parse("00000000-0000-0000-0000-000000000033"), AppPermissions.TagsManage, "tags"),
        (Guid.Parse("00000000-0000-0000-0000-000000000040"), AppPermissions.SlaRead, "sla"),
        (Guid.Parse("00000000-0000-0000-0000-000000000041"), AppPermissions.SlaManage, "sla"),
        (Guid.Parse("00000000-0000-0000-0000-000000000050"), AppPermissions.UsersRead, "users"),
        (Guid.Parse("00000000-0000-0000-0000-000000000051"), AppPermissions.UsersCreate, "users"),
        (Guid.Parse("00000000-0000-0000-0000-000000000052"), AppPermissions.UsersUpdate, "users"),
        (Guid.Parse("00000000-0000-0000-0000-000000000053"), AppPermissions.UsersDelete, "users"),
        (Guid.Parse("00000000-0000-0000-0000-000000000054"), AppPermissions.RolesRead, "roles"),
        (Guid.Parse("00000000-0000-0000-0000-000000000055"), AppPermissions.RolesManage, "roles"),
        (Guid.Parse("00000000-0000-0000-0000-000000000060"), AppPermissions.AuditRead, "audit"),
        (Guid.Parse("00000000-0000-0000-0000-000000000061"), AppPermissions.DashboardView, "dashboard"),
        (Guid.Parse("00000000-0000-0000-0000-000000000062"), AppPermissions.NotificationsRead, "notifications")
    ];

    /// <summary>Role name -> permission names it receives (additive across roles, BR-039).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> RolePermissions =
        new Dictionary<string, string[]>
        {
            [AppRoles.Customer] =
            [
                AppPermissions.TicketsRead, AppPermissions.TicketsCreate,
                AppPermissions.MessagesRead, AppPermissions.MessagesCreate,
                AppPermissions.CategoriesRead, AppPermissions.TagsRead,
                AppPermissions.NotificationsRead
            ],
            [AppRoles.Agent] =
            [
                AppPermissions.TicketsRead, AppPermissions.TicketsCreate, AppPermissions.TicketsUpdate,
                AppPermissions.TicketsAssign,
                AppPermissions.MessagesRead, AppPermissions.MessagesCreate, AppPermissions.MessagesInternalNote,
                AppPermissions.CategoriesRead, AppPermissions.TagsRead,
                AppPermissions.SlaRead, AppPermissions.DashboardView,
                AppPermissions.NotificationsRead
            ],
            [AppRoles.Admin] =
            [
                AppPermissions.TicketsRead, AppPermissions.TicketsCreate, AppPermissions.TicketsUpdate,
                AppPermissions.TicketsDelete, AppPermissions.TicketsAssign, AppPermissions.TicketsRestore,
                AppPermissions.MessagesRead, AppPermissions.MessagesCreate, AppPermissions.MessagesInternalNote,
                AppPermissions.CategoriesRead, AppPermissions.CategoriesManage,
                AppPermissions.TagsRead, AppPermissions.TagsManage,
                AppPermissions.SlaRead, AppPermissions.SlaManage,
                AppPermissions.UsersRead, AppPermissions.UsersCreate, AppPermissions.UsersUpdate, AppPermissions.UsersDelete,
                AppPermissions.RolesRead,
                AppPermissions.AuditRead, AppPermissions.DashboardView,
                AppPermissions.NotificationsRead
            ],
            // SuperAdmin receives every permission (see seeder: grants AppPermissions.All).
            [AppRoles.SuperAdmin] = AppPermissions.All.ToArray()
        };

    /// <summary>Default SLA targets from PRD BR-027..BR-030 (minutes).</summary>
    public static readonly (Guid Id, string Name, TicketPriority Priority, int Response, int Resolution)[] SlaPolicies =
    [
        (Guid.Parse("00000000-0000-0000-0000-000000000101"), "Low Priority SLA", TicketPriority.Low, 480, 4320),
        (Guid.Parse("00000000-0000-0000-0000-000000000102"), "Medium Priority SLA", TicketPriority.Medium, 240, 1440),
        (Guid.Parse("00000000-0000-0000-0000-000000000103"), "High Priority SLA", TicketPriority.High, 120, 480),
        (Guid.Parse("00000000-0000-0000-0000-000000000104"), "Urgent Priority SLA", TicketPriority.Urgent, 30, 240)
    ];

    public static readonly (Guid Id, string Name, string Description)[] Categories =
    [
        (Guid.Parse("00000000-0000-0000-0000-000000000111"), "General Inquiry", "Questions that do not fit elsewhere"),
        (Guid.Parse("00000000-0000-0000-0000-000000000112"), "Technical Support", "Bugs, errors and outages"),
        (Guid.Parse("00000000-0000-0000-0000-000000000113"), "Billing", "Invoices, payments and refunds"),
        (Guid.Parse("00000000-0000-0000-0000-000000000114"), "Account Issue", "Login, password and profile problems")
    ];

    /// <summary>Demo users for local development. Password for all: Admin123!</summary>
    public static readonly (Guid Id, string Email, string FirstName, string LastName, string Role)[] Users =
    [
        (Guid.Parse("00000000-0000-0000-0000-000000000150"), "superadmin@ticketdesk.dev", "Super", "Admin", AppRoles.SuperAdmin),
        (Guid.Parse("00000000-0000-0000-0000-000000000151"), "admin@ticketdesk.dev", "Marcus", "Johnson", AppRoles.Admin),
        (Guid.Parse("00000000-0000-0000-0000-000000000152"), "agent@ticketdesk.dev", "David", "Chen", AppRoles.Agent),
        (Guid.Parse("00000000-0000-0000-0000-000000000153"), "customer@ticketdesk.dev", "Sarah", "Jenkins", AppRoles.Customer)
    ];

    public const string DemoPassword = "Admin123!";
}
