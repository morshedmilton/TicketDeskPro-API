using FluentValidation;
using TicketDeskPro.Application.DTOs.SlaPolicies;
using TicketDeskPro.Application.Extensions;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Domain.Entities;
using TicketDeskPro.Domain.Interfaces;
using TicketDeskPro.Shared.Constants;
using TicketDeskPro.Shared.Exceptions;

namespace TicketDeskPro.Application.Services;

/// <summary>Admin CRUD for SLA policies (US-5.01). Delete is a soft delete —
/// historical timers still reference the policy for audit.</summary>
public interface ISlaPolicyService
{
    Task<IReadOnlyList<SlaPolicyDto>> GetAllAsync(CancellationToken ct = default);
    Task<SlaPolicyDto> CreateAsync(SlaPolicyCreateRequest request, CancellationToken ct = default);
    Task<SlaPolicyDto> UpdateAsync(Guid id, SlaPolicyUpdateRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class SlaPolicyService : ISlaPolicyService
{
    private readonly ISlaPolicyRepository _policies;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<SlaPolicyCreateRequest> _createValidator;
    private readonly IValidator<SlaPolicyUpdateRequest> _updateValidator;

    public SlaPolicyService(
        ISlaPolicyRepository policies,
        IUnitOfWork unitOfWork,
        IValidator<SlaPolicyCreateRequest> createValidator,
        IValidator<SlaPolicyUpdateRequest> updateValidator)
    {
        _policies = policies;
        _unitOfWork = unitOfWork;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<IReadOnlyList<SlaPolicyDto>> GetAllAsync(CancellationToken ct = default) =>
        (await _policies.GetAllAsync(ct)).Select(ToDto).ToList();

    public async Task<SlaPolicyDto> CreateAsync(SlaPolicyCreateRequest request, CancellationToken ct = default)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);

        if (await _policies.PriorityExistsAsync((int)request.Priority, excludeId: null, ct))
            throw new ConflictException(ErrorCodes.ConcurrencyConflict, $"An SLA policy for {request.Priority} already exists.");

        var policy = new SlaPolicy
        {
            Name = request.Name.Trim(),
            Priority = request.Priority,
            FirstResponseMinutes = request.FirstResponseMinutes,
            ResolutionMinutes = request.ResolutionMinutes,
            IsActive = true
        };
        await _policies.AddAsync(policy, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return ToDto(policy);
    }

    public async Task<SlaPolicyDto> UpdateAsync(Guid id, SlaPolicyUpdateRequest request, CancellationToken ct = default)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);

        var policy = await _policies.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(ErrorCodes.ResSlaPolicyNotFound, "The requested SLA policy was not found.");

        policy.Name = request.Name.Trim();
        policy.FirstResponseMinutes = request.FirstResponseMinutes;
        policy.ResolutionMinutes = request.ResolutionMinutes;
        policy.IsActive = request.IsActive;

        await _unitOfWork.SaveChangesAsync(ct);
        return ToDto(policy);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var policy = await _policies.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(ErrorCodes.ResSlaPolicyNotFound, "The requested SLA policy was not found.");

        _policies.Remove(policy); // soft delete via interceptor
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private static SlaPolicyDto ToDto(SlaPolicy p) =>
        new(p.Id, p.Name, p.Priority, p.FirstResponseMinutes, p.ResolutionMinutes, p.IsActive);
}
