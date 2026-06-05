using AudioYotoShelf.Core.DTOs.Admin;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioYotoShelf.Api.Controllers;

/// <summary>
/// Read-only usage analytics for admins (users, logins/sessions, transfers). Gated by the Admin
/// role, which is granted from the IsAdmin flag on the connection at login.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController(AudioYotoShelfDbContext db) : AppControllerBase
{
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var since7 = now.AddDays(-7);
        var since30 = now.AddDays(-30);

        var totalUsers = await db.UserConnections.CountAsync(ct);
        var absConnected = await db.UserConnections.CountAsync(u => u.AudiobookshelfToken != null, ct);
        var yotoConnected = await db.UserConnections.CountAsync(u => u.YotoRefreshToken != null, ct);
        var adminUsers = await db.UserConnections.CountAsync(u => u.IsAdmin, ct);
        var active7 = await db.UserConnections.CountAsync(u => u.LastLoginAt != null && u.LastLoginAt >= since7, ct);
        var active30 = await db.UserConnections.CountAsync(u => u.LastLoginAt != null && u.LastLoginAt >= since30, ct);

        var totalLogins = await db.LoginEvents.CountAsync(ct);
        var logins7 = await db.LoginEvents.CountAsync(e => e.CreatedAt >= since7, ct);
        var logins30 = await db.LoginEvents.CountAsync(e => e.CreatedAt >= since30, ct);

        var totalTransfers = await db.CardTransfers.CountAsync(ct);
        var completed = await db.CardTransfers.CountAsync(t => t.Status == TransferStatus.Completed, ct);
        var failed = await db.CardTransfers.CountAsync(t => t.Status == TransferStatus.Failed, ct);
        var transfers7 = await db.CardTransfers.CountAsync(t => t.CreatedAt >= since7, ct);
        var playlists = await db.Playlists.CountAsync(ct);

        var successRate = totalTransfers == 0
            ? 0
            : Math.Round((double)completed / totalTransfers * 100, 1);

        return Ok(new AdminOverview(
            totalUsers, absConnected, yotoConnected, adminUsers,
            active7, active30,
            totalLogins, logins7, logins30,
            totalTransfers, completed, failed, successRate, transfers7,
            playlists));
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users(CancellationToken ct)
    {
        var users = await db.UserConnections
            .OrderByDescending(u => u.LastLoginAt)
            .Select(u => new AdminUserRow(
                u.Id,
                u.Username,
                u.IsAdmin,
                u.AudiobookshelfToken != null,
                u.YotoRefreshToken != null,
                u.CreatedAt,
                u.LastLoginAt,
                u.LoginEvents.Count,
                u.CardTransfers.Count))
            .ToListAsync(ct);

        return Ok(users);
    }

    [HttpGet("usage")]
    public async Task<IActionResult> Usage([FromQuery] int days = 14, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 90);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(-(days - 1));
        var since = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // Bucket in memory — homelab volumes are small and this avoids provider-specific
        // date-truncation translation.
        var loginTimes = await db.LoginEvents
            .Where(e => e.CreatedAt >= since)
            .Select(e => e.CreatedAt)
            .ToListAsync(ct);
        var transferTimes = await db.CardTransfers
            .Where(t => t.CreatedAt >= since)
            .Select(t => t.CreatedAt)
            .ToListAsync(ct);

        var loginsByDay = loginTimes
            .GroupBy(t => DateOnly.FromDateTime(t.UtcDateTime))
            .ToDictionary(g => g.Key, g => g.Count());
        var transfersByDay = transferTimes
            .GroupBy(t => DateOnly.FromDateTime(t.UtcDateTime))
            .ToDictionary(g => g.Key, g => g.Count());

        var points = Enumerable.Range(0, days)
            .Select(i => startDate.AddDays(i))
            .Select(d => new UsagePoint(d, loginsByDay.GetValueOrDefault(d), transfersByDay.GetValueOrDefault(d)))
            .ToList();

        return Ok(points);
    }
}
