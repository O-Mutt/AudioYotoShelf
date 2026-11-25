using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AudioYotoShelf.Infrastructure.Services;

/// <summary>The real <see cref="IProcessRunner"/>: starts an operating-system process.</summary>
public sealed class SystemProcessRunner(ILogger<SystemProcessRunner> logger) : IProcessRunner
{
    // A killed process stuck in uninterruptible I/O may never exit; the caller's error matters more than reaping it.
    private static readonly TimeSpan ExitWaitAfterKill = TimeSpan.FromSeconds(5);

    public async Task<ProcessResult> RunAsync(
        string fileName, string arguments, bool captureStandardError, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        try
        {
            // Both pipes are drained even when unused: a child that fills a pipe nobody reads blocks forever.
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var standardOutput = await stdout;
            var standardError = await stderr;

            return new ProcessResult(process.ExitCode, captureStandardError ? standardError : "", standardOutput);
        }
        catch
        {
            // Left running, ffmpeg keeps writing output after the caller has deleted what it saw.
            await StopAsync(process, fileName);
            throw;
        }
    }

    private async Task StopAsync(Process process, string fileName)
    {
        // Failures here are logged, not thrown: the exception that ended the run is the one the caller needs.
        try
        {
            if (process.HasExited) return;
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to kill {FileName} after its run was abandoned", fileName);
        }

        try
        {
            using var timeout = new CancellationTokenSource(ExitWaitAfterKill);
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{FileName} did not exit after it was killed", fileName);
        }
    }
}
