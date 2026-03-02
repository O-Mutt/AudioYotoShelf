using AudioYotoShelf.Core.DTOs.Audiobookshelf;

namespace AudioYotoShelf.Core.Interfaces;

/// <summary>
/// Handles all communication with the Audiobookshelf API.
/// Each method requires the user's ABS token for per-user permission scoping.
/// </summary>
public interface IAudiobookshelfService
{
    // Auth
    Task<AbsLoginResponse> LoginAsync(string baseUrl, string username, string password, CancellationToken ct = default);
    Task<bool> ValidateTokenAsync(string baseUrl, string token, CancellationToken ct = default);

    // Libraries
    Task<AbsLibrary[]> GetLibrariesAsync(string baseUrl, string token, CancellationToken ct = default);
    Task<AbsLibraryItemsResponse> GetLibraryItemsAsync(string baseUrl, string token, string libraryId, int page = 0, int limit = 20, string? sort = null, bool collapseSeries = false, string? search = null, string? filter = null, CancellationToken ct = default);

    // Books
    Task<AbsLibraryItem> GetLibraryItemAsync(string baseUrl, string token, string itemId, CancellationToken ct = default);
    Task<Stream> GetCoverImageAsync(string baseUrl, string token, string itemId, CancellationToken ct = default);

    // Series
    Task<AbsSeriesResponse> GetSeriesAsync(string baseUrl, string token, string libraryId, int page = 0, int limit = 20, CancellationToken ct = default);
    Task<AbsSeriesItem> GetSeriesDetailAsync(string baseUrl, string token, string seriesId, CancellationToken ct = default);

    // File download (streams to avoid memory buffering)
    Task<Stream> DownloadAudioFileAsync(string baseUrl, string token, string itemId, string fileIno, CancellationToken ct = default);
    Task<(Stream Stream, long ContentLength, string ContentType)> DownloadAudioFileWithMetadataAsync(string baseUrl, string token, string itemId, string fileIno, CancellationToken ct = default);
}
