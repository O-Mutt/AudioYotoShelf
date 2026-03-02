namespace AudioYotoShelf.Core.Interfaces;

/// <summary>
/// Extracts individual chapter audio segments from a single audiobook file
/// using FFmpeg. Required for books stored as a single M4B/MP3 with chapter markers.
/// </summary>
public interface IChapterExtractor
{
    /// <summary>
    /// Extract a chapter segment from an audio file by start/end time.
    /// Returns a path to the extracted temp file.
    /// </summary>
    Task<string> ExtractChapterAsync(string inputFilePath, double startSeconds, double endSeconds, string outputFormat = "m4a", CancellationToken ct = default);

    /// <summary>
    /// Check if FFmpeg is available on the system.
    /// </summary>
    Task<bool> IsFfmpegAvailableAsync(CancellationToken ct = default);
}
