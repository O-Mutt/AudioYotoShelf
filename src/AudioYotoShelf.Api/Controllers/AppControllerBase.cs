using AudioYotoShelf.Api.Auth;
using Microsoft.AspNetCore.Mvc;

namespace AudioYotoShelf.Api.Controllers;

/// <summary>
/// Base for controllers whose actions operate on the caller's own connection. The connection id is
/// taken from the authenticated session cookie — never from the URL — so a request can only ever
/// act as the user it was issued to. The global authenticated-by-default policy guarantees an
/// authenticated principal reaches these actions, and login always stamps the id claim.
/// </summary>
public abstract class AppControllerBase : ControllerBase
{
    protected Guid CurrentUserConnectionId =>
        User.GetUserConnectionId()
        ?? throw new InvalidOperationException("Authenticated principal is missing the connection id claim.");
}
