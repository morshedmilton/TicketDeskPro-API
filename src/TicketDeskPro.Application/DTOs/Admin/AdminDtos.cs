using FluentValidation;

namespace TicketDeskPro.Application.DTOs.Admin;

// ---------- users (US-1.08) ----------

public record UserAdminDto(
    Guid Id, string Email, string FirstName, string LastName, string? Phone,
    bool IsActive, IReadOnlyList<string> Roles, DateTime CreatedAt, DateTime? DeletedAt);

public record UserPageDto(IReadOnlyList<UserAdminDto> Items, string? NextCursor, bool HasMore);

public record UserCreateRequest(string Email, string Password, string FirstName, string LastName, string? Phone, IReadOnlyList<string> Roles);

public record UserUpdateRequest(string FirstName, string LastName, string? Phone, bool IsActive, IReadOnlyList<string>? Roles);

public class UserCreateAdminValidator : AbstractValidator<UserCreateRequest>
{
    public UserCreateAdminValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty()
            .Must(Validators.Auth.PasswordRule.IsValid).WithMessage(Validators.Auth.PasswordRule.Message);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.Roles).NotEmpty();
    }
}

public class UserUpdateAdminValidator : AbstractValidator<UserUpdateRequest>
{
    public UserUpdateAdminValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Phone).MaximumLength(30);
    }
}

// ---------- roles (US-1.09) ----------

public record RoleDto(Guid Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions);

public record RoleCreateRequest(string Name, string? Description, IReadOnlyList<Guid> PermissionIds);

public record RoleUpdateRequest(string Name, string? Description, IReadOnlyList<Guid> PermissionIds);

public record PermissionDto(Guid Id, string Name, string Module);

public class RoleCreateAdminValidator : AbstractValidator<RoleCreateRequest>
{
    public RoleCreateAdminValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Description).MaximumLength(200);
        RuleFor(x => x.PermissionIds).NotEmpty();
    }
}

public class RoleUpdateAdminValidator : AbstractValidator<RoleUpdateRequest>
{
    public RoleUpdateAdminValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Description).MaximumLength(200);
        RuleFor(x => x.PermissionIds).NotEmpty();
    }
}

// ---------- categories & tags (US-10.01) ----------

public record CategoryDto(Guid Id, string Name, string? Description, bool IsActive);
public record CategoryCreateRequest(string Name, string? Description);
public record CategoryUpdateRequest(string Name, string? Description, bool IsActive);

public record TagDto(Guid Id, string Name);
public record TagCreateRequest(string Name);

public class CategoryCreateValidator : AbstractValidator<CategoryCreateRequest>
{
    public CategoryCreateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public class CategoryUpdateValidator : AbstractValidator<CategoryUpdateRequest>
{
    public CategoryUpdateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public class TagCreateValidator : AbstractValidator<TagCreateRequest>
{
    public TagCreateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
    }
}

// ---------- audit viewer (US-9.02) ----------

public record AuditLogDto(
    Guid Id, Guid? UserId, string? UserName, string Action,
    string EntityType, string EntityId, string? BeforeJson, string? AfterJson, DateTime Timestamp);

public record AuditLogPageDto(IReadOnlyList<AuditLogDto> Items, string? NextCursor, bool HasMore);

// ---------- notification preferences (US-7.03) ----------

public record NotificationPreferenceDto(
    [property: System.Text.Json.Serialization.JsonRequired] bool EmailOnTicketCreated,
    [property: System.Text.Json.Serialization.JsonRequired] bool EmailOnTicketAssigned,
    [property: System.Text.Json.Serialization.JsonRequired] bool EmailOnStatusChanged,
    [property: System.Text.Json.Serialization.JsonRequired] bool EmailOnSlaWarning,
    [property: System.Text.Json.Serialization.JsonRequired] bool InAppEnabled);

public class NotificationPreferenceValidator : AbstractValidator<NotificationPreferenceDto>
{
    // All toggles are booleans — presence is enforced by binding; validator exists
    // so the endpoint shares the standard validation pipeline.
}
