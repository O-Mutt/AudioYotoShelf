using System.Diagnostics;

namespace AudioYotoShelf.Infrastructure.Services;

/// <summary>The real <see cref="IProcessRunner"/>: starts an operating-system process. Not unit-tested by design.</summary>
public sealed class SystemProcessRunner : IProcessRunner
{
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
            var stderr = captureStandardError ? await process.StandardError.ReadToEndAsync(ct) : "";
            await process.WaitForExitAsync(ct);

            return new ProcessResult(process.ExitCode, stderr);
        }
        catch (OperationCanceledException)
        {
            // Cancelling the wait does not stop the process. Left running, ffmpeg would keep writing
            // output (or new segments) after the caller has deleted what it saw.
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It exited between the cancellation and the kill.
            }

            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }
}
