using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioYotoShelf.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    IAudiobookshelfService absService,
    IYotoService yotoService,
    AudioYotoShelfDbContext db,
    ILogger<AuthController> logger) : ControllerBase
{
    // --- Audiobookshelf Auth ---

    public record AbsConnectRequest(string BaseUrl, string Username, string Password);

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
                AudiobookshelfToken = absUser.Token,
                AudiobookshelfTokenValidatedAt = DateTimeOffset.UtcNow,
                DefaultLibraryId = loginResponse.UserDefaultLibraryId
            };
            db.UserConnections.Add(userConnection);
        }
        else
        {
            userConnection.AudiobookshelfUrl = request.BaseUrl.TrimEnd('/');
            userConnection.AudiobookshelfToken = absUser.Token;
            userConnection.AudiobookshelfTokenValidatedAt = DateTimeOffset.UtcNow;
            userConnection.DefaultLibraryId = loginResponse.UserDefaultLibraryId ?? userConnection.DefaultLibraryId;
        }

        await db.SaveChangesAsync(ct);

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

    [HttpPost("abs/validate/{userConnectionId:guid}")]
    public async Task<IActionResult> ValidateAbsToken(Guid userConnectionId, CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);
        if (user is null) return NotFound();

        var isValid = await absService.ValidateTokenAsync(
            user.AudiobookshelfUrl, user.AudiobookshelfToken ?? "", ct);

        if (isValid)
            user.AudiobookshelfTokenValidatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return Ok(new { Valid = isValid });
    }

    // --- Yoto OAuth Device Flow ---

    [HttpPost("yoto/initiate/{userConnectionId:guid}")]
    public async Task<IActionResult> InitiateYotoAuth(Guid userConnectionId, CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);
        if (user is null) return NotFound();

        var deviceCodeResponse = await yotoService.InitiateDeviceAuthAsync(ct);

        user.YotoDeviceCode = deviceCodeResponse.DeviceCode;
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            deviceCodeResponse.UserCode,
            deviceCodeResponse.VerificationUri,
            deviceCodeResponse.VerificationUriComplete,
            deviceCodeResponse.ExpiresIn,
            deviceCodeResponse.Interval
        });
    }

    [HttpPost("yoto/poll/{userConnectionId:guid}")]
    public async Task<IActionResult> PollYotoAuth(Guid userConnectionId, CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);
        if (user?.YotoDeviceCode is null) return BadRequest("No pending device authorization");

        var tokenResponse = await yotoService.PollForTokenAsync(user.YotoDeviceCode, ct);

        if (tokenResponse is null)
            return Ok(new { Status = "pending" });

        user.YotoAccessToken = tokenResponse.AccessToken;
        user.YotoRefreshToken = tokenResponse.RefreshToken;
        user.YotoTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        user.YotoDeviceCode = null;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("User {Username} connected to Yoto", user.Username);

        return Ok(new { Status = "authorized", YotoConnected = true });
    }

    // --- User Settings (Phase 3) ---

    [HttpPatch("settings/{userConnectionId:guid}")]
    public async Task<IActionResult> UpdateSettings(
        Guid userConnectionId,
        [FromBody] Core.DTOs.Transfer.UpdateSettingsRequest request,
        CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);
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

    [HttpGet("status/{userConnectionId:guid}")]
    public async Task<IActionResult> GetConnectionStatus(Guid userConnectionId, CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct);
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
