using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace AudioYotoShelf.IntegrationTests;

/// <summary>
/// End-to-end-ish API tests over the real pipeline (Kestrel TestServer + Postgres + Redis):
/// startup migrations, cookie auth, ownership, admin gating, health probes, and metrics.
/// </summary>
public class AuthIntegrationTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    private async Task<HttpClient> ConnectAsync(string username, string baseUrl = IntegrationTestFactory.AdminAbsUrl)
    {
        factory.Abs.Username = username;
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/auth/abs/connect", new { baseUrl, username = "x", password = "y" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return client;
    }

    [Fact]
    public async Task HealthReady_Returns200_AgainstRealDependencies()
    {
        var resp = await factory.CreateClient().GetAsync("/health/ready");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthLive_Returns200()
    {
        var resp = await factory.CreateClient().GetAsync("/health/live");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Metrics_Returns200()
    {
        var resp = await factory.CreateClient().GetAsync("/metrics");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Libraries_WithoutSession_Returns401()
    {
        var resp = await factory.CreateClient().GetAsync("/api/libraries");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Connect_IssuesSession_AndStatusReflectsConnection()
    {
        // Proves migrations applied (writes UserConnection + LoginEvent), the cookie is issued,
        // and the session is honored on a follow-up request.
        var client = await ConnectAsync("alice");

        var status = await client.GetAsync("/api/auth/status");
        status.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await status.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("absConnected").GetBoolean().Should().BeTrue();
        json.GetProperty("username").GetString().Should().Be("alice");
    }

    [Fact]
    public async Task Libraries_WithSession_Returns200()
    {
        var client = await ConnectAsync("bookworm");
        var resp = await client.GetAsync("/api/libraries");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_ForbiddenForNonAdminUser()
    {
        // Authenticated, but not in Admin:Usernames -> no admin role -> 403.
        var client = await ConnectAsync("regularuser");
        var resp = await client.GetAsync("/api/admin/overview");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_AllowedForAdminUser_AndLoginIsTracked()
    {
        // adminuser is allow-listed and logs in against the trusted ABS URL -> admin session.
        var client = await ConnectAsync("adminuser");
        var resp = await client.GetAsync("/api/admin/overview");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalUsers").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        json.GetProperty("totalLogins").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }
}
