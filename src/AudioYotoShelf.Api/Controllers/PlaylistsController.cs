using AudioYotoShelf.Core.DTOs.Playlist;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Services.BackgroundJobs;
using Hangfire;
using Microsoft.AspNetCore.Mvc;

namespace AudioYotoShelf.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlaylistsController(
    IPlaylistService playlists,
    IBackgroundJobClient backgroundJobs,
    ILogger<PlaylistsController> logger) : ControllerBase
{
    [HttpPost("{userConnectionId:guid}")]
    public async Task<IActionResult> Create(
        Guid userConnectionId, [FromBody] CreatePlaylistRequest request, CancellationToken ct)
    {
        var result = await playlists.CreateAsync(userConnectionId, request, ct);
        return CreatedAtAction(nameof(GetDetail), new { playlistId = result.Id }, result);
    }

    [HttpGet("{userConnectionId:guid}")]
    public async Task<IActionResult> List(Guid userConnectionId, CancellationToken ct)
        => Ok(await playlists.ListAsync(userConnectionId, ct));

    [HttpGet("detail/{playlistId:guid}")]
    public async Task<IActionResult> GetDetail(Guid playlistId, CancellationToken ct)
    {
        var result = await playlists.GetAsync(playlistId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPatch("{playlistId:guid}")]
    public async Task<IActionResult> Update(
        Guid playlistId, [FromBody] UpdatePlaylistRequest request, CancellationToken ct)
        => Ok(await playlists.UpdateAsync(playlistId, request, ct));

    [HttpDelete("{playlistId:guid}")]
    public async Task<IActionResult> Delete(Guid playlistId, CancellationToken ct)
    {
        await playlists.DeleteAsync(playlistId, ct);
        return Ok(new { Message = "Playlist deleted" });
    }

    [HttpPost("{playlistId:guid}/items")]
    public async Task<IActionResult> AddItems(
        Guid playlistId, [FromBody] AddPlaylistItemsRequest request, CancellationToken ct)
        => Ok(await playlists.AddItemsAsync(playlistId, request.AbsLibraryItemIds, ct));

    [HttpPost("{playlistId:guid}/series")]
    public async Task<IActionResult> AddSeries(
        Guid playlistId, [FromBody] AddPlaylistSeriesRequest request, CancellationToken ct)
        => Ok(await playlists.AddSeriesAsync(playlistId, request.AbsSeriesId, ct));

    [HttpDelete("{playlistId:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> RemoveItem(Guid playlistId, Guid itemId, CancellationToken ct)
        => Ok(await playlists.RemoveItemAsync(playlistId, itemId, ct));

    [HttpPut("{playlistId:guid}/order")]
    public async Task<IActionResult> Reorder(
        Guid playlistId, [FromBody] ReorderPlaylistRequest request, CancellationToken ct)
        => Ok(await playlists.ReorderAsync(playlistId, request.OrderedItemIds, ct));

    [HttpPatch("{playlistId:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> SetItemGrouping(
        Guid playlistId, Guid itemId, [FromBody] UpdatePlaylistItemRequest request, CancellationToken ct)
        => Ok(await playlists.SetItemGroupingAsync(playlistId, itemId, request.GroupingOverride, ct));

    /// <summary>
    /// Enqueue a transfer of the whole playlist to one MYO card. Blocks (409) when the
    /// playlist exceeds card capacity.
    /// </summary>
    [HttpPost("{playlistId:guid}/transfer")]
    public async Task<IActionResult> Transfer(Guid playlistId, CancellationToken ct)
    {
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
}
