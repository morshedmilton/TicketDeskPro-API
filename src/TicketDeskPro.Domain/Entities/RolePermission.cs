namespace TicketDeskPro.Domain.Entities;

/// <summary>Join table mapping roles to their granted permissions.</summary>
public class RolePermission
{
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
}
