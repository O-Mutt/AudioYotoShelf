using System.Security.Claims;
using AudioYotoShelf.Api.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace AudioYotoShelf.Api.Tests;

public class ConnectionOwnershipFilterTests
{
    private static ActionExecutingContext MakeContext(Guid? routeId, Guid? userId)
    {
        var httpContext = new DefaultHttpContext();
        if (userId is not null)
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "Test");
            httpContext.User = new ClaimsPrincipal(identity);
        }

        var routeData = new RouteData();
        if (routeId is not null)
            routeData.Values["userConnectionId"] = routeId.Value.ToString();

        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
        return new ActionExecutingContext(
            actionContext, [], new Dictionary<string, object?>(), controller: new object());
    }

    private static async Task<bool> RunAsync(ActionExecutingContext ctx)
    {
        var nextCalled = false;
        await new ConnectionOwnershipFilter().OnActionExecutionAsync(ctx, () =>
        {
            nextCalled = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });
        return nextCalled;
    }

    [Fact]
    public async Task Allows_WhenRouteIdMatchesUser()
    {
        var id = Guid.NewGuid();
        var ctx = MakeContext(id, id);

        var nextCalled = await RunAsync(ctx);

        nextCalled.Should().BeTrue();
        ctx.Result.Should().BeNull();
    }

    [Fact]
    public async Task Forbids_WhenRouteIdDiffersFromUser()
    {
        var ctx = MakeContext(Guid.NewGuid(), Guid.NewGuid());

        var nextCalled = await RunAsync(ctx);

        nextCalled.Should().BeFalse();
        ctx.Result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Forbids_WhenRouteIdPresentButUnauthenticated()
    {
        var ctx = MakeContext(Guid.NewGuid(), userId: null);

        var nextCalled = await RunAsync(ctx);

        nextCalled.Should().BeFalse();
        ctx.Result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task PassesThrough_WhenNoUserConnectionIdInRoute()
    {
        // Resource-id endpoints (transferId/playlistId) and anonymous endpoints have no
        // userConnectionId route value — the filter must not block them.
        var ctx = MakeContext(routeId: null, userId: Guid.NewGuid());

        var nextCalled = await RunAsync(ctx);

        nextCalled.Should().BeTrue();
        ctx.Result.Should().BeNull();
    }
}
