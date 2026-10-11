using System.Text.Json;
using TicketDeskPro.Shared.Constants;
using TicketDeskPro.Shared.Exceptions;
using TicketDeskPro.Shared.Wrappers;

namespace TicketDeskPro.Api.Middleware;

/// <summary>
/// Turns every exception into the standard error envelope (FRD §6/§7.1):
/// { success:false, errors:[{code,message,field}], traceId } with the right HTTP status.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    // Same camelCase contract as MVC controller responses — one envelope style everywhere.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var (status, errors) = Map(exception);

        if (status >= 500)
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            _logger.LogInformation("Handled {ExceptionType}: {Message}", exception.GetType().Name, exception.Message);

        var envelope = ApiResponse<object>.Fail(errors, CorrelationIdMiddleware.GetCorrelationId(context));

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(envelope, JsonOptions));
    }

    private static (int Status, List<ApiError> Errors) Map(Exception exception) => exception switch
    {
        ValidationException v => (v.StatusCode, v.Failures
            .Select(f => new ApiError(v.Code, f.Message, f.Field)).ToList()),

        AppException a => (a.StatusCode, [new ApiError(a.Code, a.Message)]),

        FluentValidation.ValidationException fv => (422, fv.Errors
            .Select(e => new ApiError(ErrorCodes.Validation, e.ErrorMessage, e.PropertyName)).ToList()),

        BadHttpRequestException br => (400, [new ApiError(ErrorCodes.Validation, br.Message)]),

        // A racing writer or duplicate insert is a client-visible conflict, not a server bug.
        Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => (409,
            [new ApiError(ErrorCodes.ConcurrencyConflict, "The resource has been modified by another user.")]),

        Microsoft.EntityFrameworkCore.DbUpdateException => (409,
            [new ApiError(ErrorCodes.ConcurrencyConflict, "The resource was changed concurrently. Refresh and retry.")]),

        _ => (500, [new ApiError(ErrorCodes.Unexpected, "An unexpected error occurred.")])
    };
}
