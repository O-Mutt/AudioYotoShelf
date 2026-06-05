using AudioYotoShelf.Core.DTOs.Playlist;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Data;
using AudioYotoShelf.Infrastructure.Services.BackgroundJobs;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioYotoShelf.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlaylistsController(
    IPlaylistService playlists,
    IBackgroundJobClient backgroundJobs,
    AudioYotoShelfDbContext db,
    ILogger<PlaylistsController> logger) : AppControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreatePlaylistRequest request, CancellationToken ct)
    {
        var result = await playlists.CreateAsync(CurrentUserConnectionId, request, ct);
        return CreatedAtAction(nameof(GetDetail), new { playlistId = result.Id }, result);
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await playlists.ListAsync(CurrentUserConnectionId, ct));

    [HttpGet("detail/{playlistId:guid}")]
    public async Task<IActionResult> GetDetail(Guid playlistId, CancellationToken ct)
    {
        if (!await OwnsPlaylistAsync(playlistId, ct)) return NotFound();
        var result = await playlists.GetAsync(playlistId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPatch("{playlistId:guid}")]
    public async Task<IActionResult> Update(
        Guid playlistId, [FromBody] UpdatePlaylistRequest request, CancellationToken ct)
    {
        if (!await OwnsPlaylistAsync(playlistId, ct)) return NotFound();
        return Ok(await playlists.UpdateAsync(playlistId, request, ct));
    }

    [HttpDelete("{playlistId:guid}")]
    public async Task<IActionResult> Delete(Guid playlistId, CancellationToken ct)
    {
        if (!await OwnsPlaylistAsync(playlistId, ct)) return NotFound();
        await playlists.DeleteAsync(playlistId, ct);
        return Ok(new { Message = "Playlist deleted" });
    }

    [HttpPost("{playlistId:guid}/items")]
    public async Task<IActionResult> AddItems(
        Guid playlistId, [FromBody] AddPlaylistItemsRequest request, CancellationToken ct)
    {
        if (!await OwnsPlaylistAsync(playlistId, ct)) return NotFound();
        return Ok(await playlists.AddItemsAsync(playlistId, request.AbsLibraryItemIds, ct));
    }

    [HttpPost("{playlistId:guid}/series")]
    public async Task<IActionResult> AddSeries(
        Guid playlistId, [FromBody] AddPlaylistSeriesRequest request, CancellationToken ct)
    {
        if (!await OwnsPlaylistAsync(playlistId, ct)) return NotFound();
        return Ok(await playlists.AddSeriesAsync(playlistId, request.AbsSeriesId, ct));
    }

    [HttpDelete("{playlistId:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> RemoveItem(Guid playlistId, Guid itemId, CancellationToken ct)
    {
        if (!await OwnsPlaylistAsync(playlistId, ct)) return NotFound();
        return Ok(await playlists.RemoveItemAsync(playlistId, itemId, ct));
    }

    [HttpPut("{playlistId:guid}/order")]
    public async Task<IActionResult> Reorder(
        Guid playlistId, [FromBody] ReorderPlaylistRequest request, CancellationToken ct)
    {
        if (!await OwnsPlaylistAsync(playlistId, ct)) return NotFound();
        return Ok(await playlists.ReorderAsync(playlistId, request.OrderedItemIds, ct));
    }

    [HttpPatch("{playlistId:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> SetItemGrouping(
        Guid playlistId, Guid itemId, [FromBody] UpdatePlaylistItemRequest request, CancellationToken ct)
    {
        if (!await OwnsPlaylistAsync(playlistId, ct)) return NotFound();
        return Ok(await playlists.SetItemGroupingAsync(playlistId, itemId, request.GroupingOverride, ct));
    }

    /// <summary>
    /// Enqueue a transfer of the whole playlist to one MYO card. Blocks (409) when the
    /// playlist exceeds card capacity.
    /// </summary>
    [HttpPost("{playlistId:guid}/transfer")]
    public async Task<IActionResult> Transfer(Guid playlistId, CancellationToken ct)
    {
        if (!await OwnsPlaylistAsync(playlistId, ct)) return NotFound();

        var playlist = await playlists.GetAsync(playlistId, ct);
        if (playlist is null) return NotFound();

        if (!playlist.Capacity.WithinLimits)
        {
            return Conflict(new
            {
                Message = "Playlist exceeds Yoto card capacity. Remove books until it fits.",
                playlist.Capacity
            });
        }

        var jobId = backgroundJobs.Enqueue<IPlaylistJobService>(
            svc => svc.ExecutePlaylistTransferAsync(playlistId, CancellationToken.None));

        logger.LogInformation("Playlist transfer queued: {PlaylistId} -> Job {JobId}", playlistId, jobId);
        return Accepted(new { PlaylistId = playlistId, JobId = jobId, Message = "Playlist transfer queued" });
    }

    /// <summary>True when the playlist exists and belongs to the authenticated connection.</summary>
    private async Task<bool> OwnsPlaylistAsync(Guid playlistId, CancellationToken ct)
    {
        var owner = await db.Playlists
            .Where(p => p.Id == playlistId)
            .Select(p => (Guid?)p.UserConnectionId)
            .FirstOrDefaultAsync(ct);
        return owner is not null && owner == CurrentUserConnectionId;
    }
}
