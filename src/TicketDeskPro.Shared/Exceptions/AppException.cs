namespace TicketDeskPro.Shared.Exceptions;

/// <summary>
/// Base for all business exceptions the API turns into structured error responses.
/// Carries the HTTP status and a stable machine-readable error code (FRD §15 matrix).
/// </summary>
public abstract class AppException : Exception
{
    public abstract int StatusCode { get; }
    public string Code { get; }

    protected AppException(string code, string message) : base(message)
    {
        Code = code;
    }
}

public class NotFoundException : AppException
{
    public override int StatusCode => 404;
    public NotFoundException(string code, string message) : base(code, message) { }
}

public class ConflictException : AppException
{
    public override int StatusCode => 409;
    public ConflictException(string code, string message) : base(code, message) { }
}

public class ForbiddenException : AppException
{
    public override int StatusCode => 403;
    public ForbiddenException(string code, string message) : base(code, message) { }
}

public class UnauthorizedException : AppException
{
    public override int StatusCode => 401;
    public UnauthorizedException(string code, string message) : base(code, message) { }
}

public class RateLimitedException : AppException
{
    public override int StatusCode => 429;
    public RateLimitedException(string code, string message) : base(code, message) { }
}

/// <summary>Dependency failure surfaced as a controlled 503 (e.g. Redis down, SYS_002).</summary>
public class ServiceUnavailableException : AppException
{
    public override int StatusCode => 503;
    public ServiceUnavailableException(string code, string message) : base(code, message) { }
}

public class ValidationException : AppException
{
    public override int StatusCode => 422;

    /// <summary>Per-field errors: [{ field, message }].</summary>
    public IReadOnlyList<ValidationFailure> Failures { get; }

    public ValidationException(string message, IReadOnlyList<ValidationFailure>? failures = null)
        : base("VALIDATION_ERROR", message)
    {
        Failures = failures ?? [];
    }
}

public record ValidationFailure(string Field, string Message);
