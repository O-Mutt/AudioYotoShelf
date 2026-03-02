using AudioYotoShelf.Api.Controllers;
using AudioYotoShelf.Core.DTOs.Transfer;
using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Core.Tests.Helpers;
using AudioYotoShelf.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudioYotoShelf.Api.Tests;

public class AuthControllerTests : IDisposable
{
    private readonly AudioYotoShelfDbContext _db;
    private readonly AuthController _sut;

    public AuthControllerTests()
    {
        var options = new DbContextOptionsBuilder<AudioYotoShelfDbContext>()
            .UseInMemoryDatabase($"AuthCtrlTest_{Guid.NewGuid()}")
            .Options;
        _db = new AudioYotoShelfDbContext(options);

        _sut = new AuthController(
            Mock.Of<IAudiobookshelfService>(),
            Mock.Of<IYotoService>(),
            _db,
            Mock.Of<ILogger<AuthController>>());
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    // =========================================================================
    // UpdateSettings
    // =========================================================================

    [Fact]
    public async Task UpdateSettings_SavesValues()
    {
        var user = TestData.CreateUserConnection();
        _db.UserConnections.Add(user);
        await _db.SaveChangesAsync();

        var request = new UpdateSettingsRequest(
            DefaultLibraryId: "lib-new",
            DefaultMinAge: 3,
            DefaultMaxAge: 8);

        var result = await _sut.UpdateSettings(user.Id, request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _db.UserConnections.FindAsync(user.Id);
        updated!.DefaultLibraryId.Should().Be("lib-new");
        updated.DefaultMinAge.Should().Be(3);
        updated.DefaultMaxAge.Should().Be(8);
    }

    [Fact]
    public async Task UpdateSettings_PartialUpdate_KeepsExisting()
    {
        var user = TestData.CreateUserConnection();
        user.DefaultMinAge = 2;
        user.DefaultMaxAge = 12;
        user.DefaultLibraryId = "lib-original";
        _db.UserConnections.Add(user);
        await _db.SaveChangesAsync();

        // Only update max age
        var request = new UpdateSettingsRequest(DefaultMaxAge: 15);

        await _sut.UpdateSettings(user.Id, request, CancellationToken.None);

        var updated = await _db.UserConnections.FindAsync(user.Id);
        updated!.DefaultMinAge.Should().Be(2); // Unchanged
        updated.DefaultMaxAge.Should().Be(15); // Updated
        updated.DefaultLibraryId.Should().Be("lib-original"); // Unchanged
    }

    [Fact]
    public async Task UpdateSettings_NotFound_Returns404()
    {
        var request = new UpdateSettingsRequest(DefaultMinAge: 5);

        var result = await _sut.UpdateSettings(Guid.NewGuid(), request, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    // =========================================================================
    // GetConnectionStatus
    // =========================================================================

    [Fact]
    public async Task GetStatus_ReturnsUserStatus()
    {
        var user = TestData.CreateUserConnection();
        _db.UserConnections.Add(user);
        await _db.SaveChangesAsync();

        var result = await _sut.GetConnectionStatus(user.Id, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetStatus_NotFound_Returns404()
    {
        var result = await _sut.GetConnectionStatus(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }
}
