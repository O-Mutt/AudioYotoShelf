using System.Text.Json.Serialization;

namespace AudioYotoShelf.Core.DTOs.Audiobookshelf;

// --- Library ---

public record AbsLibrary(
    string Id,
    string Name,
    string MediaType,
    AbsLibrarySettings? Settings
);

public record AbsLibrarySettings(
    int CoverAspectRatio,
    bool DisableWatcher
);

public record AbsLibraryItemsResponse(
    AbsLibraryItem[] Results,
    int Total,
    int Limit,
    int Page
);

// --- Library Items ---

public record AbsLibraryItem(
    string Id,
    string Ino,
    string LibraryId,
    string MediaType,
    AbsBookMedia? Media,
    int NumFiles,
    long Size
);

public record AbsBookMedia(
    AbsBookMetadata Metadata,
    string? CoverPath,
    AbsAudioFile[] AudioFiles,
    AbsChapter[] Chapters,
    double Duration,
    int NumTracks,
    int NumAudioFiles,
    int NumChapters
);

public record AbsBookMetadata(
    string? Title,
    string? Subtitle,
    AbsAuthor[]? Authors,
    string[]? Narrators,
    AbsSeries[]? Series,
    string[]? Genres,
    string? PublishedYear,
    string? Publisher,
    string? Description,
    string? Isbn,
    string? Asin,
    string? Language,
    bool Explicit
);

public record AbsAuthor(string Id, string Name);
public record AbsSeries(string Id, string Name, string? Sequence);

public record AbsAudioFile(
    int Index,
    string Ino,
    AbsFileMetadata Metadata,
    double Duration,
    string Codec,
    int BitRate,
    string Format,
    long Size
);

public record AbsFileMetadata(
    string Filename,
    string Ext,
    string Path,
    long Size
);

public record AbsChapter(
    int Id,
    double Start,
    double End,
    string Title
);

// --- Series ---

public record AbsSeriesResponse(
    AbsSeriesItem[] Results,
    int Total,
    int Limit,
    int Page
);

public record AbsSeriesItem(
    string Id,
    string Name,
    string? Description,
    AbsSeriesBook[] Books,
    int TotalDuration
);

public record AbsSeriesBook(
    string Id,
    AbsBookMedia? Media,
    string? Sequence
);

// --- Auth ---

public record AbsLoginRequest(
    string Username,
    string Password
);

public record AbsLoginResponse(
    AbsUser User,
    string? UserDefaultLibraryId
);

public record AbsUser(
    string Id,
    string Username,
    string Type,
    string Token,
    bool IsActive,
    AbsPermissions? Permissions,
    string[]? LibrariesAccessible
);

public record AbsPermissions(
    bool Download,
    bool Update,
    bool Delete,
    bool Upload,
    bool AccessAllLibraries,
    bool AccessAllTags,
    bool AccessExplicitContent
);
