using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AudioYotoShelf.IntegrationTests;

/// <summary>
/// The Transfers page's card-set list over real Postgres: the query must translate, the status
/// filter must bind from the query string, and only the caller's sets come back.
/// </summary>
public class CardSetSendsIntegrationTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    private async Task<(HttpClient Client, Guid UserId)> ConnectAsync(string username)
    {
        factory.Abs.Username = username;
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/auth/abs/connect", new { baseUrl = IntegrationTestFactory.AdminAbsUrl, username = "x", password = "y" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AudioYotoShelfDbContext>();
        var userId = await db.UserConnections.Where(u => u.Username == username).Select(u => u.Id).SingleAsync();
        return (client, userId);
    }

    private async Task<Guid> SeedSetAsync(Guid userId, string seriesId, params PlaylistStatus[] cards)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AudioYotoShelfDbContext>();
        var set = new CardSet { UserConnectionId = userId, AbsSeriesId = seriesId, AbsLibraryId = "lib-1", Name = seriesId };
        set.Cards = cards.Select((status, i) => new Playlist
        {
            UserConnectionId = userId,
            Name = $"{seriesId} card {i + 1}",
            Status = status,
            SetIndex = i,
            Items = [new PlaylistItem { AbsLibraryItemId = $"{seriesId}-{i}", BookTitle = $"Book {i + 1}", Position = 0 }],
        }).ToList();
        db.CardSets.Add(set);
        await db.SaveChangesAsync();
        return set.Id;
    }

    [Fact]
    public async Task Sends_WithoutSession_Returns401()
    {
        var resp = await factory.CreateClient().GetAsync("/api/cardsets/sends");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sends_ListsOnlyTheCallersSentSets_FilteredByStatus()
    {
        var (client, userId) = await ConnectAsync("sendsowner");
        var (_, otherId) = await ConnectAsync("sendsother");
        var sending = await SeedSetAsync(userId, "ser-sending", PlaylistStatus.Transferred, PlaylistStatus.Queued);
        var complete = await SeedSetAsync(userId, "ser-complete", PlaylistStatus.Transferred);
        await SeedSetAsync(userId, "ser-draft", PlaylistStatus.Draft);
        await SeedSetAsync(otherId, "ser-theirs", PlaylistStatus.Transferred);

        var all = await client.GetFromJsonAsync<JsonElement>("/api/cardsets/sends");
        var filtered = await client.GetFromJsonAsync<JsonElement>("/api/cardsets/sends?status=Sending");

        all.EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).Should().BeEquivalentTo([sending, complete]);
        var one = filtered.EnumerateArray().Should().ContainSingle().Subject;
        one.GetProperty("id").GetGuid().Should().Be(sending);
        one.GetProperty("status").GetString().Should().Be("Sending");
        one.GetProperty("sentCount").GetInt32().Should().Be(1);
        one.GetProperty("cards")[0].GetProperty("bookTitles")[0].GetString().Should().Be("Book 1");
    }
}
