namespace TicketDeskPro.Api.Middleware;

/// <summary>
/// Assigns every request an X-Correlation-ID (inbound if the caller sent one, else a new Guid)
/// so one id ties together API logs, error envelopes and downstream calls.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    private const string ItemKey = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // Accept the caller's id only if it is safe to echo into headers/logs.
        var inbound = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = inbound is not null && System.Text.RegularExpressions.Regex.IsMatch(inbound, @"^[A-Za-z0-9-]{8,64}$")
            ? inbound
            : Guid.NewGuid().ToString();

        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await _next(context);
    }

    public static string GetCorrelationId(HttpContext context) =>
        context.Items[ItemKey] as string ?? context.TraceIdentifier;
}
