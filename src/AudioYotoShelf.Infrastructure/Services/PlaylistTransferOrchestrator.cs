using AudioYotoShelf.Core.DTOs.Audiobookshelf;
using AudioYotoShelf.Core.DTOs.Playlist;
using AudioYotoShelf.Core.DTOs.Yoto;
using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Core.Services;
using AudioYotoShelf.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AudioYotoShelf.Infrastructure.Services;

/// <summary>
/// Builds a single Yoto MYO card from an entire playlist: one chapter per book, applying each
/// book's grouping policy (merging via ffmpeg concat, or per-chapter) and splitting any track
/// that exceeds the per-track limit. Refuses to start if the playlist exceeds card capacity.
/// </summary>
public class PlaylistTransferOrchestrator(
    AudioYotoShelfDbContext db,
    IAudiobookshelfService absService,
    IYotoService yotoService,
    IIconGenerationService iconService,
    IChapterExtractor chapterExtractor,
    ITrackPlanner planner,
    ICardCapacityCalculator calculator,
    IConfiguration configuration,
    ILogger<PlaylistTransferOrchestrator> logger) : IPlaylistTransferOrchestrator
{
    private string TempDir => configuration.GetValue("Transfer:TempDirectory", "/app/temp")!;

    private sealed record LocalTrack(string Path, double DurationSeconds, string Title);

    public async Task TransferPlaylistAsync(Guid playlistId, CancellationToken ct = default)
    {
        var playlist = await db.Playlists
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == playlistId, ct)
            ?? throw new InvalidOperationException("Playlist not found");

        var user = await db.UserConnections.FindAsync([playlist.UserConnectionId], ct)
            ?? throw new InvalidOperationException("User connection not found");
        EnsureValidConnections(user);

        // Refresh the stored ABS access token before downloading any book audio for this card.
        await AbsTokens.EnsureValidAsync(db, absService, user, logger, ct);

        // Pre-flight capacity gate — refuse to build a card that can't fit.
        var capacity = calculator.Calculate(PlaylistCapacity.BuildPlans(playlist, planner));
        if (!capacity.WithinLimits)
        {
            playlist.Status = PlaylistStatus.Failed;
            playlist.ErrorMessage = string.Join(" ", capacity.Messages);
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException($"Playlist exceeds card capacity: {playlist.ErrorMessage}");
        }

        if (playlist.Items.Count == 0)
            throw new InvalidOperationException("Playlist has no books to transfer");

        playlist.Status = PlaylistStatus.Transferring;
        playlist.ErrorMessage = null;
        await db.SaveChangesAsync(ct);

        try
        {
            var yotoToken = await YotoTokens.EnsureValidAsync(db, yotoService, user, logger, ct);

            var chapters = new List<YotoChapter>();
            var orderedItems = playlist.Items.OrderBy(i => i.Position).ToList();
            string? firstAuthor = null;

            for (var bookIndex = 0; bookIndex < orderedItems.Count; bookIndex++)
            {
                var item = orderedItems[bookIndex];
                var libraryItem = await absService.GetLibraryItemAsync(
                    user.AudiobookshelfUrl, user.AudiobookshelfToken!, item.AbsLibraryItemId, ct);
                var media = libraryItem.Media
                    ?? throw new InvalidOperationException($"Book {item.AbsLibraryItemId} has no media");

                firstAuthor ??= media.Metadata.Authors?.FirstOrDefault()?.Name;

                var grouping = item.GroupingOverride ?? playlist.DefaultGrouping;

                // Cards are sized by Yoto's stored audio, not the source, so one book's downloads can
                // run to several GB. Dropping them once the book is uploaded keeps peak disk use to
                // the largest single book instead of the whole playlist.
                var bookTemp = new List<string>();
                YotoChapter chapter;
                try
                {
                    chapter = await BuildBookChapterAsync(
                        user, libraryItem, media, item, grouping, yotoToken, bookIndex + 1, bookTemp, ct);
                }
                finally
                {
                    CleanupTempFiles(bookTemp);
                }
                chapters.Add(chapter);

                logger.LogInformation("Playlist {PlaylistId}: book {Index}/{Total} '{Title}' -> {Tracks} tracks",
                    playlistId, bookIndex + 1, orderedItems.Count, item.BookTitle, chapter.Tracks.Length);
            }

            var content = new YotoCardContent(
                Chapters: chapters.ToArray(),
                Config: new YotoCardConfig(AllowSkip: true, AllowFastForward: true, AllowRewind: true),
                PlaybackType: "linear",
                Version: "1");

            // For a single-book playlist, use that book's Audiobookshelf cover as the card cover.
            var coverUrl = orderedItems.Count == 1
                ? await TryUploadBookCoverAsync(user, orderedItems[0].AbsLibraryItemId, yotoToken, ct)
                : null;

            var metadata = new YotoCardMetadata(
                Author: firstAuthor,
                Category: YotoCategory.Stories.ToString().ToLowerInvariant(),
                Description: playlist.Name,
                Genre: null, Languages: null, MinAge: null, MaxAge: null,
                ReadBy: null,
                Cover: coverUrl is not null ? new YotoCover(coverUrl) : null);

            var cardId = await yotoService.CreateOrUpdateCardAsync(
                yotoToken, content, metadata, title: playlist.Name, existingCardId: playlist.YotoCardId, ct: ct);

            playlist.YotoCardId = cardId;
            playlist.Status = PlaylistStatus.Transferred;
            playlist.TransferredAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Playlist {PlaylistId} transferred to card {CardId} ({Chapters} books)",
                playlistId, cardId, chapters.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Playlist transfer failed: {PlaylistId}", playlistId);
            playlist.Status = PlaylistStatus.Failed;
            playlist.ErrorMessage = ex.Message.Length > 4000 ? ex.Message[..4000] : ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<YotoChapter> BuildBookChapterAsync(
        UserConnection user, AbsLibraryItem libraryItem, AbsBookMedia media, PlaylistItem item,
        TrackGrouping grouping, string yotoToken, int chapterIndex, List<string> temp, CancellationToken ct)
    {
        var bookTitle = item.BookTitle;
        var baseTracks = item.IsPart
            ? await ProducePartTracksAsync(user, libraryItem, media, item, grouping, temp, ct)
            : await ProduceBaseTracksAsync(user, libraryItem, media, bookTitle, grouping, temp, ct);
        var iconRef = await GenerateBookIconAsync(yotoToken, bookTitle, media.Metadata.Genres?.FirstOrDefault(), ct);
        var display = iconRef is not null ? new YotoDisplay(iconRef) : null;

        var yotoTracks = new List<YotoTrack>();
        var trackNumber = 1;
        foreach (var baseTrack in baseTracks)
        {
            foreach (var piece in await SplitIfNeededAsync(baseTrack, temp, ct))
            {
                var length = new FileInfo(piece.Path).Length;
                await using var stream = File.OpenRead(piece.Path);
                var result = await yotoService.UploadAndTranscodeAsync(
                    yotoToken, stream, length, ContentTypeFor(piece.Path), null, ct);

                yotoTracks.Add(new YotoTrack(
                    Key: $"{chapterIndex:D2}{trackNumber:D2}",
                    Title: piece.Title,
                    TrackUrl: $"yoto:#{result.Sha256}",
                    // What Yoto actually transcoded to; a declared Format that doesn't match plays for
                    // a couple of seconds then fails on the device. "aac" is the fallback for the rare
                    // case Yoto's response carries no transcodedInfo at all.
                    Format: result.Format ?? "aac",
                    Type: "audio",
                    Duration: result.Duration ?? piece.DurationSeconds,
                    FileSize: result.FileSize ?? length,
                    Channels: "stereo",
                    Display: display));
                trackNumber++;
            }
        }

        return new YotoChapter(
            Key: $"{chapterIndex:D2}",
            Title: item.IsPart ? $"{bookTitle} · Part {item.PartNumber} of {item.PartCount}" : bookTitle,
            Tracks: yotoTracks.ToArray(),
            Display: display);
    }

    private async Task<List<LocalTrack>> ProduceBaseTracksAsync(
        UserConnection user, AbsLibraryItem libraryItem, AbsBookMedia media, string bookTitle,
        TrackGrouping grouping, List<string> temp, CancellationToken ct)
    {
        var mergeWholeBook = grouping == TrackGrouping.SingleTrack
            || (grouping == TrackGrouping.Auto && planner.Plan(media, TrackGrouping.Auto).Count == 1);

        var files = media.AudioFiles.OrderBy(f => f.Index).ToList();
        if (files.Count == 0) return [];

        if (mergeWholeBook)
        {
            string mergedPath;
            if (files.Count == 1)
            {
                mergedPath = await DownloadToTempAsync(user, libraryItem.Id, files[0], temp, ct);
            }
            else
            {
                var parts = new List<string>();
                foreach (var f in files)
                    parts.Add(await DownloadToTempAsync(user, libraryItem.Id, f, temp, ct));
                mergedPath = await chapterExtractor.ConcatenateAsync(parts, "m4a", ct);
                temp.Add(mergedPath);
                DeleteConsumed(parts, mergedPath);
            }
            return [new LocalTrack(mergedPath, files.Sum(f => f.Duration), bookTitle)];
        }

        // Per-chapter grouping
        if (media.NumAudioFiles > 1)
        {
            var result = new List<LocalTrack>();
            foreach (var f in files)
            {
                var path = await DownloadToTempAsync(user, libraryItem.Id, f, temp, ct);
                var title = ChapterTitleResolver.ForFile(media.Chapters, f);
                result.Add(new LocalTrack(path, f.Duration, title));
            }
            return result;
        }

        if (media.Chapters.Length > 0 && files.Count == 1)
        {
            var inputPath = await DownloadToTempAsync(user, libraryItem.Id, files[0], temp, ct);
            var result = new List<LocalTrack>();
            foreach (var chapter in media.Chapters)
            {
                var chapterPath = await chapterExtractor.ExtractChapterAsync(
                    inputPath, chapter.Start, chapter.End, "m4a", ct);
                temp.Add(chapterPath);
                result.Add(new LocalTrack(chapterPath, chapter.End - chapter.Start, chapter.Title));
            }
            DeleteConsumed([inputPath], result.Select(t => t.Path));
            return result;
        }

        var single = files[0];
        var singlePath = await DownloadToTempAsync(user, libraryItem.Id, single, temp, ct);
        return [new LocalTrack(singlePath, single.Duration, bookTitle)];
    }

    /// <summary>
    /// Produces the tracks for one part of a book split across cards: only the chapters in the item's
    /// range are downloaded or extracted. The range indexes the chapter list cached when the card set
    /// was planned (files, embedded chapters, or equal time segments for a book without chapters), so
    /// a book whose chapters changed since then is refused rather than sent with the wrong range.
    /// </summary>
    private async Task<List<LocalTrack>> ProducePartTracksAsync(
        UserConnection user, AbsLibraryItem libraryItem, AbsBookMedia media, PlaylistItem item,
        TrackGrouping grouping, List<string> temp, CancellationToken ct)
    {
        var first = item.FirstChapterIndex!.Value;
        var last = item.LastChapterIndex!.Value;
        var planned = item.TrackDurations.Count;
        var files = media.AudioFiles.OrderBy(f => f.Index).ToList();
        // The planner cuts a book with a single entry in its chapter list into equal time segments, and a
        // single file with one embedded chapter has a single entry just as one with none does.
        var timeSegments = files.Count == 1 && media.Chapters.Length <= 1;
        var current = files.Count > 1 ? files.Count : timeSegments ? planned : media.Chapters.Length;

        if (files.Count == 0 || current != planned || first < 0 || last >= planned || first > last)
            throw new InvalidOperationException(
                $"'{item.BookTitle}' no longer has the chapters this card was planned with; re-plan the card set before sending it.");

        var tracks = new List<LocalTrack>();
        if (files.Count > 1)
        {
            foreach (var f in files.GetRange(first, last - first + 1))
            {
                var path = await DownloadToTempAsync(user, libraryItem.Id, f, temp, ct);
                tracks.Add(new LocalTrack(path, f.Duration, ChapterTitleResolver.ForFile(media.Chapters, f)));
            }
        }
        else
        {
            var inputPath = await DownloadToTempAsync(user, libraryItem.Id, files[0], temp, ct);
            var total = files[0].Duration > 0 ? files[0].Duration : media.Duration;
            for (var i = first; i <= last; i++)
            {
                var (start, end, title) = timeSegments
                    ? (total * i / planned, total * (i + 1) / planned, $"{item.BookTitle} ({i + 1})")
                    : (media.Chapters[i].Start, media.Chapters[i].End, media.Chapters[i].Title);
                var chapterPath = await chapterExtractor.ExtractChapterAsync(inputPath, start, end, "m4a", ct);
                temp.Add(chapterPath);
                tracks.Add(new LocalTrack(chapterPath, end - start, title));
            }
            DeleteConsumed([inputPath], tracks.Select(t => t.Path));
        }

        var merge = tracks.Count > 1 && planner.ApplyGrouping(
            PlaylistCapacity.ChapterTracks(item), grouping, item.BookTitle).Count == 1;
        if (!merge) return tracks;

        var mergedPath = await chapterExtractor.ConcatenateAsync(tracks.Select(t => t.Path).ToList(), "m4a", ct);
        temp.Add(mergedPath);
        DeleteConsumed(tracks.Select(t => t.Path), mergedPath);
        return [new LocalTrack(mergedPath, tracks.Sum(t => t.DurationSeconds), item.BookTitle)];
    }

    private async Task<List<LocalTrack>> SplitIfNeededAsync(LocalTrack track, List<string> temp, CancellationToken ct)
    {
        var sizeBytes = new FileInfo(track.Path).Length;
        var parts = calculator.ProjectedTrackCount(new SourceTrack(track.Title, track.DurationSeconds, sizeBytes));
        if (parts <= 1) return [track];

        var segments = await chapterExtractor.SplitAsync(track.Path, track.DurationSeconds / parts, "m4a", ct);
        foreach (var segment in segments) temp.Add(segment);
        DeleteConsumed([track.Path], segments);

        var segmentDuration = track.DurationSeconds / segments.Count;
        return segments
            .Select((path, i) => new LocalTrack(path, segmentDuration, $"{track.Title} (Part {i + 1})"))
            .ToList();
    }

    /// <summary>Uploads a book's Audiobookshelf cover to Yoto, returning the cover URL (best-effort, null on failure).</summary>
    private async Task<string?> TryUploadBookCoverAsync(UserConnection user, string itemId, string yotoToken, CancellationToken ct)
    {
        try
        {
            var coverStream = await absService.GetCoverImageAsync(
                user.AudiobookshelfUrl, user.AudiobookshelfToken!, itemId, ct);
            return await yotoService.UploadCoverImageAsync(yotoToken, coverStream, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to upload cover for book {ItemId}", itemId);
            return null;
        }
    }

    /// <summary>Returns a yoto:#{mediaId} display reference for the book, or null if generation fails.</summary>
    private async Task<string?> GenerateBookIconAsync(string yotoToken, string bookTitle, string? genre, CancellationToken ct)
    {
        try
        {
            var iconBytes = await iconService.GenerateChapterIconAsync(bookTitle, bookTitle, genre, ct);
            var safeName = bookTitle[..Math.Min(20, bookTitle.Length)];
            var upload = await yotoService.UploadCustomIconAsync(yotoToken, iconBytes, $"{safeName}.png", ct);
            return $"yoto:#{upload.MediaId}";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Icon generation failed for book '{Title}'", bookTitle);
            return null;
        }
    }

    private async Task<string> DownloadToTempAsync(
        UserConnection user, string itemId, AbsAudioFile file, List<string> temp, CancellationToken ct)
    {
        Directory.CreateDirectory(TempDir);
        var ext = Path.GetExtension(file.Metadata.Filename);
        if (string.IsNullOrEmpty(ext)) ext = ".mp3";
        var path = Path.Combine(TempDir, $"pl_{Guid.NewGuid():N}{ext}");
        // Registered before the copy so a download that fails or is cancelled partway, which can
        // already be several GB, is still deleted with the rest of the book's temp files.
        temp.Add(path);

        await using var source = await absService.DownloadAudioFileAsync(
            user.AudiobookshelfUrl, user.AudiobookshelfToken!, itemId, file.Ino, ct);
        await using (var fileStream = File.Create(path))
            await source.CopyToAsync(fileStream, ct);

        return path;
    }

    /// <summary>
    /// The declared type sent to Yoto's transcoder, which trusts it rather than sniffing the bytes.
    /// A merged or chapter-extracted file is always our own ffmpeg m4a output; a file that reached
    /// here unmerged still has the extension Audiobookshelf served it with (<see cref="DownloadToTempAsync"/>),
    /// so an ogg/opus or flac source must not fall into the "everything else is mp3" bucket — that
    /// mismatch is what made a real ogg/opus book play about a second per track before skipping on.
    /// </summary>
    internal static string ContentTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".m4a" or ".m4b" or ".mp4" or ".aac" => "audio/mp4",
            ".ogg" or ".opus" => "audio/ogg",
            ".flac" => "audio/flac",
            ".wav" => "audio/wav",
            _ => "audio/mpeg"
        };

    private static void EnsureValidConnections(UserConnection user)
    {
        if (!user.HasValidAbsConnection)
            throw new InvalidOperationException("No valid Audiobookshelf connection");
        if (!user.HasValidYotoConnection)
            throw new InvalidOperationException("No valid Yoto connection");
    }

    /// <summary>
    /// Deletes a step's inputs as soon as it has produced its outputs, so a book that is merged or cut
    /// from one file and then split holds about twice its source on disk rather than three times
    /// (#133). An input that is also an output is kept: ConcatenateAsync hands back a lone input
    /// unchanged. The inputs stay in the book's temp list, which remains the backstop on failure.
    /// </summary>
    private void DeleteConsumed(IEnumerable<string> inputs, params IEnumerable<string> outputs) =>
        CleanupTempFiles(inputs.Except(outputs, StringComparer.Ordinal));

    private void CleanupTempFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clean up temp file {Path}", path);
            }
        }
    }
}
