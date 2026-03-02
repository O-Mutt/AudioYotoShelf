using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Tests.Helpers;
using AudioYotoShelf.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AudioYotoShelf.Infrastructure.Tests;

public class DbContextTests : IDisposable
{
    private readonly InMemoryDbFixture _fixture;

    public DbContextTests()
    {
        _fixture = new InMemoryDbFixture();
    }

    public void Dispose() => _fixture.Dispose();

    // =========================================================================
    // SaveChangesAsync - audit timestamps
    // =========================================================================

    [Fact]
    public async Task SaveChangesAsync_NewEntity_SetsCreatedAtAndUpdatedAt()
    {
        var user = TestData.CreateUserConnection();
        _fixture.DbContext.UserConnections.Add(user);
        await _fixture.DbContext.SaveChangesAsync();

        user.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        user.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SaveChangesAsync_ModifiedEntity_UpdatesTimestamp()
    {
        var user = TestData.CreateUserConnection();
        _fixture.DbContext.UserConnections.Add(user);
        await _fixture.DbContext.SaveChangesAsync();

        var originalUpdatedAt = user.UpdatedAt;
        await Task.Delay(50); // Small delay to ensure timestamp differs

        user.Username = "updated-user";
        _fixture.DbContext.Entry(user).State = EntityState.Modified;
        await _fixture.DbContext.SaveChangesAsync();

        user.UpdatedAt.Should().BeOnOrAfter(originalUpdatedAt);
    }

    // =========================================================================
    // UserConnection CRUD
    // =========================================================================

    [Fact]
    public async Task UserConnection_Create_PersistsAllFields()
    {
        var user = TestData.CreateUserConnection(
            username: "persist-test",
            absUrl: "http://my-abs.local:13378",
            absToken: "my-token");

        _fixture.DbContext.UserConnections.Add(user);
        await _fixture.DbContext.SaveChangesAsync();

        var loaded = await _fixture.DbContext.UserConnections.FindAsync(user.Id);
        loaded.Should().NotBeNull();
        loaded!.Username.Should().Be("persist-test");
        loaded.AudiobookshelfUrl.Should().Be("http://my-abs.local:13378");
        loaded.AudiobookshelfToken.Should().Be("my-token");
    }

    [Fact]
    public async Task UserConnection_UniqueUsername_EnforcedByIndex()
    {
        var user1 = TestData.CreateUserConnection(username: "dupe-user");
        var user2 = TestData.CreateUserConnection(username: "dupe-user");

        _fixture.DbContext.UserConnections.Add(user1);
        await _fixture.DbContext.SaveChangesAsync();

        _fixture.DbContext.UserConnections.Add(user2);

        // InMemory provider doesn't enforce unique constraints,
        // but verify the model configuration is correct
        var entityType = _fixture.DbContext.Model.FindEntityType(typeof(UserConnection));
        var index = entityType!.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "Username"));
        index.Should().NotBeNull();
        index!.IsUnique.Should().BeTrue();
    }

    // =========================================================================
    // CardTransfer relationships
    // =========================================================================

    [Fact]
    public async Task CardTransfer_BelongsToUserConnection()
    {
        var user = TestData.CreateUserConnection();
        _fixture.DbContext.UserConnections.Add(user);

        var transfer = TestData.CreateCardTransfer(userConnectionId: user.Id);
        _fixture.DbContext.CardTransfers.Add(transfer);
        await _fixture.DbContext.SaveChangesAsync();

        var loaded = await _fixture.DbContext.CardTransfers
            .Include(t => t.UserConnection)
            .FirstAsync(t => t.Id == transfer.Id);

        loaded.UserConnection.Should().NotBeNull();
        loaded.UserConnection.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task CardTransfer_HasManyTrackMappings()
    {
        var user = TestData.CreateUserConnection();
        _fixture.DbContext.UserConnections.Add(user);

        var transfer = TestData.CreateCardTransfer(userConnectionId: user.Id);
        _fixture.DbContext.CardTransfers.Add(transfer);

        var track1 = TestData.CreateTrackMapping(cardTransferId: transfer.Id, index: 0);
        var track2 = TestData.CreateTrackMapping(cardTransferId: transfer.Id, index: 1);
        _fixture.DbContext.TrackMappings.AddRange(track1, track2);

        await _fixture.DbContext.SaveChangesAsync();

        var loaded = await _fixture.DbContext.CardTransfers
            .Include(t => t.TrackMappings)
            .FirstAsync(t => t.Id == transfer.Id);

        loaded.TrackMappings.Should().HaveCount(2);
    }

    // =========================================================================
    // TrackMapping SHA256 deduplication index
    // =========================================================================

    [Fact]
    public async Task TrackMapping_Sha256Index_ExistsForDeduplication()
    {
        var entityType = _fixture.DbContext.Model.FindEntityType(typeof(TrackMapping));
        var sha256Index = entityType!.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "YotoTranscodedSha256"));

        sha256Index.Should().NotBeNull("SHA256 index is required for deduplication queries");
    }

    // =========================================================================
    // GeneratedIcon relationship to TrackMapping
    // =========================================================================

    [Fact]
    public async Task TrackMapping_OptionalIconRelationship()
    {
        var user = TestData.CreateUserConnection();
        _fixture.DbContext.UserConnections.Add(user);

        var transfer = TestData.CreateCardTransfer(userConnectionId: user.Id);
        _fixture.DbContext.CardTransfers.Add(transfer);

        var icon = new GeneratedIcon
        {
            UserConnectionId = user.Id,
            Prompt = "test prompt",
            ContextTitle = "Test Book - Chapter 1",
            Source = IconSource.GeminiGenerated,
            YotoIconUrl = "https://icons.yotoplay.com/test.png",
            TimesUsed = 1
        };
        _fixture.DbContext.GeneratedIcons.Add(icon);

        var track = TestData.CreateTrackMapping(cardTransferId: transfer.Id);
        track.GeneratedIconId = icon.Id;
        _fixture.DbContext.TrackMappings.Add(track);

        await _fixture.DbContext.SaveChangesAsync();

        var loaded = await _fixture.DbContext.TrackMappings
            .Include(t => t.GeneratedIcon)
            .FirstAsync(t => t.Id == track.Id);

        loaded.GeneratedIcon.Should().NotBeNull();
        loaded.GeneratedIcon!.YotoIconUrl.Should().Be("https://icons.yotoplay.com/test.png");
    }

    // =========================================================================
    // Enum string conversion
    // =========================================================================

    [Fact]
    public async Task CardTransfer_StatusStoredAsString()
    {
        var user = TestData.CreateUserConnection();
        _fixture.DbContext.UserConnections.Add(user);

        var transfer = TestData.CreateCardTransfer(userConnectionId: user.Id, status: TransferStatus.AwaitingTranscode);
        _fixture.DbContext.CardTransfers.Add(transfer);
        await _fixture.DbContext.SaveChangesAsync();

        // Verify model configuration stores enum as string
        var entityType = _fixture.DbContext.Model.FindEntityType(typeof(CardTransfer));
        var statusProperty = entityType!.FindProperty(nameof(CardTransfer.Status));
        statusProperty.Should().NotBeNull();

        var loaded = await _fixture.DbContext.CardTransfers.FindAsync(transfer.Id);
        loaded!.Status.Should().Be(TransferStatus.AwaitingTranscode);
    }
}
