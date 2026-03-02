using System.Net.Http.Headers;
using System.Net.Http.Json;
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
    private const int MaxTranscodePollAttempts = 60;
    private const int TranscodePollDelayMs = 500;

    private string ClientId => configuration["Yoto:ClientId"]
        ?? throw new InvalidOperationException("Yoto:ClientId not configured");
    private string ClientSecret => configuration["Yoto:ClientSecret"]
        ?? throw new InvalidOperationException("Yoto:ClientSecret not configured");

    private HttpClient CreateApiClient(string accessToken)
    {
        var client = httpClientFactory.CreateClient("Yoto");
        client.BaseAddress = new Uri(YotoApiBase);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    // --- OAuth Device Flow ---

    public async Task<YotoDeviceCodeResponse> InitiateDeviceAuthAsync(CancellationToken ct = default)
    {
        using var client = httpClientFactory.CreateClient("YotoAuth");
        client.BaseAddress = new Uri(YotoAuthBase);

        var response = await client.PostAsJsonAsync("/oauth/device/code", new YotoDeviceCodeRequest(
            ClientId,
            "profile offline_access openid",
            YotoApiBase
        ), ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<YotoDeviceCodeResponse>(ct)
            ?? throw new InvalidOperationException("Failed to deserialize device code response");
    }

    public async Task<YotoTokenResponse?> PollForTokenAsync(string deviceCode, CancellationToken ct = default)
    {
        using var client = httpClientFactory.CreateClient("YotoAuth");
        client.BaseAddress = new Uri(YotoAuthBase);

        var response = await client.PostAsJsonAsync("/oauth/token", new YotoTokenRequest(
            "urn:ietf:params:oauth:grant-type:device_code",
            deviceCode,
            ClientId,
            ClientSecret,
            null
        ), ct);

        if (!response.IsSuccessStatusCode)
        {
            // "authorization_pending" means user hasn't authorized yet — not an error
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            if (errorBody.Contains("authorization_pending", StringComparison.OrdinalIgnoreCase))
                return null;

            logger.LogWarning("Yoto token poll failed: {StatusCode} {Body}", response.StatusCode, errorBody);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<YotoTokenResponse>(ct);
    }

    public async Task<YotoTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        using var client = httpClientFactory.CreateClient("YotoAuth");
        client.BaseAddress = new Uri(YotoAuthBase);

        var response = await client.PostAsJsonAsync("/oauth/token", new YotoTokenRequest(
            "refresh_token",
            null,
            ClientId,
            ClientSecret,
            refreshToken
        ), ct);
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

        var result = await response.Content.ReadFromJsonAsync<YotoCardListResponse>(ct);
        return result?.Cards ?? [];
    }

    public async Task<YotoCard> GetCardContentAsync(string accessToken, string cardId, CancellationToken ct = default)
    {
        using var client = CreateApiClient(accessToken);
        var response = await client.GetAsync($"/content/{cardId}", ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<YotoCard>(ct)
            ?? throw new InvalidOperationException($"Failed to get card {cardId}");
    }

    public async Task<string> CreateOrUpdateCardAsync(
        string accessToken, YotoCardContent content, YotoCardMetadata metadata,
        string? existingCardId = null, CancellationToken ct = default)
    {
        using var client = CreateApiClient(accessToken);

        var body = new { cardId = existingCardId, content, metadata };
        var response = await client.PostAsJsonAsync("/content", body, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<YotoCard>(ct);
        return result?.CardId ?? throw new InvalidOperationException("No cardId returned from Yoto");
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

            var result = await response.Content.ReadFromJsonAsync<YotoTranscodeResponse>(ct);
            if (result?.TranscodedSha256 is not null)
            {
                logger.LogInformation("Transcode complete for upload {UploadId}: SHA256={Sha256}",
                    uploadId, result.TranscodedSha256);
                return result;
            }

            await Task.Delay(TranscodePollDelayMs, ct);
        }

        throw new TimeoutException($"Transcode polling timed out for upload {uploadId} after {MaxTranscodePollAttempts} attempts");
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
