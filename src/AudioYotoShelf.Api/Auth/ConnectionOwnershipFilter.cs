using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AudioYotoShelf.Api.Auth;

/// <summary>
/// Defence-in-depth ownership guard for any endpoint whose route still carries a
/// <c>userConnectionId</c>. Authentication (the fallback policy) already guarantees the caller has
/// a valid session; this additionally requires the <c>userConnectionId</c> in the URL to match the
/// connection bound to that session, so a leaked/guessed id can't be used to act as another user.
/// Endpoints without a <c>userConnectionId</c> route value (login, OAuth callback, health, and the
/// resource-id endpoints that do their own ownership checks) pass through untouched.
/// </summary>
public sealed class ConnectionOwnershipFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.RouteData.Values.TryGetValue("userConnectionId", out var raw) &&
            Guid.TryParse(raw?.ToString(), out var routeId))
        {
            var currentId = context.HttpContext.User.GetUserConnectionId();
            if (currentId is null || currentId.Value != routeId)
            {
                context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
                return;
            }
        }

        await next();
    }
}
