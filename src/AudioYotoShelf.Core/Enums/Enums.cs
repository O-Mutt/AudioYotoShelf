namespace AudioYotoShelf.Core.Enums;

public enum TransferStatus
{
    Pending,
    DownloadingAudio,
    UploadingToYoto,
    AwaitingTranscode,
    GeneratingIcons,
    CreatingCard,
    Completed,
    Failed,
    Cancelled
}

/// <summary>Where one track is in its journey to Yoto, as far as the server can say while a transfer runs.</summary>
public enum TrackPhase
{
    Downloading,
    Uploading,
    Transcoding,
    Uploaded,
    /// <summary>Yoto already held this audio from an earlier transfer, so nothing was sent.</summary>
    Reused
}

public enum YotoCategory
{
    Stories,
    Music,
    Radio,
    Podcast,
    Sfx,
    Activities,
    Alarms
}

public enum AgeRangeSource
{
    GenreInferred,
    DurationInferred,
    KeywordInferred,
    UserOverride,
    Default
}

public enum PlaybackType
{
    Linear,
    Interactive
}

public enum IconSource
{
    GeminiGenerated,
    YotoPublicLibrary,
    AudiobookshelfCover,
    UserUploaded
}

/// <summary>How a book's audio is grouped into Yoto tracks within a playlist card.</summary>
public enum TrackGrouping
{
    /// <summary>One track per chapter/audio file; oversized tracks are split to fit.</summary>
    Chapters,
    /// <summary>Merge the whole book into one track; split only if it exceeds per-track limits.</summary>
    SingleTrack,
    /// <summary>Merge into one track when the whole book fits one track; otherwise keep chapters.</summary>
    Auto
}

/// <summary>How a card set packs a series' books onto cards.</summary>
public enum PackingMode
{
    /// <summary>Fewest cards, then breaks chosen so the fullest card is as empty as possible.</summary>
    Balance,
    /// <summary>Fewest cards, each filled as far as it goes before the next starts.</summary>
    Fill
}

/// <summary>A card set's state, derived from its cards' playlist statuses.</summary>
public enum CardSetStatus
{
    /// <summary>Some or all cards are not on Yoto yet, and none is failing or being sent.</summary>
    Draft,
    /// <summary>At least one card is being sent.</summary>
    Sending,
    /// <summary>A card failed to send and needs a retry.</summary>
    NeedsAttention,
    /// <summary>Every card is on Yoto.</summary>
    Complete
}

/// <summary>Lifecycle of a multi-book playlist.</summary>
public enum PlaylistStatus
{
    Draft,
    Transferring,
    Transferred,
    Failed
}
