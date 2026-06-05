using System.Security.Claims;
using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Data;
using AudioYotoShelf.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioYotoShelf.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    IAudiobookshelfService absService,
    IYotoService yotoService,
    AudioYotoShelfDbContext db,
    ILogger<AuthController> logger) : AppControllerBase
{
    // --- Audiobookshelf Auth ---

    public record AbsConnectRequest(string BaseUrl, string Username, string Password);

    [AllowAnonymous]
    [HttpPost("abs/connect")]
    public async Task<IActionResult> ConnectToAudiobookshelf([FromBody] AbsConnectRequest request, CancellationToken ct)
    {
        var loginResponse = await absService.LoginAsync(request.BaseUrl, request.Username, request.Password, ct);
        var absUser = loginResponse.User;

        var userConnection = await db.UserConnections
            .FirstOrDefaultAsync(u => u.Username == absUser.Username, ct);

        if (userConnection is null)
        {
            userConnection = new UserConnection
            {
                Username = absUser.Username,
                AudiobookshelfUrl = request.BaseUrl.TrimEnd('/'),
                DefaultLibraryId = loginResponse.UserDefaultLibraryId
            };
            AbsTokens.ApplyLogin(userConnection, absUser);
            db.UserConnections.Add(userConnection);
        }
        else
        {
            userConnection.AudiobookshelfUrl = request.BaseUrl.TrimEnd('/');
            AbsTokens.ApplyLogin(userConnection, absUser);
            userConnection.DefaultLibraryId = loginResponse.UserDefaultLibraryId ?? userConnection.DefaultLibraryId;
        }

        await db.SaveChangesAsync(ct);

        // Issue the session cookie that binds every subsequent request to this connection.
        await IssueSessionAsync(userConnection);

        logger.LogInformation("User {Username} connected to ABS at {BaseUrl}", absUser.Username, request.BaseUrl);

        return Ok(new
        {
            UserConnectionId = userConnection.Id,
            Username = absUser.Username,
            AbsConnected = true,
            YotoConnected = userConnection.HasValidYotoConnection,
            DefaultLibraryId = userConnection.DefaultLibraryId,
            Libraries = absUser.LibrariesAccessible
        });
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { LoggedOut = true });
    }

    private async Task IssueSessionAsync(UserConnection user)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }

    [HttpPost("abs/validate")]
    public async Task<IActionResult> ValidateAbsToken(CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([CurrentUserConnectionId], ct);
        if (user is null) return NotFound();

        // Renew the stored access token from the refresh token if it's expiring, then validate it.
        var token = await AbsTokens.EnsureValidAsync(db, absService, user, logger, ct);

        var isValid = await absService.ValidateTokenAsync(
            user.AudiobookshelfUrl, token, ct);

        if (isValid)
            user.AudiobookshelfTokenValidatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return Ok(new { Valid = isValid });
    }

    // --- Yoto OAuth Authorization Code Flow ---

    [HttpGet("yoto/authorize")]
    public async Task<IActionResult> AuthorizeYoto(CancellationToken ct)
    {
        var userConnectionId = CurrentUserConnectionId;
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);
        if (user is null) return NotFound();

        var nonce = Guid.NewGuid().ToString("N");
        user.YotoDeviceCode = nonce; // reuse column for OAuth state nonce
        await db.SaveChangesAsync(ct);

        var state = $"{userConnectionId}:{nonce}";
        var redirectUri = $"{Request.Scheme}://{Request.Host}/api/auth/yoto/callback";
        var authUrl = yotoService.GetAuthorizationUrl(redirectUri, state);

        return Ok(new { authUrl });
    }

    // Top-level redirect back from Yoto. Identity here comes from the single-use, unguessable
    // `state` nonce we persisted on the row in AuthorizeYoto — not from the session — so it stays
    // anonymous-safe while remaining bound to the connection that initiated the flow.
    [AllowAnonymous]
    [HttpGet("yoto/callback")]
    public async Task<IActionResult> YotoCallback(
        [FromQuery] string code, [FromQuery] string state, CancellationToken ct)
    {
        // Parse state = "{userConnectionId}:{nonce}"
        var parts = state.Split(':', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var userConnectionId))
            return BadRequest("Invalid state parameter");

        var nonce = parts[1];
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);
        if (user is null) return NotFound();

        if (user.YotoDeviceCode != nonce)
            return BadRequest("State mismatch — possible CSRF attack");

        var redirectUri = $"{Request.Scheme}://{Request.Host}/api/auth/yoto/callback";
        var tokenResponse = await yotoService.ExchangeAuthCodeAsync(code, redirectUri, ct);

        user.YotoAccessToken = tokenResponse.AccessToken;
        user.YotoRefreshToken = tokenResponse.RefreshToken;
        user.YotoTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        user.YotoDeviceCode = null;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("User {Username} connected to Yoto via auth code flow", user.Username);

        return Redirect("/setup");
    }

    // --- User Settings (Phase 3) ---

    [HttpPatch("settings")]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] Core.DTOs.Transfer.UpdateSettingsRequest request,
        CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([CurrentUserConnectionId], ct);
        if (user is null) return NotFound();

        if (request.DefaultLibraryId is not null)
            user.DefaultLibraryId = request.DefaultLibraryId;

        if (request.DefaultMinAge.HasValue)
            user.DefaultMinAge = request.DefaultMinAge.Value;

        if (request.DefaultMaxAge.HasValue)
            user.DefaultMaxAge = request.DefaultMaxAge.Value;

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Settings updated for user {Username}", user.Username);

        return Ok(MapConnectionStatus(user));
    }

    // --- Connection Status ---

    [HttpGet("status")]
    public async Task<IActionResult> GetConnectionStatus(CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([CurrentUserConnectionId], ct);
        if (user is null) return NotFound();

        return Ok(MapConnectionStatus(user));
    }

    private static object MapConnectionStatus(UserConnection user) => new
    {
        user.Id,
        user.Username,
        AbsConnected = user.HasValidAbsConnection,
        user.AudiobookshelfUrl,
        YotoConnected = user.HasValidYotoConnection,
        YotoTokenExpiresAt = user.YotoTokenExpiresAt,
        user.DefaultLibraryId,
        user.DefaultMinAge,
        user.DefaultMaxAge
    };
}
