namespace TicketDeskPro.Shared.Constants;

/// <summary>
/// Fine-grained permission names (PRD US-1.09, BR-039).
/// Format: module:action — stored in Permissions.Name, checked by RequirePermission filter (Phase 2).
/// </summary>
public static class AppPermissions
{
    // Tickets
    public const string TicketsRead = "tickets.read";
    public const string TicketsCreate = "tickets.create";
    public const string TicketsUpdate = "tickets.update";
    public const string TicketsDelete = "tickets.delete";
    public const string TicketsAssign = "tickets.assign";
    public const string TicketsRestore = "tickets.restore";

    // Messages
    public const string MessagesRead = "messages.read";
    public const string MessagesCreate = "messages.create";
    public const string MessagesInternalNote = "messages.internal_note";

    // Categories & Tags
    public const string CategoriesRead = "categories.read";
    public const string CategoriesManage = "categories.manage";
    public const string TagsRead = "tags.read";
    public const string TagsManage = "tags.manage";

    // SLA
    public const string SlaRead = "sla.read";
    public const string SlaManage = "sla.manage";

    // Users & Roles
    public const string UsersRead = "users.read";
    public const string UsersCreate = "users.create";
    public const string UsersUpdate = "users.update";
    public const string UsersDelete = "users.delete";
    public const string RolesRead = "roles.read";
    public const string RolesManage = "roles.manage";

    // System
    public const string AuditRead = "audit.read";
    public const string DashboardView = "dashboard.view";
    public const string NotificationsRead = "notifications.read";

    public static readonly IReadOnlyList<string> All =
    [
        TicketsRead, TicketsCreate, TicketsUpdate, TicketsDelete, TicketsAssign, TicketsRestore,
        MessagesRead, MessagesCreate, MessagesInternalNote,
        CategoriesRead, CategoriesManage, TagsRead, TagsManage,
        SlaRead, SlaManage,
        UsersRead, UsersCreate, UsersUpdate, UsersDelete, RolesRead, RolesManage,
        AuditRead, DashboardView, NotificationsRead
    ];
}
