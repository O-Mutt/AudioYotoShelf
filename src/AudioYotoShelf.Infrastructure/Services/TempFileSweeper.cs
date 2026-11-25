using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace AudioYotoShelf.Infrastructure.Services;

/// <summary>
/// Deletes temp files that transfers left behind when the process was stopped hard (container
/// restart, out-of-memory kill), which in-process cleanup never gets to run for. Run at startup,
/// before the Hangfire server starts, so no resumed job is writing to these folders yet.
/// </summary>
public static partial class TempFileSweeper
{
    // Only the exact names this app creates: outside the Docker image the system temp folder is
    // shared with other programs, so a looser pattern could delete someone else's file.
    [GeneratedRegex(
        @"^(?:" +
        @"pl_[0-9a-f]{32}(?:\.[^.\\/]+)?" +                                                        // playlist download
        @"|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}_[0-9a-f]{32}_(?:input(?:\.[^.\\/]+)?|track\d+\.tmp)" + // single-book transfer
        @"|(?:chapter|audio)_[0-9a-f]{32}\.[A-Za-z0-9]+" +                                        // ffmpeg extract / merge
        @"|segment_[0-9a-f]{32}_\d{3}\.[A-Za-z0-9]+" +                                            // ffmpeg split
        @"|concat_[0-9a-f]{32}\.txt" +                                                            // ffmpeg concat list
        @")$")]
    private static partial Regex AppTempFile();

    /// <summary>
    /// Deletes the app's temp files written before <paramref name="olderThanUtc"/> in each folder
    /// (top level only), and returns how many it deleted. Missing folders are skipped; a file that
    /// can't be deleted is logged and left.
    /// </summary>
    public static int Sweep(IEnumerable<string> directories, DateTime olderThanUtc, ILogger logger)
    {
        var deleted = 0;
        long bytes = 0;

        foreach (var directory in directories
                     .Select(d => Path.TrimEndingDirectorySeparator(Path.GetFullPath(d)))
                     .Distinct())
        {
            if (!Directory.Exists(directory)) continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory).ToList();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not list temp folder {Directory} for leftover transfer files", directory);
                continue;
            }

            foreach (var path in files)
            {
                if (!AppTempFile().IsMatch(Path.GetFileName(path))) continue;

                try
                {
                    var info = new FileInfo(path);
                    if (info.LastWriteTimeUtc >= olderThanUtc) continue;
                    var size = info.Length;
                    info.Delete();
                    deleted++;
                    bytes += size;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not delete leftover temp file {File}", path);
                }
            }
        }

        if (deleted > 0)
            logger.LogInformation(
                "Deleted {Count} temp files ({Megabytes:F0} MB) left by transfers that were stopped part-way",
                deleted, bytes / 1024.0 / 1024);
        return deleted;
    }
}
