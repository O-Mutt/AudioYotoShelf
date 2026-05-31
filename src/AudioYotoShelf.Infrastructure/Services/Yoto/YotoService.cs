using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AudioYotoShelf.Core.DTOs.Yoto;
using AudioYotoShelf.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AudioYotoShelf.Infrastructure.Services.Yoto;

public class YotoService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<YotoService> logger) : IYotoService
{
    private const string YotoApiBase = "https://api.yotoplay.com";
    private const string YotoAuthBase = "https://login.yotoplay.com";
    private const int MaxTranscodePollAttempts = 360;
    private const int TranscodePollDelayMs = 5000;
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    private string ClientId => configuration["Yoto:ClientId"]
        ?? throw new InvalidOperationException("Yoto:ClientId not configured");
    private string? ClientSecret => configuration["Yoto:ClientSecret"];

    private HttpClient CreateApiClient(string accessToken)
    {
        var client = httpClientFactory.CreateClient("Yoto");
        client.BaseAddress = new Uri(YotoApiBase);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    // --- OAuth Authorization Code Flow ---

    public string GetAuthorizationUrl(string redirectUri, string state)
    {
        var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
        query["response_type"] = "code";
        query["client_id"] = ClientId;
        query["redirect_uri"] = redirectUri;
        query["scope"] = "profile offline_access openid";
        query["audience"] = YotoApiBase;
        query["state"] = state;
        return $"{YotoAuthBase}/authorize?{query}";
    }

    public async Task<YotoTokenResponse> ExchangeAuthCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        using var client = httpClientFactory.CreateClient("YotoAuth");
        client.BaseAddress = new Uri(YotoAuthBase);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = ClientId,
        };
        if (!string.IsNullOrEmpty(ClientSecret))
            form["client_secret"] = ClientSecret;

        var response = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(form), ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Yoto auth code exchange failed: {StatusCode} {Body}", response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }

        return await response.Content.ReadFromJsonAsync<YotoTokenResponse>(ct)
            ?? throw new InvalidOperationException("Failed to deserialize token response");
    }

    public async Task<YotoTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        using var client = httpClientFactory.CreateClient("YotoAuth");
        client.BaseAddress = new Uri(YotoAuthBase);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
        };
        if (!string.IsNullOrEmpty(ClientSecret))
            form["client_secret"] = ClientSecret;

        var response = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(form), ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<YotoTokenResponse>(ct)
            ?? throw new InvalidOperationException("Failed to refresh Yoto token");
    }

    // --- Cards ---

    public async Task<YotoCard[]> GetUserCardsAsync(string accessToken, CancellationToken ct = default)
    {
        using var client = CreateApiClient(accessToken);
        var response = await client.GetAsync("/card/family/library/mine?showDeleted=false", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        logger.LogDebug("Yoto family library response: {Body}", json.Length > 700 ? json[..700] : json);

        var result = JsonSerializer.Deserialize<YotoCardListResponse>(json, JsonWeb);
        return result?.Cards ?? [];
    }

    public async Task<YotoCard> GetCardContentAsync(string accessToken, string cardId, CancellationToken ct = default)
    {
        using var client = CreateApiClient(accessToken);
        var response = await client.GetAsync($"/content/{cardId}", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        logger.LogDebug("Yoto card content {CardId} response: {Body}", cardId, json.Length > 700 ? json[..700] : json);

        // GET /content/{id} nests the card under a "card" object and omits the id from the body.
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var cardNode = root.TryGetProperty("card", out var c) && c.ValueKind == JsonValueKind.Object ? c : root;

        var content = cardNode.TryGetProperty("content", out var ce) && ce.ValueKind == JsonValueKind.Object
            ? ce.Deserialize<YotoCardContent>(JsonWeb) : null;
        var metadata = cardNode.TryGetProperty("metadata", out var me) && me.ValueKind == JsonValueKind.Object
            ? me.Deserialize<YotoCardMetadata>(JsonWeb) : null;

        return new YotoCard(cardId, content, metadata);
    }

    public async Task<string> CreateOrUpdateCardAsync(
        string accessToken, YotoCardContent content, YotoCardMetadata metadata,
        string? existingCardId = null, CancellationToken ct = default)
    {
        using var client = CreateApiClient(accessToken);

        var body = new { cardId = existingCardId, content, metadata };
        var response = await client.PostAsJsonAsync("/content", body, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Yoto card create/update failed: {StatusCode} {Body}", response.StatusCode, responseBody);
            response.EnsureSuccessStatusCode();
        }

        var cardId = ExtractCardId(responseBody);
        if (cardId is null)
            logger.LogWarning("Yoto card create/update returned no recognizable cardId: {Body}",
                responseBody.Length > 600 ? responseBody[..600] : responseBody);

        return cardId ?? throw new InvalidOperationException("No cardId returned from Yoto");
    }

    public async Task DeleteCardAsync(string accessToken, string cardId, CancellationToken ct = default)
    {
        using var client = CreateApiClient(accessToken);
        var response = await client.DeleteAsync($"/content/{cardId}", ct);
        response.EnsureSuccessStatusCode();
    }

    // --- Audio Upload Pipeline ---

    public async Task<YotoUploadInfo> GetUploadUrlAsync(string accessToken, CancellationToken ct = default)
    {
        using var client = CreateApiClient(accessToken);
        var response = await client.GetAsync("/media/transcode/audio/uploadUrl", ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<YotoUploadUrlResponse>(ct);
        return result?.Upload ?? throw new InvalidOperationException("No upload URL returned");
    }

    public async Task UploadAudioFileAsync(
        string uploadUrl, Stream audioStream, long contentLength, string contentType, CancellationToken ct = default)
    {
        using var client = httpClientFactory.CreateClient("YotoUpload");
        client.Timeout = TimeSpan.FromMinutes(30); // Large files may take a while

        using var content = new StreamContent(audioStream);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        if (contentLength > 0) content.Headers.ContentLength = contentLength;

        var response = await client.PutAsync(uploadUrl, content, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<YotoTranscodeResponse> PollTranscodeStatusAsync(
        string accessToken, string uploadId, CancellationToken ct = default)
    {
        using var client = CreateApiClient(accessToken);

        for (var attempt = 0; attempt < MaxTranscodePollAttempts; attempt++)
        {
            var response = await client.GetAsync(
                $"/media/upload/{uploadId}/transcoded?loudnorm=false", ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);

            // Raw body once per upload (Debug) for diagnosing response-shape changes.
            if (attempt == 0)
                logger.LogDebug("Transcode response for {UploadId}: {Body}",
                    uploadId, json.Length > 600 ? json[..600] : json);

            var result = ParseTranscodeResponse(json);
            if (result.TranscodedSha256 is not null)
            {
                logger.LogInformation("Transcode complete for upload {UploadId}: SHA256={Sha256}",
                    uploadId, result.TranscodedSha256);
                return result;
            }

            if (attempt % 10 == 0)
                logger.LogInformation("Transcode poll {Attempt}/{Max} for {UploadId}: status={Status}",
                    attempt, MaxTranscodePollAttempts, uploadId, result.Status ?? "null");

            await Task.Delay(TranscodePollDelayMs, ct);
        }

        var elapsedMinutes = MaxTranscodePollAttempts * TranscodePollDelayMs / 60_000.0;
        throw new TimeoutException($"Transcode polling timed out for upload {uploadId} after {MaxTranscodePollAttempts} attempts (~{elapsedMinutes:F0} min)");
    }

    /// <summary>
    /// The POST /content response returns the card id, but the exact key/nesting varies
    /// (cardId / id / contentId, sometimes under a "card" wrapper). Find it tolerantly.
    /// </summary>
    private static string? ExtractCardId(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var doc = JsonDocument.Parse(json);
        return FindCardId(doc.RootElement, depth: 0);
    }

    private static string? FindCardId(JsonElement element, int depth)
    {
        if (element.ValueKind != JsonValueKind.Object || depth > 3) return null;

        foreach (var name in (ReadOnlySpan<string>)["cardId", "id", "contentId"])
            if (element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();

        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                var found = FindCardId(prop.Value, depth + 1);
                if (found is not null) return found;
            }
        }
        return null;
    }

    /// <summary>
    /// Yoto nests the transcode payload under a "transcode" object (like the upload endpoint nests
    /// under "upload"); older/other shapes put it at the root. Read whichever is present.
    /// </summary>
    private static YotoTranscodeResponse ParseTranscodeResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var node = root.ValueKind == JsonValueKind.Object
                   && root.TryGetProperty("transcode", out var transcode)
                   && transcode.ValueKind == JsonValueKind.Object
            ? transcode
            : root;

        string? Str(string name) =>
            node.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

        return new YotoTranscodeResponse(
            Str("transcodedSha256"),
            TranscodedInfo: null,
            Str("status") ?? Str("transcodeStatus"));
    }

    public async Task<string> UploadAndTranscodeAsync(
        string accessToken, Stream audioStream, long contentLength, string contentType,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        // Step 1: Get upload URL
        progress?.Report(10);
        var uploadInfo = await GetUploadUrlAsync(accessToken, ct);

        // Step 2: Upload file
        progress?.Report(30);
        await UploadAudioFileAsync(uploadInfo.UploadUrl, audioStream, contentLength, contentType, ct);

        // Step 3: Poll for transcode completion
        progress?.Report(60);
        var transcodeResult = await PollTranscodeStatusAsync(accessToken, uploadInfo.UploadId, ct);

        progress?.Report(100);
        return transcodeResult.TranscodedSha256!;
    }

    // --- Icons ---

    public async Task<YotoPublicIcon[]> GetPublicIconsAsync(string accessToken, CancellationToken ct = default)
    {
        using var client = CreateApiClient(accessToken);
        var response = await client.GetAsync("/media/displayIcons/public", ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<YotoPublicIcon[]>(ct) ?? [];
    }

    public async Task<YotoIconUploadResponse> UploadCustomIconAsync(
        string accessToken, byte[] iconData, string filename, CancellationToken ct = default)
    {
        using var client = CreateApiClient(accessToken);
        using var formContent = new MultipartFormDataContent();
        formContent.Add(new ByteArrayContent(iconData), "file", filename);

        var response = await client.PostAsync(
            $"/media/displayIcons/user/me/upload?autoConvert=true&filename={Uri.EscapeDataString(filename)}", formContent, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<YotoIconUploadResponse>(ct)
            ?? throw new InvalidOperationException("Failed to upload custom icon");
    }

    // --- Cover ---

    public async Task<string> UploadCoverImageAsync(string accessToken, Stream imageStream, CancellationToken ct = default)
    {
        using var client = CreateApiClient(accessToken);
        using var formContent = new MultipartFormDataContent();
        formContent.Add(new StreamContent(imageStream), "file", "cover.jpg");

        var response = await client.PostAsync("/media/cover/upload?autoconvert=true", formContent, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<YotoCoverUploadResponse>(ct);
        return result?.Url ?? throw new InvalidOperationException("No cover URL returned");
    }
}
