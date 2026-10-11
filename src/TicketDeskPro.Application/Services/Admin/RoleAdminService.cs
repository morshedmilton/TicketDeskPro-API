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

/// <summary>SuperAdmin role/permission management (US-1.09). System roles are
/// locked: they cannot be renamed, edited or deleted.</summary>
public interface IRoleAdminService
{
    Task<IReadOnlyList<RoleDto>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PermissionDto>> GetPermissionsAsync(CancellationToken ct = default);
    Task<RoleDto> CreateAsync(RoleCreateRequest request, CancellationToken ct = default);
    Task<RoleDto> UpdateAsync(Guid id, RoleUpdateRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class RoleAdminService : IRoleAdminService
{
    private readonly IRoleRepository _roles;
    private readonly IPermissionLookup _permissions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<RoleCreateRequest> _createValidator;
    private readonly IValidator<RoleUpdateRequest> _updateValidator;

    public RoleAdminService(
        IRoleRepository roles, IPermissionLookup permissions, IUnitOfWork unitOfWork,
        IValidator<RoleCreateRequest> createValidator, IValidator<RoleUpdateRequest> updateValidator)
    {
        _roles = roles;
        _permissions = permissions;
        _unitOfWork = unitOfWork;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<IReadOnlyList<RoleDto>> GetAllAsync(CancellationToken ct = default) =>
        (await _roles.GetAllWithPermissionsAsync(ct)).Select(ToDto).ToList();

    public async Task<IReadOnlyList<PermissionDto>> GetPermissionsAsync(CancellationToken ct = default) =>
        (await _permissions.GetAllAsync(ct)).Select(p => new PermissionDto(p.Id, p.Name, p.Module)).ToList();

    public async Task<RoleDto> CreateAsync(RoleCreateRequest request, CancellationToken ct = default)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var name = request.Name.Trim();

        if (await _roles.NameExistsAsync(name, null, ct))
            throw new ConflictException(ErrorCodes.NameExists, $"A role named '{name}' already exists.");

        var role = new Role { Name = name, Description = request.Description?.Trim(), IsSystem = false };
        await AttachPermissionsAsync(role, request.PermissionIds, ct);

        await _roles.AddAsync(role, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return ToDto((await _roles.GetByIdWithPermissionsAsync(role.Id, ct))!);
    }

    public async Task<RoleDto> UpdateAsync(Guid id, RoleUpdateRequest request, CancellationToken ct = default)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);
        var role = await _roles.GetByIdWithPermissionsAsync(id, ct)
            ?? throw new NotFoundException(ErrorCodes.RoleNotFound, "The requested role was not found.");

        if (role.IsSystem)
            throw new ConflictException(ErrorCodes.SystemRoleLocked, "System roles cannot be modified.");

        var newName = request.Name.Trim();
        if (await _roles.NameExistsAsync(newName, id, ct))
            throw new ConflictException(ErrorCodes.NameExists, $"A role named '{newName}' already exists.");

        role.Name = newName;
        role.Description = request.Description?.Trim();
        role.RolePermissions.Clear();
        await AttachPermissionsAsync(role, request.PermissionIds, ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return ToDto((await _roles.GetByIdWithPermissionsAsync(id, ct))!);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var role = await _roles.GetByIdWithPermissionsAsync(id, ct)
            ?? throw new NotFoundException(ErrorCodes.RoleNotFound, "The requested role was not found.");

        if (role.IsSystem)
            throw new ConflictException(ErrorCodes.SystemRoleLocked, "System roles cannot be deleted.");

        _roles.Remove(role); // soft delete
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task AttachPermissionsAsync(Role role, IReadOnlyCollection<Guid> permissionIds, CancellationToken ct)
    {
        var valid = (await _permissions.GetByIdsAsync(permissionIds.Distinct().ToList(), ct)).ToDictionary(p => p.Id);
        if (valid.Count != permissionIds.Distinct().Count())
            throw new ValidationException("One or more validation errors occurred.",
                [new ValidationFailure("permissionIds", "One or more permissions do not exist.")]);

        foreach (var permission in valid.Values)
            role.RolePermissions.Add(new RolePermission { Role = role, PermissionId = permission.Id });
    }

    private static RoleDto ToDto(Role r) => new(
        r.Id, r.Name, r.Description, r.IsSystem,
        r.RolePermissions.Select(rp => rp.Permission.Name).ToList());
}

/// <summary>Read-only permission catalog lookup (implemented in Infrastructure).</summary>
public interface IPermissionLookup
{
    Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Permission>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
}
