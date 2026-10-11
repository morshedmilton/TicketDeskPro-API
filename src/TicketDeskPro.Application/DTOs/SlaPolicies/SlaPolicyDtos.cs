using FluentValidation;
using TicketDeskPro.Domain.Enums;

namespace TicketDeskPro.Application.DTOs.SlaPolicies;

public record SlaPolicyDto(Guid Id, string Name, TicketPriority Priority, int FirstResponseMinutes, int ResolutionMinutes, bool IsActive);

public record SlaPolicyCreateRequest(string Name, TicketPriority Priority, int FirstResponseMinutes, int ResolutionMinutes);

public record SlaPolicyUpdateRequest(string Name, int FirstResponseMinutes, int ResolutionMinutes, bool IsActive);

public class SlaPolicyCreateValidator : AbstractValidator<SlaPolicyCreateRequest>
{
    public SlaPolicyCreateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.FirstResponseMinutes).InclusiveBetween(1, 43200); // 30 days max
        RuleFor(x => x.ResolutionMinutes).InclusiveBetween(1, 43200)
            .GreaterThanOrEqualTo(x => x.FirstResponseMinutes)
            .WithMessage("Resolution time must be at least the first response time.");
    }
}

public class SlaPolicyUpdateValidator : AbstractValidator<SlaPolicyUpdateRequest>
{
    public SlaPolicyUpdateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.FirstResponseMinutes).InclusiveBetween(1, 43200);
        RuleFor(x => x.ResolutionMinutes).InclusiveBetween(1, 43200)
            .GreaterThanOrEqualTo(x => x.FirstResponseMinutes)
            .WithMessage("Resolution time must be at least the first response time.");
    }
}
