using FluentValidation;
using TicketDeskPro.Shared.Exceptions;
using SharedValidationException = TicketDeskPro.Shared.Exceptions.ValidationException;

namespace TicketDeskPro.Application.Extensions;

public static class ValidationExtensions
{
    /// <summary>
    /// Named EnsureValidAsync (not ValidateAndThrowAsync) to avoid colliding with FluentValidation's
    /// own extension of the same name. Converts failures into our 422 envelope exception.
    /// </summary>
    public static async Task EnsureValidAsync<T>(this IValidator<T> validator, T instance, CancellationToken ct = default)
    {
        var result = await validator.ValidateAsync(instance, ct);
        if (result.IsValid) return;

        var failures = result.Errors
            .Select(e => new ValidationFailure(e.PropertyName, e.ErrorMessage))
            .ToList();

        throw new SharedValidationException("One or more validation errors occurred.", failures);
    }
}
