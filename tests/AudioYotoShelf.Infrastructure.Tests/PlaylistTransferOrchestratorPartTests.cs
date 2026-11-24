using AudioYotoShelf.Core.DTOs.Yoto;
using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace AudioYotoShelf.Infrastructure.Tests;

/// <summary>Sending one part of a book split across cards (docs/specs/series-card-sets.md §6).</summary>
public partial class PlaylistTransferOrchestratorTests
{
    private void SetupCardCreation() =>
        _yotoService.Setup(s => s.CreateOrUpdateCardAsync(
                It.IsAny<string>(), It.IsAny<YotoCardContent>(), It.IsAny<YotoCardMetadata>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("card-part");

    private List<(double Start, double End)> RecordExtractions()
    {
        var extracted = new List<(double, double)>();
        _chapterExtractor.Setup(e => e.ExtractChapterAsync(
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, double start, double end, string _, CancellationToken _) =>
            {
                extracted.Add((start, end));
                var path = Path.Combine(_tempDir, $"chapter_{start}_{end}.m4a");
                File.WriteAllBytes(path, new byte[100]);
                return path;
            });
        return extracted;
    }

    private static PlaylistItem Part(List<double> durations, int first, int last, int part, int parts) => new()
    {
        AbsLibraryItemId = "big",
        Position = 0,
        BookTitle = "Big Book",
        TrackDurations = durations,
        TrackBytes = durations.Select(_ => 1_000_000L).ToList(),
        FirstChapterIndex = first,
        LastChapterIndex = last,
        PartNumber = part,
        PartCount = parts,
    };

    private async Task<YotoCardContent?> SendPartAsync(PlaylistItem part)
    {
        YotoCardContent? captured = null;
        _yotoService.Setup(s => s.CreateOrUpdateCardAsync(
                It.IsAny<string>(), It.IsAny<YotoCardContent>(), It.IsAny<YotoCardMetadata>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, YotoCardContent, YotoCardMetadata, string?, string?, CancellationToken>(
                (_, content, _, _, _, _) => captured = content)
            .ReturnsAsync("card-part");

        var playlistId = await SeedPlaylistAsync(grouping: TrackGrouping.Chapters, items: [part]);
        await _sut.TransferPlaylistAsync(playlistId);
        return captured;
    }

    [Fact]
    public async Task TransferPlaylist_PartOfAMultiFileBook_DownloadsOnlyTheFilesInItsRange()
    {
        SetupBook("big", TestData.CreateAbsMedia(audioFiles:
        [
            TestData.CreateAbsAudioFile(0, "ino-1", duration: 900),
            TestData.CreateAbsAudioFile(1, "ino-2", duration: 900),
            TestData.CreateAbsAudioFile(2, "ino-3", duration: 900),
            TestData.CreateAbsAudioFile(3, "ino-4", duration: 900),
        ]));

        var card = await SendPartAsync(Part([900, 900, 900, 900], first: 1, last: 2, part: 2, parts: 2));

        _absService.Verify(s => s.DownloadAudioFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), "big", "ino-2", It.IsAny<CancellationToken>()), Times.Once);
        _absService.Verify(s => s.DownloadAudioFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), "big", "ino-3", It.IsAny<CancellationToken>()), Times.Once);
        _absService.Verify(s => s.DownloadAudioFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), "big", It.IsIn("ino-1", "ino-4"), It.IsAny<CancellationToken>()), Times.Never);

        card!.Chapters.Should().ContainSingle();
        card.Chapters[0].Title.Should().Be("Big Book · Part 2 of 2");
        card.Chapters[0].Tracks.Should().HaveCount(2);
    }

    [Fact]
    public async Task TransferPlaylist_PartOfAChapteredSingleFileBook_ExtractsOnlyItsChapters()
    {
        SetupBook("big", TestData.CreateAbsMedia(
            audioFiles: [TestData.CreateAbsAudioFile(0, "ino-1", duration: 3600)],
            chapters:
            [
                TestData.CreateAbsChapter(0, "One", 0, 900),
                TestData.CreateAbsChapter(1, "Two", 900, 1800),
                TestData.CreateAbsChapter(2, "Three", 1800, 2700),
                TestData.CreateAbsChapter(3, "Four", 2700, 3600),
            ]));
        var extracted = RecordExtractions();

        var card = await SendPartAsync(Part([900, 900, 900, 900], first: 2, last: 3, part: 2, parts: 2));

        extracted.Should().Equal((1800, 2700), (2700, 3600));
        card!.Chapters[0].Tracks.Select(t => t.Title).Should().Equal("Three", "Four");
    }

    [Fact]
    public async Task TransferPlaylist_PartOfABookWithoutChapters_ExtractsItsTimeSegments()
    {
        SetupBook("big", TestData.CreateAbsMedia(
            audioFiles: [TestData.CreateAbsAudioFile(0, "ino-1", duration: 7200)], chapters: []));
        var extracted = RecordExtractions();

        var card = await SendPartAsync(Part([3600, 3600], first: 1, last: 1, part: 2, parts: 2));

        extracted.Should().Equal((3600, 7200));
        card!.Chapters[0].Title.Should().Be("Big Book · Part 2 of 2");
    }

    [Fact]
    public async Task TransferPlaylist_PartOfASingleFileBookWithOneChapter_ExtractsItsTimeSegments()
    {
        // Many .m4b files carry one chapter spanning the whole book; the planner cuts those by time too.
        SetupBook("big", TestData.CreateAbsMedia(
            audioFiles: [TestData.CreateAbsAudioFile(0, "ino-1", duration: 7200)],
            chapters: [TestData.CreateAbsChapter(0, "Whole book", 0, 7200)]));
        var extracted = RecordExtractions();

        await SendPartAsync(Part([3600, 3600], first: 0, last: 0, part: 1, parts: 2));

        extracted.Should().Equal((0, 3600));
    }

    [Fact]
    public async Task TransferPlaylist_PartWhoseBookChangedChapters_FailsAndAsksToReplan()
    {
        SetupBook("big", TestData.CreateAbsMedia(audioFiles:
        [
            TestData.CreateAbsAudioFile(0, "ino-1", duration: 900),
            TestData.CreateAbsAudioFile(1, "ino-2", duration: 900),
        ]));
        SetupCardCreation();
        var playlistId = await SeedPlaylistAsync(
            grouping: TrackGrouping.Chapters, items: [Part([900, 900, 900], first: 1, last: 2, part: 2, parts: 2)]);

        var act = () => _sut.TransferPlaylistAsync(playlistId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*re-plan*");
        var playlist = await _fixture.DbContext.Playlists.FirstAsync(p => p.Id == playlistId);
        playlist.Status.Should().Be(PlaylistStatus.Failed);
    }
}
