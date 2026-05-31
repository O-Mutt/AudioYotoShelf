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
    /// Concatenate multiple audio files (in order) into a single output file.
    /// Used to merge a multi-file book into one track. Returns the output temp path.
    /// </summary>
    Task<string> ConcatenateAsync(IReadOnlyList<string> inputFilePaths, string outputFormat = "m4a", CancellationToken ct = default);

    /// <summary>
    /// Split an audio file into segments of at most <paramref name="segmentSeconds"/> each.
    /// Used to break oversized tracks down to the per-track card limit. Returns the segment
    /// paths in order (a single-element list when no split is needed).
    /// </summary>
    Task<IReadOnlyList<string>> SplitAsync(string inputFilePath, double segmentSeconds, string outputFormat = "m4a", CancellationToken ct = default);

    /// <summary>
    /// Check if FFmpeg is available on the system.
    /// </summary>
    Task<bool> IsFfmpegAvailableAsync(CancellationToken ct = default);
}
