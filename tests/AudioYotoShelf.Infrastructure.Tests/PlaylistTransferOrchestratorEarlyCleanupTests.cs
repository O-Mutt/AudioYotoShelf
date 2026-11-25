using AudioYotoShelf.Core.DTOs.Yoto;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Tests.Helpers;
using FluentAssertions;
using Moq;

namespace AudioYotoShelf.Infrastructure.Tests;

// #133: within a book, each intermediate file goes as soon as the next step has consumed it, rather
// than waiting for the book's tracks to be uploaded. The extractor mocks write real files.
public partial class PlaylistTransferOrchestratorTests
{
    private sealed class DiskAtFirstUpload
    {
        public string? Uploaded { get; set; }
        public string[]? Files { get; set; }
    }

    private DiskAtFirstUpload RecordDiskAtFirstUpload()
    {
        var seen = new DiskAtFirstUpload();
        _yotoService.Setup(s => s.UploadAndTranscodeAsync(
                It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<string>(),
                It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Stream stream, long _, string _, IProgress<int>? _, CancellationToken _) =>
            {
                if (seen.Files is null)
                {
                    seen.Uploaded = ((FileStream)stream).Name;
                    seen.Files = Directory.GetFiles(_tempDir);
                }
                return new YotoTranscodeResult("sha-123", null, null, null);
            });
        return seen;
    }

    private List<string> RecordExtractionInputs()
    {
        var inputs = new List<string>();
        _chapterExtractor.Setup(e => e.ExtractChapterAsync(
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string input, double start, double _, string _, CancellationToken _) =>
            {
                inputs.Add(input);
                return MakeTempFile($"chapter_{start}.m4a", 50);
            });
        return inputs;
    }

    [Fact]
    public async Task TransferPlaylist_MergedAndSplitBook_DeletesThePartsBeforeTheSplitAndTheMergedFileBeforeUpload()
    {
        CaptureCard();
        var parts = new List<string>();
        _chapterExtractor.Setup(e => e.ConcatenateAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> inputs, string _, CancellationToken _) =>
            {
                parts.AddRange(inputs);
                return MakeTempFile("merged.m4a", 200);
            });
        string[]? onDiskAtSplit = null;
        _chapterExtractor.Setup(e => e.SplitAsync(
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                onDiskAtSplit = Directory.GetFiles(_tempDir);
                return (IReadOnlyList<string>)[MakeTempFile("segment_000.m4a", 100), MakeTempFile("segment_001.m4a", 100)];
            });
        var atUpload = RecordDiskAtFirstUpload();
        // Two 2000 s files merge to one 4000 s track, over the one-hour track limit, so it is split.
        var playlistId = await SeedPlaylistAsync(grouping: TrackGrouping.SingleTrack,
            items: [Item("book-1", 0, "Merged", [2000, 2000])]);
        SetupBook("book-1", TestData.CreateAbsMedia(audioFiles:
        [
            TestData.CreateAbsAudioFile(0, "ino-1", duration: 2000),
            TestData.CreateAbsAudioFile(1, "ino-2", duration: 2000),
        ]));

        await _sut.TransferPlaylistAsync(playlistId);

        parts.Should().HaveCount(2);
        onDiskAtSplit.Should().Equal(Path.Combine(_tempDir, "merged.m4a"));
        atUpload.Uploaded.Should().Be(Path.Combine(_tempDir, "segment_000.m4a"));
        atUpload.Files.Should().BeEquivalentTo(
            [Path.Combine(_tempDir, "segment_000.m4a"), Path.Combine(_tempDir, "segment_001.m4a")]);
        TempDirShouldBeEmpty();
    }

    [Fact]
    public async Task TransferPlaylist_SingleFileWithChapters_DeletesTheDownloadOnceEveryChapterIsExtracted()
    {
        CaptureCard();
        var inputs = RecordExtractionInputs();
        var atUpload = RecordDiskAtFirstUpload();
        var playlistId = await SeedPlaylistAsync(grouping: TrackGrouping.Chapters,
            items: [Item("book-1", 0, "Solo", [100, 250])]);
        SetupBook("book-1", TestData.CreateAbsMedia(
            audioFiles: [TestData.CreateAbsAudioFile(0, "ino-1", 350)],
            chapters: [TestData.CreateAbsChapter(0, "One", 0, 100), TestData.CreateAbsChapter(1, "Two", 100, 350)]));

        await _sut.TransferPlaylistAsync(playlistId);

        inputs.Should().HaveCount(2).And.OnlyContain(i => i == inputs[0]);
        atUpload.Files.Should().NotContain(inputs[0]).And.BeEquivalentTo(
            [Path.Combine(_tempDir, "chapter_0.m4a"), Path.Combine(_tempDir, "chapter_100.m4a")]);
        TempDirShouldBeEmpty();
    }

    [Fact]
    public async Task TransferPlaylist_PartOfASingleFileBook_DeletesTheDownloadOnceItsChaptersAreExtracted()
    {
        CaptureCard();
        var inputs = RecordExtractionInputs();
        var atUpload = RecordDiskAtFirstUpload();
        SetupBook("big", TestData.CreateAbsMedia(
            audioFiles: [TestData.CreateAbsAudioFile(0, "ino-1", duration: 3600)],
            chapters:
            [
                TestData.CreateAbsChapter(0, "One", 0, 900),
                TestData.CreateAbsChapter(1, "Two", 900, 1800),
                TestData.CreateAbsChapter(2, "Three", 1800, 2700),
                TestData.CreateAbsChapter(3, "Four", 2700, 3600),
            ]));
        var playlistId = await SeedPlaylistAsync(grouping: TrackGrouping.Chapters,
            items: [Part([900, 900, 900, 900], first: 2, last: 3, part: 2, parts: 2)]);

        await _sut.TransferPlaylistAsync(playlistId);

        inputs.Should().HaveCount(2).And.OnlyContain(i => i == inputs[0]);
        atUpload.Files.Should().NotContain(inputs[0]).And.BeEquivalentTo(
            [Path.Combine(_tempDir, "chapter_1800.m4a"), Path.Combine(_tempDir, "chapter_2700.m4a")]);
        TempDirShouldBeEmpty();
    }

    [Fact]
    public async Task TransferPlaylist_MergedPartOfASingleFileBook_DeletesTheExtractedChaptersOnceMerged()
    {
        CaptureCard();
        var inputs = RecordExtractionInputs();
        _chapterExtractor.Setup(e => e.ConcatenateAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => MakeTempFile("merged.m4a", 100));
        var atUpload = RecordDiskAtFirstUpload();
        SetupBook("big", TestData.CreateAbsMedia(
            audioFiles: [TestData.CreateAbsAudioFile(0, "ino-1", duration: 3600)],
            chapters:
            [
                TestData.CreateAbsChapter(0, "One", 0, 900),
                TestData.CreateAbsChapter(1, "Two", 900, 1800),
                TestData.CreateAbsChapter(2, "Three", 1800, 2700),
                TestData.CreateAbsChapter(3, "Four", 2700, 3600),
            ]));
        var playlistId = await SeedPlaylistAsync(grouping: TrackGrouping.SingleTrack,
            items: [Part([900, 900, 900, 900], first: 0, last: 1, part: 1, parts: 2)]);

        await _sut.TransferPlaylistAsync(playlistId);

        inputs.Should().HaveCount(2);
        atUpload.Uploaded.Should().Be(Path.Combine(_tempDir, "merged.m4a"));
        atUpload.Files.Should().Equal(Path.Combine(_tempDir, "merged.m4a"));
        TempDirShouldBeEmpty();
    }

    [Fact]
    public async Task TransferPlaylist_MergedTrackThatIsNotSplit_IsKeptUntilUploadButItsPartsAreNot()
    {
        CaptureCard();
        _chapterExtractor.Setup(e => e.ConcatenateAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => MakeTempFile("merged.m4a", 100));
        var atUpload = RecordDiskAtFirstUpload();
        var playlistId = await SeedPlaylistAsync(grouping: TrackGrouping.SingleTrack,
            items: [Item("book-1", 0, "Short", [600, 600])]);
        SetupBook("book-1", TestData.CreateAbsMedia(audioFiles:
        [
            TestData.CreateAbsAudioFile(0, "ino-1", duration: 600),
            TestData.CreateAbsAudioFile(1, "ino-2", duration: 600),
        ]));

        await _sut.TransferPlaylistAsync(playlistId);

        _chapterExtractor.Verify(e => e.SplitAsync(
            It.IsAny<string>(), It.IsAny<double>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        atUpload.Uploaded.Should().Be(Path.Combine(_tempDir, "merged.m4a"));
        atUpload.Files.Should().Equal(Path.Combine(_tempDir, "merged.m4a"));
        TempDirShouldBeEmpty();
    }

    [Fact]
    public async Task TransferPlaylist_DownloadThatIsNotSplit_IsKeptUntilItIsUploaded()
    {
        CaptureCard();
        var atUpload = RecordDiskAtFirstUpload();
        var playlistId = await SeedPlaylistAsync(grouping: TrackGrouping.Chapters,
            items: [Item("book-1", 0, "Short", [300])]);
        SetupBook("book-1", TestData.CreateAbsMedia(
            audioFiles: [TestData.CreateAbsAudioFile(0, "ino-1", duration: 300)], chapters: []));

        await _sut.TransferPlaylistAsync(playlistId);

        atUpload.Uploaded.Should().StartWith(_tempDir);
        atUpload.Files.Should().Equal(atUpload.Uploaded);
        TempDirShouldBeEmpty();
    }
}
