using System.Diagnostics;
using AudioYotoShelf.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AudioYotoShelf.Infrastructure.Services;

public class FfmpegChapterExtractor(ILogger<FfmpegChapterExtractor> logger) : IChapterExtractor
{
    public async Task<string> ExtractChapterAsync(
        string inputFilePath, double startSeconds, double endSeconds,
        string outputFormat = "m4a", CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(inputFilePath, nameof(inputFilePath));
        if (!File.Exists(inputFilePath))
            throw new FileNotFoundException("Input audio file not found", inputFilePath);
        if (startSeconds < 0)
            throw new ArgumentException("Start time cannot be negative", nameof(startSeconds));
        if (endSeconds <= startSeconds)
            throw new ArgumentException("End time must be greater than start time", nameof(endSeconds));

        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"chapter_{Guid.NewGuid():N}.{outputFormat}");

        var args = $"-i \"{inputFilePath}\" -ss {startSeconds:F3} -to {endSeconds:F3} -c copy -y \"{outputPath}\"";

        logger.LogInformation("Extracting chapter: ffmpeg {Args}", args);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            logger.LogError("FFmpeg failed with exit code {ExitCode}: {Stderr}", process.ExitCode, stderr);
            throw new InvalidOperationException($"FFmpeg chapter extraction failed: {stderr}");
        }

        logger.LogInformation("Chapter extracted to {OutputPath} ({Size} bytes)",
            outputPath, new FileInfo(outputPath).Length);

        return outputPath;
    }

    public async Task<bool> IsFfmpegAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
