using AudioYotoShelf.Core.DTOs.Audiobookshelf;
using AudioYotoShelf.Core.DTOs.Transfer;
using AudioYotoShelf.Core.DTOs.Yoto;
using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AudioYotoShelf.Infrastructure.Services;

public class TransferOrchestrator(
    AudioYotoShelfDbContext db,
    IAudiobookshelfService absService,
    IYotoService yotoService,
    IIconGenerationService iconService,
    IAgeSuggestionService ageService,
    IChapterExtractor chapterExtractor,
    IConfiguration configuration,
    ILogger<TransferOrchestrator> logger) : ITransferOrchestrator
{
    private string TempDir => configuration.GetValue("Transfer:TempDirectory", "/app/temp")!;

    public async Task<TransferResponse> TransferBookAsync(
        Guid userConnectionId, CreateTransferRequest request, CancellationToken ct = default)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct)
            ?? throw new InvalidOperationException("User connection not found");

        EnsureValidConnections(user);

        logger.LogInformation("Starting transfer for item {ItemId}, user {UserId}",
            request.AbsLibraryItemId, userConnectionId);

        var item = await absService.GetLibraryItemAsync(
            user.AudiobookshelfUrl, user.AudiobookshelfToken!, request.AbsLibraryItemId, ct);

        var media = item.Media ?? throw new InvalidOperationException("Item has no media");
        var metadata = media.Metadata;

        var ageSuggestion = ageService.SuggestAgeRange(metadata, media.Duration, media.NumChapters);

        var transfer = new CardTransfer
        {
            UserConnectionId = userConnectionId,
            AbsLibraryItemId = request.AbsLibraryItemId,
            BookTitle = metadata.Title ?? "Unknown",
            BookAuthor = metadata.Authors?.FirstOrDefault()?.Name,
            SeriesName = metadata.Series?.FirstOrDefault()?.Name,
            SeriesSequence = ParseSequence(metadata.Series?.FirstOrDefault()?.Sequence),
            Category = request.Category,
            PlaybackType = request.PlaybackType,
            SuggestedMinAge = ageSuggestion.SuggestedMinAge,
            SuggestedMaxAge = ageSuggestion.SuggestedMaxAge,
            AgeSuggestionReason = ageSuggestion.Reason,
            AgeSuggestionSource = ageSuggestion.Source,
            OverrideMinAge = request.OverrideMinAge,
            OverrideMaxAge = request.OverrideMaxAge,
            Status = TransferStatus.Pending
        };

        db.CardTransfers.Add(transfer);
        await db.SaveChangesAsync(ct);

        try
        {
            await UpdateStatus(transfer, TransferStatus.DownloadingAudio, 5, ct);
            var trackMappings = await BuildTrackMappingsAsync(user, item, transfer, ct);

            await UpdateStatus(transfer, TransferStatus.UploadingToYoto, 20, ct);
            var yotoAccessToken = await EnsureYotoTokenAsync(user, ct);
            await UploadTracksAsync(yotoAccessToken, trackMappings, transfer, ct);

            await UpdateStatus(transfer, TransferStatus.GeneratingIcons, 70, ct);
            var chapterIcons = await GenerateIconsAsync(yotoAccessToken, user, media, trackMappings, ct);

            string? coverUrl = null;
            try
            {
                var coverStream = await absService.GetCoverImageAsync(
                    user.AudiobookshelfUrl, user.AudiobookshelfToken!,
                    request.AbsLibraryItemId, ct);
                coverUrl = await yotoService.UploadCoverImageAsync(yotoAccessToken, coverStream, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to upload cover art for {ItemId}", request.AbsLibraryItemId);
            }

            await UpdateStatus(transfer, TransferStatus.CreatingCard, 85, ct);
            var cardId = await CreateYotoCardAsync(
                yotoAccessToken, transfer, metadata, trackMappings, chapterIcons, coverUrl, ct);

            transfer.YotoCardId = cardId;
            transfer.Status = TransferStatus.Completed;
            transfer.ProgressPercent = 100;
            transfer.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Transfer completed: {TransferId} → Card {CardId}", transfer.Id, cardId);
            return MapToResponse(transfer);
        }
        catch (OperationCanceledException)
        {
            transfer.Status = TransferStatus.Cancelled;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Transfer failed: {TransferId}", transfer.Id);
            transfer.Status = TransferStatus.Failed;
            transfer.ErrorMessage = ex.Message.Length > 4000 ? ex.Message[..4000] : ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            CleanupTempFiles(transfer.Id);
        }
    }

    public async Task<TransferResponse[]> TransferSeriesAsync(
        Guid userConnectionId, CreateSeriesTransferRequest request, CancellationToken ct = default)
    {
        var user = await db.UserConnections.FindAsync([userConnectionId], ct)
            ?? throw new InvalidOperationException("User connection not found");

        EnsureValidConnections(user);

        var seriesDetail = await absService.GetSeriesDetailAsync(
            user.AudiobookshelfUrl, user.AudiobookshelfToken!, request.AbsSeriesId, ct);

        var results = new List<TransferResponse>();
        var orderedBooks = seriesDetail.Books
            .OrderBy(b => ParseSequence(b.Sequence) ?? 999f)
            .ToArray();

        logger.LogInformation("Transferring series '{SeriesName}' with {BookCount} books",
            seriesDetail.Name, orderedBooks.Length);

        foreach (var book in orderedBooks)
        {
            try
            {
                var bookRequest = new CreateTransferRequest(
                    AbsLibraryItemId: book.Id,
                    Category: request.Category,
                    OverrideMinAge: request.OverrideMinAge,
                    OverrideMaxAge: request.OverrideMaxAge
                );
                var result = await TransferBookAsync(userConnectionId, bookRequest, ct);
                results.Add(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to transfer book {BookId} in series {SeriesName}",
                    book.Id, seriesDetail.Name);
            }
        }

        return results.ToArray();
    }

    public async Task<TransferResponse> RetryTransferAsync(Guid transferId, CancellationToken ct = default)
    {
        var transfer = await db.CardTransfers
            .Include(t => t.TrackMappings)
            .FirstOrDefaultAsync(t => t.Id == transferId, ct)
            ?? throw new InvalidOperationException("Transfer not found");

        if (transfer.Status != TransferStatus.Failed)
            throw new InvalidOperationException("Can only retry failed transfers");

        transfer.Status = TransferStatus.Pending;
        transfer.ErrorMessage = null;
        transfer.ProgressPercent = 0;
        db.TrackMappings.RemoveRange(transfer.TrackMappings);
        await db.SaveChangesAsync(ct);

        var request = new CreateTransferRequest(
            AbsLibraryItemId: transfer.AbsLibraryItemId,
            Category: transfer.Category,
            PlaybackType: transfer.PlaybackType,
            OverrideMinAge: transfer.OverrideMinAge,
            OverrideMaxAge: transfer.OverrideMaxAge
        );

        return await TransferBookAsync(transfer.UserConnectionId, request, ct);
    }

    public async Task CancelTransferAsync(Guid transferId, CancellationToken ct = default)
    {
        var transfer = await db.CardTransfers.FindAsync([transferId], ct)
            ?? throw new InvalidOperationException("Transfer not found");

        transfer.Status = TransferStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        CleanupTempFiles(transferId);
    }

    public async Task<TransferResponse> GetTransferStatusAsync(Guid transferId, CancellationToken ct = default)
    {
        var transfer = await db.CardTransfers
            .Include(t => t.TrackMappings)
                .ThenInclude(tm => tm.GeneratedIcon)
            .FirstOrDefaultAsync(t => t.Id == transferId, ct)
            ?? throw new InvalidOperationException("Transfer not found");

        return MapToResponse(transfer);
    }

    public async Task<TransferResponse[]> GetUserTransfersAsync(
        Guid userConnectionId, int page = 0, int limit = 20, CancellationToken ct = default)
    {
        var transfers = await db.CardTransfers
            .Where(t => t.UserConnectionId == userConnectionId)
            .Include(t => t.TrackMappings)
                .ThenInclude(tm => tm.GeneratedIcon)
            .OrderByDescending(t => t.CreatedAt)
            .Skip(page * limit)
            .Take(limit)
            .ToArrayAsync(ct);

        return transfers.Select(MapToResponse).ToArray();
    }

    // =========================================================================
    // Private pipeline methods
    // =========================================================================

    internal async Task<List<TrackMapping>> BuildTrackMappingsAsync(
        UserConnection user, AbsLibraryItem item, CardTransfer transfer, CancellationToken ct)
    {
        var media = item.Media!;
        var mappings = new List<TrackMapping>();

        if (media.NumAudioFiles > 1)
        {
            // Multi-file book: each audio file = one track
            foreach (var audioFile in media.AudioFiles.OrderBy(f => f.Index))
            {
                var chapterTitle = media.Chapters.Length > audioFile.Index
                    ? media.Chapters[audioFile.Index].Title
                    : audioFile.Metadata.Filename;

                var mapping = new TrackMapping
                {
                    CardTransferId = transfer.Id,
                    AbsFileIno = audioFile.Ino,
                    ChapterTitle = chapterTitle,
                    ChapterIndex = audioFile.Index,
                    StartTime = 0,
                    EndTime = audioFile.Duration,
                    FileSizeBytes = audioFile.Size
                };

                db.TrackMappings.Add(mapping);
                mappings.Add(mapping);
            }
        }
        else if (media.Chapters.Length > 0 && media.AudioFiles.Length == 1)
        {
            // Single-file book: extract chapters via FFmpeg
            var singleFile = media.AudioFiles[0];

            // Download the single file to temp
            var tempInputPath = Path.Combine(TempDir, $"{transfer.Id}_input{Path.GetExtension(singleFile.Metadata.Filename)}");
            await DownloadToFileAsync(user, item.Id, singleFile.Ino, tempInputPath, ct);

            for (int i = 0; i < media.Chapters.Length; i++)
            {
                var chapter = media.Chapters[i];
                var chapterPath = await chapterExtractor.ExtractChapterAsync(
                    tempInputPath, chapter.Start, chapter.End, "m4a", ct);

                var mapping = new TrackMapping
                {
                    CardTransferId = transfer.Id,
                    AbsFileIno = $"{singleFile.Ino}:ch{i}",
                    ChapterTitle = chapter.Title,
                    ChapterIndex = i,
                    StartTime = chapter.Start,
                    EndTime = chapter.End,
                    FileSizeBytes = new FileInfo(chapterPath).Length
                };

                db.TrackMappings.Add(mapping);
                mappings.Add(mapping);
            }
        }
        else
        {
            // Single file, no chapters: treat as single track
            var singleFile = media.AudioFiles[0];
            var mapping = new TrackMapping
            {
                CardTransferId = transfer.Id,
                AbsFileIno = singleFile.Ino,
                ChapterTitle = media.Metadata.Title ?? "Track 1",
                ChapterIndex = 0,
                StartTime = 0,
                EndTime = singleFile.Duration,
                FileSizeBytes = singleFile.Size
            };
            db.TrackMappings.Add(mapping);
            mappings.Add(mapping);
        }

        await db.SaveChangesAsync(ct);
        return mappings;
    }

    internal async Task UploadTracksAsync(
        string yotoAccessToken, List<TrackMapping> mappings, CardTransfer transfer, CancellationToken ct)
    {
        var user = await db.UserConnections.FindAsync([transfer.UserConnectionId], ct)!;

        for (int i = 0; i < mappings.Count; i++)
        {
            var mapping = mappings[i];

            // Check for existing SHA256 deduplication
            var existingSha = await db.TrackMappings
                .Where(tm => tm.AbsFileIno == mapping.AbsFileIno &&
                             tm.YotoTranscodedSha256 != null &&
                             tm.Id != mapping.Id)
                .Select(tm => tm.YotoTranscodedSha256)
                .FirstOrDefaultAsync(ct);

            if (existingSha is not null)
            {
                logger.LogInformation("Reusing existing SHA256 for track {FileIno}", mapping.AbsFileIno);
                mapping.YotoTranscodedSha256 = existingSha;
                mapping.YotoTrackUrl = $"yoto:#{existingSha}";
                continue;
            }

            // Determine source: extracted temp file or direct download
            var tempChapterPath = FindExtractedChapterPath(transfer.Id, mapping.ChapterIndex);
            Stream audioStream;
            long contentLength;
            string contentType;

            if (tempChapterPath is not null && File.Exists(tempChapterPath))
            {
                audioStream = File.OpenRead(tempChapterPath);
                contentLength = new FileInfo(tempChapterPath).Length;
                contentType = "audio/mp4";
            }
            else
            {
                // Direct download from ABS — need to buffer to temp file for content-length
                var tempPath = Path.Combine(TempDir, $"{transfer.Id}_track{i}.tmp");
                await DownloadToFileAsync(user!, transfer.AbsLibraryItemId, mapping.AbsFileIno, tempPath, ct);
                audioStream = File.OpenRead(tempPath);
                contentLength = new FileInfo(tempPath).Length;
                contentType = "audio/mpeg";
            }

            try
            {
                var sha256 = await yotoService.UploadAndTranscodeAsync(
                    yotoAccessToken, audioStream, contentLength, contentType,
                    new Progress<int>(p =>
                    {
                        var overallProgress = 20 + (int)((i + p / 100.0) / mappings.Count * 50);
                        transfer.ProgressPercent = Math.Min(overallProgress, 70);
                    }),
                    ct);

                mapping.YotoTranscodedSha256 = sha256;
                mapping.YotoTrackUrl = $"yoto:#{sha256}";
                await db.SaveChangesAsync(ct);
            }
            finally
            {
                await audioStream.DisposeAsync();
            }
        }
    }

    internal async Task<Dictionary<int, string>> GenerateIconsAsync(
        string yotoAccessToken, UserConnection user,
        AbsBookMedia media, List<TrackMapping> mappings, CancellationToken ct)
    {
        var chapterIcons = new Dictionary<int, string>();
        var primaryGenre = media.Metadata.Genres?.FirstOrDefault();
        var bookTitle = media.Metadata.Title ?? "Unknown";

        for (int i = 0; i < mappings.Count; i++)
        {
            var mapping = mappings[i];

            try
            {
                var iconBytes = await iconService.GenerateChapterIconAsync(
                    mapping.ChapterTitle, bookTitle, primaryGenre, ct);

                var iconUpload = await yotoService.UploadCustomIconAsync(
                    yotoAccessToken, iconBytes, $"ch{i}_{mapping.ChapterTitle[..Math.Min(20, mapping.ChapterTitle.Length)]}.png", ct);

                var icon = new GeneratedIcon
                {
                    UserConnectionId = user.Id,
                    Prompt = iconService.BuildChapterIconPrompt(mapping.ChapterTitle, bookTitle, primaryGenre),
                    ContextTitle = $"{bookTitle} - {mapping.ChapterTitle}",
                    Source = IconSource.GeminiGenerated,
                    YotoMediaId = iconUpload.MediaId,
                    YotoIconUrl = iconUpload.Url,
                    TimesUsed = 1
                };

                db.GeneratedIcons.Add(icon);
                mapping.GeneratedIconId = icon.Id;
                chapterIcons[i] = iconUpload.Url;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Icon generation failed for chapter {Index}: {Title}",
                    i, mapping.ChapterTitle);
                // Fallback: use a generic icon URL — the card can still be created
                chapterIcons[i] = "https://yotoicons.com/api/icon/book";
            }
        }

        await db.SaveChangesAsync(ct);
        return chapterIcons;
    }

    internal async Task<string> CreateYotoCardAsync(
        string yotoAccessToken, CardTransfer transfer, AbsBookMetadata metadata,
        List<TrackMapping> mappings, Dictionary<int, string> chapterIcons, string? coverUrl,
        CancellationToken ct)
    {
        var chapters = new List<YotoChapter>();

        for (int i = 0; i < mappings.Count; i++)
        {
            var mapping = mappings[i];
            var iconUrl = chapterIcons.GetValueOrDefault(i, "https://yotoicons.com/api/icon/book");

            chapters.Add(new YotoChapter(
                Key: (i + 1).ToString("D2"),
                Title: mapping.ChapterTitle,
                Tracks:
                [
                    new YotoTrack(
                        Key: $"{(i + 1):D2}01",
                        Title: mapping.ChapterTitle,
                        TrackUrl: mapping.YotoTrackUrl ?? "",
                        Format: "aac",
                        Type: "audio",
                        Duration: mapping.TranscodedDuration ?? (mapping.EndTime - mapping.StartTime),
                        FileSize: mapping.TranscodedFileSize ?? mapping.FileSizeBytes,
                        Channels: "stereo",
                        Display: new YotoDisplay(iconUrl)
                    )
                ],
                Display: new YotoDisplay(iconUrl)
            ));
        }

        var content = new YotoCardContent(
            Chapters: chapters.ToArray(),
            Config: new YotoCardConfig(AllowSkip: true, AllowFastForward: true, AllowRewind: true),
            PlaybackType: transfer.PlaybackType == PlaybackType.Linear ? "linear" : "interactive",
            Version: 1
        );

        var cardMetadata = new YotoCardMetadata(
            Author: metadata.Authors?.FirstOrDefault()?.Name,
            Category: transfer.Category.ToString().ToLowerInvariant(),
            Description: metadata.Description?.Length > 500
                ? metadata.Description[..500]
                : metadata.Description,
            Genre: metadata.Genres,
            Languages: metadata.Language is not null ? [metadata.Language] : null,
            MinAge: transfer.EffectiveMinAge,
            MaxAge: transfer.EffectiveMaxAge,
            ReadBy: metadata.Narrators?.FirstOrDefault(),
            Cover: coverUrl is not null ? new YotoCover(coverUrl) : null
        );

        // Check if updating existing card
        var existingCardId = transfer.YotoCardId;
        if (existingCardId is null)
        {
            var existingTransfer = await db.CardTransfers
                .Where(t => t.AbsLibraryItemId == transfer.AbsLibraryItemId &&
                            t.YotoCardId != null &&
                            t.Id != transfer.Id)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync(ct);

            existingCardId = existingTransfer?.YotoCardId;
        }

        return await yotoService.CreateOrUpdateCardAsync(
            yotoAccessToken, content, cardMetadata, existingCardId, ct);
    }

    // =========================================================================
    // Helper methods
    // =========================================================================

    private static void EnsureValidConnections(UserConnection user)
    {
        if (!user.HasValidAbsConnection)
            throw new InvalidOperationException("No valid Audiobookshelf connection");
        if (!user.HasValidYotoConnection)
            throw new InvalidOperationException("No valid Yoto connection");
    }

    private async Task<string> EnsureYotoTokenAsync(UserConnection user, CancellationToken ct)
    {
        if (user.YotoTokenExpiresAt.HasValue &&
            user.YotoTokenExpiresAt.Value < DateTimeOffset.UtcNow.AddMinutes(5))
        {
            logger.LogInformation("Refreshing Yoto token for user {Username}", user.Username);
            var newToken = await yotoService.RefreshTokenAsync(user.YotoRefreshToken!, ct);
            user.YotoAccessToken = newToken.AccessToken;
            user.YotoRefreshToken = newToken.RefreshToken ?? user.YotoRefreshToken;
            user.YotoTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(newToken.ExpiresIn);
            await db.SaveChangesAsync(ct);
        }

        return user.YotoAccessToken!;
    }

    private async Task DownloadToFileAsync(
        UserConnection user, string itemId, string fileIno, string outputPath, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        await using var sourceStream = await absService.DownloadAudioFileAsync(
            user.AudiobookshelfUrl, user.AudiobookshelfToken!, itemId, fileIno, ct);
        await using var fileStream = File.Create(outputPath);
        await sourceStream.CopyToAsync(fileStream, ct);

        logger.LogInformation("Downloaded {FileIno} to {Path} ({Size} bytes)",
            fileIno, outputPath, new FileInfo(outputPath).Length);
    }

    private string? FindExtractedChapterPath(Guid transferId, int chapterIndex)
    {
        var pattern = $"chapter_{transferId}*ch{chapterIndex}*";
        var tempFiles = Directory.Exists(TempDir)
            ? Directory.GetFiles(TempDir, $"*{transferId}*")
            : [];

        // Convention from FFmpeg extractor: chapter_{guid}.m4a
        var chapterFile = Path.Combine(TempDir, $"{transferId}_ch{chapterIndex}.m4a");
        return File.Exists(chapterFile) ? chapterFile : null;
    }

    private async Task UpdateStatus(
        CardTransfer transfer, TransferStatus status, int progress, CancellationToken ct)
    {
        transfer.Status = status;
        transfer.ProgressPercent = progress;
        await db.SaveChangesAsync(ct);
    }

    private void CleanupTempFiles(Guid transferId)
    {
        try
        {
            if (!Directory.Exists(TempDir)) return;

            var files = Directory.GetFiles(TempDir, $"{transferId}*");
            foreach (var file in files)
            {
                File.Delete(file);
                logger.LogDebug("Cleaned up temp file: {File}", file);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clean up temp files for transfer {TransferId}", transferId);
        }
    }

    internal static float? ParseSequence(string? sequence)
    {
        if (string.IsNullOrEmpty(sequence)) return null;
        return float.TryParse(sequence, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static TransferResponse MapToResponse(CardTransfer t) => new(
        t.Id,
        t.BookTitle,
        t.BookAuthor,
        t.SeriesName,
        t.SeriesSequence,
        t.Status,
        t.ProgressPercent,
        t.ErrorMessage,
        new AgeRangeResponse(
            t.SuggestedMinAge, t.SuggestedMaxAge,
            t.AgeSuggestionReason, t.AgeSuggestionSource,
            t.OverrideMinAge, t.OverrideMaxAge,
            t.EffectiveMinAge, t.EffectiveMaxAge),
        t.YotoCardId,
        t.CreatedAt,
        t.CompletedAt,
        t.TrackMappings.Select(tm => new TrackMappingResponse(
            tm.Id, tm.ChapterTitle, tm.ChapterIndex,
            tm.EndTime - tm.StartTime, tm.IsUploaded,
            tm.GeneratedIcon?.YotoIconUrl)).ToArray()
    );
}
