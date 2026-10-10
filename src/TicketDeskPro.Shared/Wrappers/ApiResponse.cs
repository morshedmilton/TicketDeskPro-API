namespace TicketDeskPro.Shared.Wrappers;

/// <summary>
/// Standard response envelope (FRD §6): every API response — success or error —
/// has the same shape so clients can parse one structure everywhere.
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public List<ApiError>? Errors { get; set; }
    public string? TraceId { get; set; }
    public object? Meta { get; set; }

    public static ApiResponse<T> Ok(T data, object? meta = null) => new()
    {
        Success = true,
        Data = data,
        Meta = meta
    };

    public static ApiResponse<T> Fail(IEnumerable<ApiError> errors, string? traceId = null) => new()
    {
        Success = false,
        Errors = errors.ToList(),
        TraceId = traceId
    };
}

public record ApiError(string Code, string Message, string? Field = null);
