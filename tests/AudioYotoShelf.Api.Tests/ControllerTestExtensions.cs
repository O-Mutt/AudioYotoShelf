using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AudioYotoShelf.Api.Tests;

internal static class ControllerTestExtensions
{
    /// <summary>
    /// Attaches an authenticated principal (carrying the given connection id as the
    /// NameIdentifier claim) so in-controller ownership checks resolve the current user.
    /// </summary>
    public static T AsUser<T>(this T controller, Guid userConnectionId) where T : ControllerBase
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userConnectionId.ToString())], "Test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        return controller;
    }
}
