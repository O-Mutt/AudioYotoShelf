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
