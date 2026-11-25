using AudioYotoShelf.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AudioYotoShelf.Infrastructure.Tests;

/// <summary>
/// Runs real, short-lived <c>sh</c> processes: what is under test is how the runner treats an operating-system
/// process (pipes, exit, kill), which a fake cannot show.
/// </summary>
public class SystemProcessRunnerTests : IDisposable
{
    // Generous so a slow CI machine does not fail a test that only needs to finish.
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    private readonly SystemProcessRunner _sut = new(NullLogger<SystemProcessRunner>.Instance);
    private readonly string _pidFile = Path.Combine(Path.GetTempPath(), $"runner_{Guid.NewGuid():N}.pid");

    public void Dispose()
    {
        if (File.Exists(_pidFile)) File.Delete(_pidFile);
    }

    [UnixFact]
    public async Task RunAsync_ReturnsTheExitCodeAndStandardError()
    {
        var result = await _sut.RunAsync("sh", "-c \"echo oops >&2; exit 3\"", captureStandardError: true, default);

        result.Should().Be(new ProcessResult(3, "oops\n"));
    }

    [UnixFact]
    public async Task RunAsync_ReturnsStandardOutput()
    {
        // ffprobe prints the duration it reads on stdout.
        var result = await _sut.RunAsync("sh", "-c \"echo 7213.418\"", captureStandardError: false, default);

        result.Should().Be(new ProcessResult(0, "", "7213.418\n"));
    }

    [UnixFact]
    public async Task RunAsync_NotCapturing_ReturnsEmptyStandardError()
    {
        var result = await _sut.RunAsync("sh", "-c \"echo oops >&2\"", captureStandardError: false, default);

        result.Should().Be(new ProcessResult(0, ""));
    }

    [UnixTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_ChildFillsBothPipes_DoesNotBlock(bool captureStandardError)
    {
        // 1 MiB on each pipe is far past the 64 KiB a Linux pipe buffers.
        const string args = "-c \"head -c 1048576 /dev/zero; head -c 1048576 /dev/zero >&2\"";
        using var deadline = new CancellationTokenSource(Deadline);

        var result = await _sut.RunAsync("sh", args, captureStandardError, deadline.Token);

        result.ExitCode.Should().Be(0);
        result.StandardError.Length.Should().Be(captureStandardError ? 1048576 : 0);
    }

    [UnixFact]
    public async Task RunAsync_Cancelled_KillsTheWholeProcessTreeAndRethrowsTheCancellation()
    {
        using var cts = new CancellationTokenSource();
        // sh stays the parent and sleep its child, so the kill must reach a grandchild of the runner.
        var run = _sut.RunAsync("sh", $"-c \"sleep 30 & echo $$ $! > '{_pidFile}'; wait\"", false, cts.Token);
        var pids = await ReadPidsAsync();

        await cts.CancelAsync();

        var act = () => run;
        await act.Should().ThrowAsync<OperationCanceledException>();
        foreach (var pid in pids)
            (await IsGoneAsync(pid)).Should().BeTrue($"process {pid} should have been killed");
    }

    private async Task<int[]> ReadPidsAsync()
    {
        var giveUp = DateTime.UtcNow + Deadline;
        while (DateTime.UtcNow < giveUp)
        {
            var text = File.Exists(_pidFile) ? await File.ReadAllTextAsync(_pidFile) : "";
            if (text.EndsWith('\n'))
                return text.Split(' ', StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
            await Task.Delay(20);
        }

        throw new TimeoutException("The child never wrote its pids");
    }

    /// <summary>Gone or a zombie: either way it no longer runs. An orphan's reaping by init is not ours to wait on.</summary>
    private static async Task<bool> IsGoneAsync(int pid)
    {
        var giveUp = DateTime.UtcNow + Deadline;
        while (DateTime.UtcNow < giveUp)
        {
            var stat = $"/proc/{pid}/stat";
            try
            {
                if (!File.Exists(stat)) return true;
                var content = await File.ReadAllTextAsync(stat);
                // The state follows the parenthesised command name.
                if (content[(content.LastIndexOf(')') + 2)..].StartsWith('Z')) return true;
            }
            catch (IOException)
            {
                return true;
            }

            await Task.Delay(20);
        }

        return false;
    }

    private sealed class UnixFactAttribute : FactAttribute
    {
        public UnixFactAttribute()
        {
            if (OperatingSystem.IsWindows()) Skip = "Needs sh";
        }
    }

    private sealed class UnixTheoryAttribute : TheoryAttribute
    {
        public UnixTheoryAttribute()
        {
            if (OperatingSystem.IsWindows()) Skip = "Needs sh";
        }
    }
}
