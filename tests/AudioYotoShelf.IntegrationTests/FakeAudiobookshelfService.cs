using AudioYotoShelf.Core.DTOs.Audiobookshelf;
using AudioYotoShelf.Core.Interfaces;

namespace AudioYotoShelf.IntegrationTests;

/// <summary>
/// Stands in for the external Audiobookshelf API so the integration tests exercise the real HTTP
/// pipeline, auth/session, EF/Postgres, and admin logic without reaching a live ABS server.
/// Only the members the tested endpoints touch are implemented.
/// </summary>
public sealed class FakeAudiobookshelfService : IAudiobookshelfService
{
    /// <summary>Username the fake ABS server reports for the next login.</summary>
    public string Username { get; set; } = "alice";

    public Task<AbsLoginResponse> LoginAsync(string baseUrl, string username, string password, CancellationToken ct = default)
    {
        var user = new AbsUser(
            Id: "abs-user-1",
            Username: Username,
            Type: "user",
            Token: "abs-token",
            IsActive: true,
            Permissions: null,
            LibrariesAccessible: ["lib-1"],
            AccessToken: null,
            RefreshToken: null);
        return Task.FromResult(new AbsLoginResponse(user, "lib-1"));
    }

    public Task<bool> ValidateTokenAsync(string baseUrl, string token, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<AbsLibrary[]> GetLibrariesAsync(string baseUrl, string token, CancellationToken ct = default) =>
        Task.FromResult<AbsLibrary[]>([new AbsLibrary("lib-1", "Books", "book", null)]);

    public Task<AbsLoginResponse> RefreshTokenAsync(string baseUrl, string refreshToken, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<AbsLibraryItemsResponse> GetLibraryItemsAsync(string baseUrl, string token, string libraryId, int page = 0, int limit = 20, string? sort = null, bool desc = false, bool collapseSeries = false, string? search = null, string? filter = null, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<AbsLibraryItem[]> SearchLibraryItemsAsync(string baseUrl, string token, string libraryId, string query, int limit = 20, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<AbsLibraryItem> GetLibraryItemAsync(string baseUrl, string token, string itemId, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<Stream> GetCoverImageAsync(string baseUrl, string token, string itemId, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<AbsSeriesResponse> GetSeriesAsync(string baseUrl, string token, string libraryId, int page = 0, int limit = 20, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<AbsSeriesItem> GetSeriesDetailAsync(string baseUrl, string token, string libraryId, string seriesId, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<Stream> DownloadAudioFileAsync(string baseUrl, string token, string itemId, string fileIno, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<(Stream Stream, long ContentLength, string ContentType)> DownloadAudioFileWithMetadataAsync(string baseUrl, string token, string itemId, string fileIno, CancellationToken ct = default) =>
        throw new NotImplementedException();
}
