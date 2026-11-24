using AudioYotoShelf.Api.Controllers;
using AudioYotoShelf.Core.DTOs.CardSet;
using AudioYotoShelf.Core.DTOs.Playlist;
using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Infrastructure.Data;
using AudioYotoShelf.Infrastructure.Services.BackgroundJobs;
using FluentAssertions;
using Hangfire;
using Hangfire.States;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudioYotoShelf.Api.Tests;

public class CardSetsControllerTests : IDisposable
{
    private readonly AudioYotoShelfDbContext _db;
    private readonly Mock<ICardSetService> _cardSets = new();
    private readonly Mock<IBackgroundJobClient> _backgroundJobs = new();
    private readonly CardSetsController _sut;

    public CardSetsControllerTests()
    {
        var options = new DbContextOptionsBuilder<AudioYotoShelfDbContext>()
            .UseInMemoryDatabase($"CardSetsCtrlTest_{Guid.NewGuid()}")
            .Options;
        _db = new AudioYotoShelfDbContext(options);
        _backgroundJobs
            .Setup(b => b.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<IState>()))
            .Returns("job-9");

        _sut = new CardSetsController(
            _cardSets.Object, _backgroundJobs.Object, _db, Mock.Of<ILogger<CardSetsController>>());
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private async Task<Guid> AddCardSetOwnedByAsync(Guid ownerId)
    {
        var set = new CardSet { UserConnectionId = ownerId, AbsSeriesId = "ser-1", AbsLibraryId = "lib-1", Name = "Dragons" };
        _db.CardSets.Add(set);
        await _db.SaveChangesAsync();
        return set.Id;
    }

    private static CapacitySummary Capacity(bool fits = true) =>
        new(1, 100, 60, null, 1_000, 500L * 1024 * 1024, fits, false, false, !fits, null, []);

    private static CardSetPlanResponse Plan() => new("Dragons", [], [], [], false, [], ["Card 1 does not fit"]);

    private static CardSetResponse SetResponse(Guid id) => new(
        id, "Dragons", "ser-1", "lib-1", PackingMode.Balance, [], CardSetStatus.Draft, [], [], true, []);

    // =========================================================================
    // Ownership: every action on one set answers 404 for someone else's, and does nothing
    // =========================================================================

    public static TheoryData<string, Func<CardSetsController, Guid, Task<IActionResult>>> ActionsOnOneSet() => new()
    {
        { "Get", (c, id) => c.Get(id, default) },
        { "UpdateBreaks", (c, id) => c.UpdateBreaks(id, new UpdateCardSetBreaksRequest([1]), default) },
        { "Update", (c, id) => c.Update(id, new UpdateCardSetRequest("x"), default) },
        { "RenameCard", (c, id) => c.RenameCard(id, Guid.NewGuid(), new RenameCardSetCardRequest("x"), default) },
        { "Delete", (c, id) => c.Delete(id, default) },
        { "Transfer", (c, id) => c.Transfer(id, new TransferCardSetRequest(), default) },
    };

    [Theory]
    [MemberData(nameof(ActionsOnOneSet))]
    public async Task Action_OnAnotherUsersSet_Returns404AndTouchesNothing(
        string action, Func<CardSetsController, Guid, Task<IActionResult>> call)
    {
        var setId = await AddCardSetOwnedByAsync(Guid.NewGuid());

        var result = await call(_sut.AsUser(Guid.NewGuid()), setId);

        result.Should().BeOfType<NotFoundResult>($"{action} must not act on a set owned by someone else");
        _cardSets.Invocations.Should().BeEmpty();
        _backgroundJobs.VerifyNothingEnqueued();
    }

    [Theory]
    [MemberData(nameof(ActionsOnOneSet))]
    public async Task Action_OnASetThatDoesNotExist_Returns404(
        string action, Func<CardSetsController, Guid, Task<IActionResult>> call)
    {
        var result = await call(_sut.AsUser(Guid.NewGuid()), Guid.NewGuid());

        result.Should().BeOfType<NotFoundResult>($"{action} on a missing set");
        _cardSets.Invocations.Should().BeEmpty();
    }

    // =========================================================================
    // Plan and create
    // =========================================================================

    [Fact]
    public async Task Plan_PlansForTheCaller()
    {
        var caller = Guid.NewGuid();
        var request = new CardSetPlanRequest("ser-1", "lib-1");
        var plan = Plan();
        _cardSets.Setup(s => s.PlanAsync(caller, request, It.IsAny<CancellationToken>())).ReturnsAsync(plan);

        var result = await _sut.AsUser(caller).Plan(request, default);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(plan);
    }

    [Fact]
    public async Task Create_PointsAtTheNewSet()
    {
        var caller = Guid.NewGuid();
        var request = new CreateCardSetRequest("ser-1", "lib-1");
        var created = SetResponse(Guid.NewGuid());
        _cardSets.Setup(s => s.CreateAsync(caller, request, It.IsAny<CancellationToken>())).ReturnsAsync(created);

        var result = await _sut.AsUser(caller).Create(request, default);

        var createdAt = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdAt.ActionName.Should().Be(nameof(CardSetsController.Get));
        createdAt.RouteValues!["cardSetId"].Should().Be(created.Id);
    }

    [Fact]
    public async Task Create_WhenASetExists_Returns409WithItsId()
    {
        var existing = Guid.NewGuid();
        _cardSets.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<CreateCardSetRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CardSetConflictException("A card set already exists for this series", existing));

        var result = await _sut.AsUser(Guid.NewGuid()).Create(new CreateCardSetRequest("ser-1", "lib-1"), default);

        result.Should().BeOfType<ConflictObjectResult>()
            .Which.Value.Should().BeEquivalentTo(new { Message = "A card set already exists for this series", CardSetId = existing });
    }

    [Fact]
    public async Task Create_WithBreaksThatDoNotFit_Returns422WithThePlan()
    {
        var plan = Plan();
        _cardSets.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<CreateCardSetRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CardSetPlanException("A card in this plan does not fit.", plan));

        var result = await _sut.AsUser(Guid.NewGuid()).Create(new CreateCardSetRequest("ser-1", "lib-1", Breaks: [1]), default);

        result.Should().BeOfType<UnprocessableEntityObjectResult>()
            .Which.Value.Should().BeEquivalentTo(new { Message = "A card in this plan does not fit.", Plan = plan });
    }

    [Fact]
    public async Task List_PassesTheSeriesFilter()
    {
        var caller = Guid.NewGuid();
        CardSetSummaryResponse[] sets = [new(Guid.NewGuid(), "Dragons", "ser-1", 3, 1, CardSetStatus.Draft)];
        _cardSets.Setup(s => s.ListAsync(caller, "ser-1", It.IsAny<CancellationToken>())).ReturnsAsync(sets);

        var result = await _sut.AsUser(caller).List("ser-1", default);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(sets);
    }

    // =========================================================================
    // Change
    // =========================================================================

    [Fact]
    public async Task UpdateBreaks_ThatMoveASentCard_Return409()
    {
        var caller = Guid.NewGuid();
        var id = await AddCardSetOwnedByAsync(caller);
        _cardSets.Setup(s => s.UpdateBreaksAsync(id, It.IsAny<UpdateCardSetBreaksRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CardSetConflictException("That change moves card 1, which is already on Yoto.", id));

        var result = await _sut.AsUser(caller).UpdateBreaks(id, new UpdateCardSetBreaksRequest([1]), default);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task UpdateBreaks_ThatDoNotFit_Return422()
    {
        var caller = Guid.NewGuid();
        var id = await AddCardSetOwnedByAsync(caller);
        _cardSets.Setup(s => s.UpdateBreaksAsync(id, It.IsAny<UpdateCardSetBreaksRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CardSetPlanException("A card in this plan does not fit.", Plan()));

        var result = await _sut.AsUser(caller).UpdateBreaks(id, new UpdateCardSetBreaksRequest([1]), default);

        result.Should().BeOfType<UnprocessableEntityObjectResult>();
    }

    // =========================================================================
    // Send
    // =========================================================================

    [Fact]
    public async Task Transfer_QueuesOneJobWithTheCardsInOrder()
    {
        var caller = Guid.NewGuid();
        var id = await AddCardSetOwnedByAsync(caller);
        var cards = new[] { Guid.NewGuid(), Guid.NewGuid() };
        _cardSets.Setup(s => s.PrepareTransferAsync(id, null, It.IsAny<CancellationToken>())).ReturnsAsync(cards);

        var result = await _sut.AsUser(caller).Transfer(id, new TransferCardSetRequest(), default);

        result.Should().BeOfType<AcceptedResult>();
        _backgroundJobs.VerifyEnqueued<ICardSetJobService>("ExecuteCardSetTransferAsync",
            args => (Guid)args[0] == id && ((Guid[])args[1]).SequenceEqual(cards));
    }

    [Fact]
    public async Task Transfer_ACardOverCapacity_Returns409AndQueuesNothing()
    {
        var caller = Guid.NewGuid();
        var id = await AddCardSetOwnedByAsync(caller);
        var card = Guid.NewGuid();
        _cardSets.Setup(s => s.PrepareTransferAsync(id, It.IsAny<IReadOnlyList<Guid>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CardSetCapacityException("Card exceeds Yoto card capacity.", card, Capacity(fits: false)));

        var result = await _sut.AsUser(caller).Transfer(id, new TransferCardSetRequest([card]), default);

        result.Should().BeOfType<ConflictObjectResult>();
        _backgroundJobs.VerifyNothingEnqueued();
    }

    [Fact]
    public async Task Transfer_WhenEveryCardIsSent_QueuesNothing()
    {
        var caller = Guid.NewGuid();
        var id = await AddCardSetOwnedByAsync(caller);
        _cardSets.Setup(s => s.PrepareTransferAsync(id, null, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await _sut.AsUser(caller).Transfer(id, null, default);

        result.Should().BeOfType<OkObjectResult>();
        _backgroundJobs.VerifyNothingEnqueued();
    }
}
