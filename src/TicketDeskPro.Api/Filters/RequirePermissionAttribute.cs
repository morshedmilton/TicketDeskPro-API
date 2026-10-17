using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TicketDeskPro.Api.Middleware;
using TicketDeskPro.Shared.Constants;
using TicketDeskPro.Shared.Wrappers;

namespace TicketDeskPro.Api.Filters;

/// <summary>
/// Fine-grained RBAC gate: [RequirePermission("tickets.read")].
/// Permissions ride the JWT as "permission" claims (additive across roles, BR-039).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class RequirePermissionAttribute : Attribute, IAuthorizationFilter
{
    private readonly string _permission;

    public RequirePermissionAttribute(string permission) => _permission = permission;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Unauthenticated callers get 401 (missing/expired token), authenticated
        // callers without the permission get 403 — the two cases stay distinguishable.
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            var unauthEnvelope = ApiResponse<object>.Fail(
                [new ApiError(ErrorCodes.InvalidTokenState, "Not authenticated.")],
                CorrelationIdMiddleware.GetCorrelationId(context.HttpContext));

            context.Result = new ObjectResult(unauthEnvelope) { StatusCode = StatusCodes.Status401Unauthorized };
            return;
        }

        var hasPermission = context.HttpContext.User
            .HasClaim(c => c.Type == "permissions" && c.Value == _permission);

        if (!hasPermission)
        {
            var envelope = ApiResponse<object>.Fail(
                [new ApiError(ErrorCodes.MissingPermission, "You do not have permission to perform this action.")],
                CorrelationIdMiddleware.GetCorrelationId(context.HttpContext));

            context.Result = new ObjectResult(envelope) { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
