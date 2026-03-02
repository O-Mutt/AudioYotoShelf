using System.Net.Http.Headers;
using System.Net.Http.Json;
using AudioYotoShelf.Core.DTOs.Audiobookshelf;
using AudioYotoShelf.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AudioYotoShelf.Infrastructure.Services.Audiobookshelf;

public class AudiobookshelfService(
    IHttpClientFactory httpClientFactory,
    ILogger<AudiobookshelfService> logger) : IAudiobookshelfService
{
    private HttpClient CreateClient(string baseUrl, string token)
    {
        var client = httpClientFactory.CreateClient("Audiobookshelf");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/'));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<AbsLoginResponse> LoginAsync(string baseUrl, string username, string password, CancellationToken ct = default)
    {
        using var client = httpClientFactory.CreateClient("Audiobookshelf");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/'));

        var response = await client.PostAsJsonAsync("/login", new AbsLoginRequest(username, password), ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AbsLoginResponse>(ct)
            ?? throw new InvalidOperationException("Failed to deserialize ABS login response");
    }

    public async Task<bool> ValidateTokenAsync(string baseUrl, string token, CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient(baseUrl, token);
            var response = await client.GetAsync("/api/authorize", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to validate ABS token for {BaseUrl}", baseUrl);
            return false;
        }
    }

    public async Task<AbsLibrary[]> GetLibrariesAsync(string baseUrl, string token, CancellationToken ct = default)
    {
        using var client = CreateClient(baseUrl, token);
        var response = await client.GetAsync("/api/libraries", ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AbsLibrariesWrapper>(ct);
        return result?.Libraries ?? [];
    }

    public async Task<AbsLibraryItemsResponse> GetLibraryItemsAsync(
        string baseUrl, string token, string libraryId,
        int page = 0, int limit = 20, string? sort = null,
        bool collapseSeries = false, string? search = null, string? filter = null,
        CancellationToken ct = default)
    {
        using var client = CreateClient(baseUrl, token);
        var queryParams = $"?page={page}&limit={limit}&minified=1";
        if (sort is not null) queryParams += $"&sort={sort}";
        if (collapseSeries) queryParams += "&collapseseries=1";
        if (!string.IsNullOrWhiteSpace(search))
            queryParams += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(filter))
            queryParams += $"&filter={filter}";

        var response = await client.GetAsync($"/api/libraries/{libraryId}/items{queryParams}", ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AbsLibraryItemsResponse>(ct)
            ?? throw new InvalidOperationException("Failed to deserialize library items");
    }

    public async Task<AbsLibraryItem> GetLibraryItemAsync(string baseUrl, string token, string itemId, CancellationToken ct = default)
    {
        using var client = CreateClient(baseUrl, token);
        var response = await client.GetAsync($"/api/items/{itemId}?expanded=1", ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AbsLibraryItem>(ct)
            ?? throw new InvalidOperationException($"Failed to deserialize library item {itemId}");
    }

    public async Task<Stream> GetCoverImageAsync(string baseUrl, string token, string itemId, CancellationToken ct = default)
    {
        using var client = CreateClient(baseUrl, token);
        var response = await client.GetAsync($"/api/items/{itemId}/cover", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }

    public async Task<AbsSeriesResponse> GetSeriesAsync(
        string baseUrl, string token, string libraryId,
        int page = 0, int limit = 20, CancellationToken ct = default)
    {
        using var client = CreateClient(baseUrl, token);
        var response = await client.GetAsync($"/api/libraries/{libraryId}/series?page={page}&limit={limit}", ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AbsSeriesResponse>(ct)
            ?? throw new InvalidOperationException("Failed to deserialize series response");
    }

    public async Task<AbsSeriesItem> GetSeriesDetailAsync(string baseUrl, string token, string seriesId, CancellationToken ct = default)
    {
        using var client = CreateClient(baseUrl, token);
        var response = await client.GetAsync($"/api/series/{seriesId}", ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AbsSeriesItem>(ct)
            ?? throw new InvalidOperationException($"Failed to deserialize series {seriesId}");
    }

    public async Task<Stream> DownloadAudioFileAsync(
        string baseUrl, string token, string itemId, string fileIno, CancellationToken ct = default)
    {
        var (stream, _, _) = await DownloadAudioFileWithMetadataAsync(baseUrl, token, itemId, fileIno, ct);
        return stream;
    }

    public async Task<(Stream Stream, long ContentLength, string ContentType)> DownloadAudioFileWithMetadataAsync(
        string baseUrl, string token, string itemId, string fileIno, CancellationToken ct = default)
    {
        // Use ResponseHeadersRead to avoid buffering the entire file in memory
        var client = CreateClient(baseUrl, token);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/items/{itemId}/file/{fileIno}/download");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength ?? -1;
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
        var stream = await response.Content.ReadAsStreamAsync(ct);

        logger.LogInformation("Streaming audio file {FileIno} from item {ItemId}: {ContentLength} bytes, {ContentType}",
            fileIno, itemId, contentLength, contentType);

        return (stream, contentLength, contentType);
    }

    // Internal wrapper for the libraries endpoint response shape
    private record AbsLibrariesWrapper(AbsLibrary[] Libraries);
}
