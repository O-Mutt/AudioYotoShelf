using AudioYotoShelf.Core.DTOs.Audiobookshelf;
using AudioYotoShelf.Core.DTOs.Transfer;
using AudioYotoShelf.Core.DTOs.Yoto;
using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Enums;

namespace AudioYotoShelf.Core.Tests.Helpers;

/// <summary>
/// Fluent test data builders for all domain entities and DTOs.
/// Every builder sets sensible defaults so tests only specify what matters.
/// </summary>
public static class TestData
{
    // =========================================================================
    // Entities
    // =========================================================================

    public static UserConnection CreateUserConnection(
        string username = "testuser",
        string absUrl = "http://abs.local",
        string absToken = "abs-test-token",
        string? yotoAccessToken = "yoto-access-token",
        string? yotoRefreshToken = "yoto-refresh-token",
        DateTimeOffset? yotoTokenExpiry = null)
    {
        return new UserConnection
        {
            Id = Guid.NewGuid(),
            Username = username,
            AudiobookshelfUrl = absUrl,
            AudiobookshelfToken = absToken,
            AudiobookshelfTokenValidatedAt = DateTimeOffset.UtcNow,
            YotoAccessToken = yotoAccessToken,
            YotoRefreshToken = yotoRefreshToken,
            YotoTokenExpiresAt = yotoTokenExpiry ?? DateTimeOffset.UtcNow.AddHours(1),
            DefaultMinAge = 5,
            DefaultMaxAge = 10
        };
    }

    public static CardTransfer CreateCardTransfer(
        Guid? userConnectionId = null,
        string itemId = "item-123",
        string title = "Test Book",
        TransferStatus status = TransferStatus.Pending)
    {
        return new CardTransfer
        {
            Id = Guid.NewGuid(),
            UserConnectionId = userConnectionId ?? Guid.NewGuid(),
            AbsLibraryItemId = itemId,
            BookTitle = title,
            BookAuthor = "Test Author",
            AgeSuggestionReason = "Default",
            AgeSuggestionSource = AgeRangeSource.Default,
            SuggestedMinAge = 5,
            SuggestedMaxAge = 10,
            Status = status
        };
    }

    public static TrackMapping CreateTrackMapping(
        Guid? cardTransferId = null,
        string fileIno = "ino-1",
        string chapterTitle = "Chapter 1",
        int index = 0,
        string? sha256 = null)
    {
        return new TrackMapping
        {
            Id = Guid.NewGuid(),
            CardTransferId = cardTransferId ?? Guid.NewGuid(),
            AbsFileIno = fileIno,
            ChapterTitle = chapterTitle,
            ChapterIndex = index,
            StartTime = 0,
            EndTime = 300,
            FileSizeBytes = 5_000_000,
            YotoTranscodedSha256 = sha256,
            YotoTrackUrl = sha256 is not null ? $"yoto:#{sha256}" : null
        };
    }

    // =========================================================================
    // ABS DTOs
    // =========================================================================

    public static AbsBookMetadata CreateAbsMetadata(
        string title = "Test Book",
        string[]? genres = null,
        string? description = null,
        bool isExplicit = false,
        string? seriesName = null,
        string? seriesSequence = null)
    {
        return new AbsBookMetadata(
            Title: title,
            Subtitle: null,
            Authors: [new AbsAuthor("a1", "Test Author")],
            Narrators: [new AbsNarrator("n1", "Test Narrator")],
            Series: seriesName is not null
                ? [new AbsSeries("s1", seriesName, seriesSequence)]
                : null,
            Genres: genres ?? ["Children's Fiction"],
            PublishedYear: "2024",
            Publisher: "Test Publisher",
            Description: description ?? "A test book description.",
            Isbn: null,
            Asin: null,
            Language: "en",
            Explicit: isExplicit
        );
    }

    public static AbsAudioFile CreateAbsAudioFile(
        int index = 0,
        string ino = "ino-1",
        double duration = 300,
        long size = 5_000_000)
    {
        return new AbsAudioFile(
            Index: index,
            Ino: ino,
            Metadata: new AbsFileMetadata($"chapter{index}.mp3", ".mp3", $"/path/chapter{index}.mp3", size),
            Duration: duration,
            Codec: "mp3",
            BitRate: 128000,
            Format: "mp3",
            Size: size
        );
    }

    public static AbsChapter CreateAbsChapter(
        int id = 0,
        string title = "Chapter 1",
        double start = 0,
        double end = 300)
    {
        return new AbsChapter(id, start, end, title);
    }

    public static AbsBookMedia CreateAbsMedia(
        AbsBookMetadata? metadata = null,
        AbsAudioFile[]? audioFiles = null,
        AbsChapter[]? chapters = null,
        double duration = 3600,
        int numChapters = 10)
    {
        return new AbsBookMedia(
            Metadata: metadata ?? CreateAbsMetadata(),
            CoverPath: "/covers/test.jpg",
            AudioFiles: audioFiles ?? [CreateAbsAudioFile()],
            Chapters: chapters ?? [CreateAbsChapter()],
            Duration: duration,
            NumTracks: audioFiles?.Length ?? 1,
            NumAudioFiles: audioFiles?.Length ?? 1,
            NumChapters: numChapters
        );
    }

    public static AbsLibraryItem CreateAbsLibraryItem(
        string id = "item-123",
        AbsBookMedia? media = null)
    {
        return new AbsLibraryItem(
            Id: id,
            Ino: "ino-item",
            LibraryId: "lib-1",
            MediaType: "book",
            Media: media ?? CreateAbsMedia(),
            NumFiles: 1,
            Size: 50_000_000
        );
    }

    public static AbsSeriesItem CreateAbsSeriesItem(
        string name = "Test Series",
        int bookCount = 3)
    {
        var books = Enumerable.Range(1, bookCount)
            .Select(i => new AbsSeriesBook($"book-{i}", CreateAbsMedia(
                CreateAbsMetadata($"Book {i}", seriesName: name, seriesSequence: i.ToString())),
                i.ToString()))
            .ToArray();

        return new AbsSeriesItem("series-1", name, "A test series", books, 36000);
    }

    // =========================================================================
    // Yoto DTOs
    // =========================================================================

    public static YotoUploadInfo CreateYotoUploadInfo() =>
        new("https://upload.example.com/signed-url", "upload-123");

    public static YotoTranscodeResponse CreateYotoTranscodeComplete(
        string sha256 = "abc123sha256") =>
        new(sha256, new YotoTranscodedInfo(300, 5_000_000, "stereo", "aac"), "complete");

    public static YotoIconUploadResponse CreateYotoIconUpload(
        string mediaId = "icon-media-1",
        string url = "https://icons.yotoplay.com/test.png") =>
        new(mediaId, url);

    // =========================================================================
    // Transfer DTOs
    // =========================================================================

    public static CreateTransferRequest CreateTransferRequest(
        string itemId = "item-123") =>
        new(AbsLibraryItemId: itemId);

    public static CreateSeriesTransferRequest CreateSeriesTransferRequest(
        string seriesId = "series-1",
        string libraryId = "lib-1") =>
        new(AbsSeriesId: seriesId, AbsLibraryId: libraryId);
}
