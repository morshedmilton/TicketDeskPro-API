using FluentValidation;
using TicketDeskPro.Application.DTOs.Tickets;

namespace TicketDeskPro.Application.Validators.Tickets;

// Length rules from PRD BR-007/BR-008/BR-024.
public class TicketCreateDtoValidator : AbstractValidator<TicketCreateRequest>
{
    public TicketCreateDtoValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MinimumLength(5).MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MinimumLength(10).MaximumLength(5000);
        RuleFor(x => x.CategoryId).NotEmpty();
    }
}

public class TicketUpdateDtoValidator : AbstractValidator<TicketUpdateRequest>
{
    public TicketUpdateDtoValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MinimumLength(5).MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MinimumLength(10).MaximumLength(5000);
        RuleFor(x => x.CategoryId).NotEmpty();
    }
}

public class TicketStatusChangeDtoValidator : AbstractValidator<TicketStatusChangeRequest>
{
    public TicketStatusChangeDtoValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public class TicketMessageCreateDtoValidator : AbstractValidator<TicketMessageCreateRequest>
{
    public TicketMessageCreateDtoValidator()
    {
        RuleFor(x => x.Content).NotEmpty().MinimumLength(1).MaximumLength(5000);
    }
}
