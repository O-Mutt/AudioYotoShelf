using System.Security.Claims;

namespace AudioYotoShelf.Api.Auth;

/// <summary>
/// Helpers for reading the authenticated user's connection identity from their session cookie.
/// The <see cref="ClaimTypes.NameIdentifier"/> claim holds the UserConnection id that was issued
/// at ABS login — this is the trusted source of "who is calling", never the URL.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserConnectionId(this ClaimsPrincipal? principal)
    {
        var raw = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
