using FluentValidation;
using TicketDeskPro.Application.DTOs.Admin;
using TicketDeskPro.Application.Extensions;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Domain.Entities;
using TicketDeskPro.Domain.Interfaces;
using TicketDeskPro.Shared.Constants;
using TicketDeskPro.Shared.Exceptions;
using ValidationException = TicketDeskPro.Shared.Exceptions.ValidationException;
using ValidationFailure = TicketDeskPro.Shared.Exceptions.ValidationFailure;

namespace TicketDeskPro.Application.Services.Admin;

/// <summary>Admin user management (US-1.08, BR-038: SuperAdmins cannot be deleted,
/// and only another SuperAdmin can deactivate one).</summary>
public interface IUserAdminService
{
    Task<UserPageDto> GetPageAsync(string? search, string? roleName, int limit, string? cursor, CancellationToken ct = default);
    Task<UserAdminDto> CreateAsync(UserCreateRequest request, CancellationToken ct = default);
    Task<UserAdminDto> UpdateAsync(Guid id, UserUpdateRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<UserAdminDto> RestoreAsync(Guid id, CancellationToken ct = default);
}

public class UserAdminService : IUserAdminService
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<UserCreateRequest> _createValidator;
    private readonly IValidator<UserUpdateRequest> _updateValidator;

    public UserAdminService(
        IUserRepository users, IRoleRepository roles, IPasswordHasher passwordHasher,
        ICurrentUserService currentUser, IUnitOfWork unitOfWork,
        IValidator<UserCreateRequest> createValidator, IValidator<UserUpdateRequest> updateValidator)
    {
        _users = users;
        _roles = roles;
        _passwordHasher = passwordHasher;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<UserPageDto> GetPageAsync(string? search, string? roleName, int limit, string? cursor, CancellationToken ct = default)
    {
        var page = await _users.GetPageAsync(search, roleName, Math.Clamp(limit, 1, 100), cursor, ct);
        return new UserPageDto(page.Items.Select(ToDto).ToList(), page.NextCursor, page.HasMore);
    }

    public async Task<UserAdminDto> CreateAsync(UserCreateRequest request, CancellationToken ct = default)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var email = request.Email.Trim().ToLowerInvariant();

        if (await _users.AnyByEmailAsync(email, ct))
            throw new ConflictException(ErrorCodes.EmailExists, "A user with this email already exists.");

        var user = new User
        {
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Phone = request.Phone,
            IsActive = true
        };

        var roles = new List<string>();
        foreach (var roleName in request.Roles.Distinct())
        {
            var role = await _roles.GetByNameAsync(roleName, ct)
                ?? throw new ValidationException("One or more validation errors occurred.",
                    [new ValidationFailure("roles", $"Role '{roleName}' does not exist.")]);
            roles.Add(roleName);
            user.UserRoles.Add(new UserRole { User = user, Role = role });
        }

        // Escalation guard: only a SuperAdmin may GRANT the SuperAdmin role.
        if (roles.Contains(AppRoles.SuperAdmin) && !await ActorHasRoleAsync(AppRoles.SuperAdmin, ct))
            throw new ForbiddenException(ErrorCodes.MissingPermission, "Only a SuperAdmin can grant the SuperAdmin role.");

        await _users.AddAsync(user, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return ToDto((await _users.GetByIdWithRolesAsync(user.Id, ct))!);
    }

    public async Task<UserAdminDto> UpdateAsync(Guid id, UserUpdateRequest request, CancellationToken ct = default)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);
        var user = await _users.GetByIdWithRolesTrackedAsync(id, includeDeleted: false, ct)
            ?? throw new NotFoundException(ErrorCodes.UserNotFound, "The requested user was not found.");

        var isSuperAdminTarget = user.UserRoles.Any(ur => ur.Role.Name == AppRoles.SuperAdmin);
        var actorIsSuperAdmin = await ActorHasRoleAsync(AppRoles.SuperAdmin, ct);

        // BR-038: deactivating a SuperAdmin requires SuperAdmin rights.
        if (isSuperAdminTarget && !request.IsActive && !actorIsSuperAdmin)
            throw new ForbiddenException(ErrorCodes.MissingPermission, "Only a SuperAdmin can deactivate a SuperAdmin.");

        if (id == _currentUser.UserId && !request.IsActive)
            throw new ConflictException(ErrorCodes.SelfActionForbidden, "You cannot deactivate your own account.");

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Phone = request.Phone;
        user.IsActive = request.IsActive;

        if (request.Roles is not null)
        {
            // BR-038: SuperAdmin status cannot be removed via user updates.
            var removingSuperAdmin = isSuperAdminTarget && !request.Roles.Contains(AppRoles.SuperAdmin);
            if (removingSuperAdmin && !actorIsSuperAdmin)
                throw new ForbiddenException(ErrorCodes.MissingPermission, "Only a SuperAdmin can remove the SuperAdmin role.");

            // Escalation guard: only a SuperAdmin may GRANT the SuperAdmin role.
            var addingSuperAdmin = !isSuperAdminTarget && request.Roles.Contains(AppRoles.SuperAdmin);
            if (addingSuperAdmin && !actorIsSuperAdmin)
                throw new ForbiddenException(ErrorCodes.MissingPermission, "Only a SuperAdmin can grant the SuperAdmin role.");

            user.UserRoles.Clear();
            foreach (var roleName in request.Roles.Distinct())
            {
                var role = await _roles.GetByNameAsync(roleName, ct)
                    ?? throw new ValidationException("One or more validation errors occurred.",
                        [new ValidationFailure("roles", $"Role '{roleName}' does not exist.")]);
                user.UserRoles.Add(new UserRole { User = user, Role = role });
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return ToDto((await _users.GetByIdWithRolesAsync(id, ct))!);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var user = await _users.GetByIdWithRolesTrackedAsync(id, includeDeleted: false, ct)
            ?? throw new NotFoundException(ErrorCodes.UserNotFound, "The requested user was not found.");

        if (id == _currentUser.UserId)
            throw new ConflictException(ErrorCodes.SelfActionForbidden, "You cannot delete your own account.");

        // BR-038: SuperAdmins cannot be deleted at all — only deactivated.
        if (user.UserRoles.Any(ur => ur.Role.Name == AppRoles.SuperAdmin))
            throw new ConflictException(ErrorCodes.SystemRoleLocked,
                "SuperAdmin accounts cannot be deleted — deactivate them instead.");

        _users.Remove(user); // soft delete via interceptor
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<UserAdminDto> RestoreAsync(Guid id, CancellationToken ct = default)
    {
        var user = await _users.GetByIdWithRolesTrackedAsync(id, includeDeleted: true, ct)
            ?? throw new NotFoundException(ErrorCodes.UserNotFound, "The requested user was not found.");

        // The filtered email index lets a new account take the soft-deleted email.
        if (await _users.AnyByEmailAsync(user.Email, ct))
            throw new ConflictException(ErrorCodes.EmailExists,
                "Another account already uses this email — the user cannot be restored.");

        user.IsDeleted = false;
        user.DeletedAt = null;
        user.DeletedById = null;
        await _unitOfWork.SaveChangesAsync(ct);
        return ToDto((await _users.GetByIdWithRolesAsync(id, ct))!);
    }

    private async Task<bool> ActorHasRoleAsync(string roleName, CancellationToken ct)
    {
        var actorId = _currentUser.UserId
            ?? throw new UnauthorizedException(ErrorCodes.InvalidTokenState, "Not authenticated.");
        var actor = await _users.GetByIdWithRolesAsync(actorId, ct);
        return actor?.UserRoles.Any(ur => ur.Role.Name == roleName) == true;
    }

    private static UserAdminDto ToDto(User u) => new(
        u.Id, u.Email, u.FirstName, u.LastName, u.Phone, u.IsActive,
        u.UserRoles.Select(ur => ur.Role.Name).ToList(), u.CreatedAt, u.DeletedAt);
}
