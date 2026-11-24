using AudioYotoShelf.Core.DTOs.Audiobookshelf;
using AudioYotoShelf.Core.DTOs.Transfer;
using AudioYotoShelf.Core.DTOs.Yoto;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Core.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace AudioYotoShelf.Infrastructure.Tests;

/// <summary>
/// A single-book transfer is checked against the card limits before anything is downloaded (#117).
/// </summary>
public partial class TransferOrchestratorTests
{
    private const double Hour = 3600;

    /// <summary>Three 5-hour files: about 640 MB as Yoto stores it at 100 kbps, over the 500 MB card.</summary>
    private static AbsLibraryItem BookLargerThanOneCard(string id = "item-123", string? seriesName = null) =>
        TestData.CreateAbsLibraryItem(id, TestData.CreateAbsMedia(
            TestData.CreateAbsMetadata("Big Book", seriesName: seriesName, seriesSequence: seriesName is null ? null : "2"),
            audioFiles: Enumerable.Range(0, 3)
                .Select(i => TestData.CreateAbsAudioFile(i, $"ino-{i}", 5 * Hour, 100_000_000))
                .ToArray(),
            duration: 15 * Hour));

    private void VerifyNothingDownloadedOrUploaded(string itemId)
    {
        _absService.Verify(s => s.DownloadAudioFileWithMetadataAsync(
            It.IsAny<string>(), It.IsAny<string>(), itemId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _yotoService.Verify(s => s.CreateOrUpdateCardAsync(
            It.IsAny<string>(), It.IsAny<YotoCardContent>(), It.IsAny<YotoCardMetadata>(),
            "Big Book", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TransferBookAsync_BookLargerThanOneCard_FailsWithTheReasonBeforeDownloading()
    {
        var user = await SeedUserAsync();
        _absService.Setup(s => s.GetLibraryItemAsync(
                It.IsAny<string>(), It.IsAny<string>(), "item-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BookLargerThanOneCard());

        var act = () => _sut.TransferBookAsync(user.Id, TestData.CreateTransferRequest());

        await act.Should().ThrowAsync<CardCapacityExceededException>();
        var transfer = await _db.CardTransfers.SingleAsync();
        transfer.Status.Should().Be(TransferStatus.Failed);
        transfer.ErrorMessage.Should().Contain("\"Big Book\" is larger than one Yoto card")
            .And.Contain("exceeds the limit of 500MB");
        VerifyNothingDownloadedOrUploaded("item-123");
        _yotoService.Verify(s => s.UploadAndTranscodeAsync(
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<string>(),
            It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TransferBookAsync_BookInASeriesLargerThanOneCard_PointsAtTheSeriesCardPlan()
    {
        var user = await SeedUserAsync();
        _absService.Setup(s => s.GetLibraryItemAsync(
                It.IsAny<string>(), It.IsAny<string>(), "item-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BookLargerThanOneCard(seriesName: "Wizards"));

        var act = () => _sut.TransferBookAsync(user.Id, TestData.CreateTransferRequest());

        (await act.Should().ThrowAsync<CardCapacityExceededException>())
            .Which.Message.Should().Contain("series \"Wizards\"").And.Contain("card plan");
    }

    /// <summary>
    /// 60 five-minute files of 200 MB. Split to 100 MB pieces that would be 120 tracks, over the
    /// 100-track card; the single-book transfer uploads them whole, so the card gets 60.
    /// </summary>
    private static AbsLibraryItem BookWithLargeTracksThatFitUnsplit() =>
        TestData.CreateAbsLibraryItem(media: TestData.CreateAbsMedia(
            audioFiles: Enumerable.Range(0, 60)
                .Select(i => TestData.CreateAbsAudioFile(i, $"ino-{i}", 300, 200L * 1024 * 1024))
                .ToArray(),
            duration: 60 * 300));

    [Fact]
    public async Task CheckBookCapacityAsync_TracksOverThePerTrackSize_CountOncePerTrackBecauseTheyAreNotSplit()
    {
        var user = await SeedUserAsync();
        ServeItemWith(BookWithLargeTracksThatFitUnsplit());

        (await _sut.CheckBookCapacityAsync(user.Id, "item-123")).Should().BeNull();
    }

    [Fact]
    public async Task TransferBookAsync_TracksOverThePerTrackSize_AreNotRefusedBecauseTheyAreNotSplit()
    {
        var user = await SeedUserAsync();
        ServeItemWith(BookWithLargeTracksThatFitUnsplit());

        var result = await _sut.TransferBookAsync(user.Id, TestData.CreateTransferRequest());

        result.Status.Should().Be(TransferStatus.Completed);
        result.Tracks.Should().HaveCount(60);
    }

    [Fact]
    public async Task TransferBookAsync_BookLargerThanOneCard_IsNotCountedAsAFailedTransfer()
    {
        var (totals, scope) = ListenToTransferMetrics();
        using var _ = scope;
        var user = await SeedUserAsync();
        ServeItemWith(BookLargerThanOneCard());
        var updates = CaptureNotifications();
        var transferId = Guid.NewGuid();

        var act = () => _sut.TransferBookAsync(user.Id, TestData.CreateTransferRequest(), transferId);

        var thrown = (await act.Should().ThrowAsync<CardCapacityExceededException>()).Which;
        thrown.Transfer!.Status.Should().Be(TransferStatus.Failed);
        thrown.Transfer.ErrorMessage.Should().Be(thrown.Message);
        updates.Should().ContainSingle(u => u.TransferId == transferId && u.Status == TransferStatus.Failed);
        totals.GetValueOrDefault("ays.transfers.failed").Should().Be(0);
    }

    [Fact]
    public async Task TransferBookAsync_BookLargerThanOneCardSentAnyway_IsSentAsOneCard()
    {
        var user = await SeedUserAsync();
        ServeItemWith(BookLargerThanOneCard());
        var transferId = Guid.NewGuid();

        var result = await _sut.TransferBookAsync(
            user.Id, TestData.CreateTransferRequest() with { AllowOversized = true }, transferId);

        result.Status.Should().Be(TransferStatus.Completed);
        (await ReadTransferAsync(transferId)).AllowOversized.Should().BeTrue();
    }

    [Fact]
    public async Task RetryTransferAsync_BookSentAnyway_KeepsThatChoice()
    {
        var user = await SeedUserAsync();
        ServeItemWith(BookLargerThanOneCard());
        var transfer = TestData.CreateCardTransfer(user.Id, status: TransferStatus.Failed);
        transfer.AllowOversized = true;
        _db.CardTransfers.Add(transfer);
        await _db.SaveChangesAsync();

        var result = await _sut.RetryTransferAsync(transfer.Id);

        result.Status.Should().Be(TransferStatus.Completed);
    }

    [Fact]
    public async Task RetryTransferAsync_BookRefusedEarlier_IsRefusedAgain()
    {
        var user = await SeedUserAsync();
        ServeItemWith(BookLargerThanOneCard());
        var transfer = TestData.CreateCardTransfer(user.Id, status: TransferStatus.Failed);
        _db.CardTransfers.Add(transfer);
        await _db.SaveChangesAsync();

        var act = () => _sut.RetryTransferAsync(transfer.Id);

        await act.Should().ThrowAsync<CardCapacityExceededException>();
    }

    [Fact]
    public async Task CheckBookCapacityAsync_BookLargerThanOneCard_ReturnsTheReasonAndProjection()
    {
        var user = await SeedUserAsync();
        _absService.Setup(s => s.GetLibraryItemAsync(
                user.AudiobookshelfUrl, user.AudiobookshelfToken!, "item-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BookLargerThanOneCard());

        var refusal = await _sut.CheckBookCapacityAsync(user.Id, "item-123");

        refusal.Should().NotBeNull();
        refusal!.Message.Should().StartWith("\"Big Book\" is larger than one Yoto card");
        refusal.Capacity.WithinLimits.Should().BeFalse();
        refusal.Capacity.ExceedsBytes.Should().BeTrue();
        refusal.Capacity.Tracks.Should().Be(3);
    }

    [Fact]
    public async Task CheckBookCapacityAsync_BookThatFits_ReturnsNull()
    {
        var user = await SeedUserAsync();
        _absService.Setup(s => s.GetLibraryItemAsync(
                It.IsAny<string>(), It.IsAny<string>(), "item-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestData.CreateAbsLibraryItem());

        (await _sut.CheckBookCapacityAsync(user.Id, "item-123")).Should().BeNull();
    }

    [Fact]
    public async Task TransferSeriesAsync_OneBookLargerThanOneCard_SendsTheOthersAndReportsItWithTheReason()
    {
        var user = await SeedUserAsync();
        _absService.Setup(s => s.GetSeriesDetailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestData.CreateAbsSeriesItem("Wizards", 3));
        _absService.Setup(s => s.GetLibraryItemAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, string id, CancellationToken _) =>
                id == "book-2" ? BookLargerThanOneCard(id, "Wizards") : TestData.CreateAbsLibraryItem(id));

        var results = await _sut.TransferSeriesAsync(user.Id, TestData.CreateSeriesTransferRequest());

        results.Should().HaveCount(3);
        results.Where(r => r.AbsLibraryItemId != "book-2").Should().OnlyContain(r => r.Status == TransferStatus.Completed);
        var notSent = results.Single(r => r.AbsLibraryItemId == "book-2");
        notSent.Status.Should().Be(TransferStatus.Failed);
        notSent.ErrorMessage.Should().Contain("larger than one Yoto card").And.Contain("series \"Wizards\"");
        VerifyNothingDownloadedOrUploaded("book-2");
    }

    [Fact]
    public async Task TransferSeriesAsync_RefusedBookDeletedMeanwhile_StillSendsTheBooksAfterIt()
    {
        var user = await SeedUserAsync();
        _absService.Setup(s => s.GetSeriesDetailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestData.CreateAbsSeriesItem("Wizards", 3));
        _absService.Setup(s => s.GetLibraryItemAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, string id, CancellationToken _) =>
                id == "book-1" ? BookLargerThanOneCard(id, "Wizards") : TestData.CreateAbsLibraryItem(id));
        // The person deletes the failed transfer from the Transfers page as soon as it shows up.
        _notifier.Setup(n => n.SendProgressAsync(
                It.Is<TransferProgressUpdate>(u => u.Status == TransferStatus.Failed), It.IsAny<CancellationToken>()))
            .Callback<TransferProgressUpdate, CancellationToken>((update, _) =>
            {
                _db.CardTransfers.Remove(_db.CardTransfers.Single(t => t.Id == update.TransferId));
                _db.SaveChanges();
            })
            .Returns(Task.CompletedTask);

        var results = await _sut.TransferSeriesAsync(user.Id, TestData.CreateSeriesTransferRequest());

        results.Select(r => (r.AbsLibraryItemId, r.Status)).Should().Equal(
            ("book-1", TransferStatus.Failed),
            ("book-2", TransferStatus.Completed),
            ("book-3", TransferStatus.Completed));
    }
}
