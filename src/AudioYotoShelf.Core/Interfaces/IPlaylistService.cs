using AudioYotoShelf.Core.DTOs.Playlist;

namespace AudioYotoShelf.Core.Interfaces;

/// <summary>
/// Manages multi-book playlists destined for a single Yoto MYO card: CRUD, item
/// management, and live capacity calculation against the card limits.
/// </summary>
public interface IPlaylistService
{
    Task<PlaylistResponse> CreateAsync(Guid userConnectionId, CreatePlaylistRequest request, CancellationToken ct = default);
    Task<PlaylistSummaryResponse[]> ListAsync(Guid userConnectionId, CancellationToken ct = default);
    Task<PlaylistResponse?> GetAsync(Guid playlistId, CancellationToken ct = default);
    Task<PlaylistResponse> UpdateAsync(Guid playlistId, UpdatePlaylistRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid playlistId, CancellationToken ct = default);

    Task<PlaylistResponse> AddItemsAsync(Guid playlistId, IReadOnlyList<string> absLibraryItemIds, CancellationToken ct = default);
    Task<PlaylistResponse> AddSeriesAsync(Guid playlistId, string absSeriesId, CancellationToken ct = default);
    Task<PlaylistResponse> RemoveItemAsync(Guid playlistId, Guid itemId, CancellationToken ct = default);
    Task<PlaylistResponse> ReorderAsync(Guid playlistId, IReadOnlyList<Guid> orderedItemIds, CancellationToken ct = default);
    Task<PlaylistResponse> SetItemGroupingAsync(Guid playlistId, Guid itemId, Enums.TrackGrouping? groupingOverride, CancellationToken ct = default);
}
