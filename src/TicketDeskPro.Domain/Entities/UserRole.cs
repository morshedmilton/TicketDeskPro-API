namespace TicketDeskPro.Domain.Entities;

/// <summary>
/// Join table: a user can hold multiple roles; permissions are additive (BR-039).
/// Deliberately not a BaseEntity — soft delete makes no sense on a link row.
/// </summary>
public class UserRole
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
}
