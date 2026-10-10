namespace TicketDeskPro.Domain.Entities;

public class Permission : BaseEntity
{
    /// <summary>Unique permission name, e.g. "tickets:delete".</summary>
    public string Name { get; set; } = null!;

    /// <summary>Grouping module, e.g. "tickets", "users", "sla".</summary>
    public string Module { get; set; } = null!;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
