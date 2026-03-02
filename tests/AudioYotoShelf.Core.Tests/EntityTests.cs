using AudioYotoShelf.Core.Entities;
using AudioYotoShelf.Core.Enums;
using AudioYotoShelf.Core.Tests.Helpers;
using FluentAssertions;

namespace AudioYotoShelf.Core.Tests;

public class EntityTests
{
    // =========================================================================
    // UserConnection
    // =========================================================================

    [Fact]
    public void UserConnection_HasValidAbsConnection_TrueWhenTokenAndValidation()
    {
        var user = TestData.CreateUserConnection();
        user.HasValidAbsConnection.Should().BeTrue();
    }

    [Fact]
    public void UserConnection_HasValidAbsConnection_FalseWhenNoToken()
    {
        var user = TestData.CreateUserConnection(absToken: "token");
        user.AudiobookshelfToken = null;
        user.HasValidAbsConnection.Should().BeFalse();
    }

    [Fact]
    public void UserConnection_HasValidAbsConnection_FalseWhenNeverValidated()
    {
        var user = TestData.CreateUserConnection();
        user.AudiobookshelfTokenValidatedAt = null;
        user.HasValidAbsConnection.Should().BeFalse();
    }

    [Fact]
    public void UserConnection_HasValidYotoConnection_TrueWhenTokenNotExpired()
    {
        var user = TestData.CreateUserConnection(
            yotoAccessToken: "token",
            yotoTokenExpiry: DateTimeOffset.UtcNow.AddHours(1));
        user.HasValidYotoConnection.Should().BeTrue();
    }

    [Fact]
    public void UserConnection_HasValidYotoConnection_FalseWhenExpired()
    {
        var user = TestData.CreateUserConnection(
            yotoAccessToken: "token",
            yotoTokenExpiry: DateTimeOffset.UtcNow.AddHours(-1));
        user.HasValidYotoConnection.Should().BeFalse();
    }

    [Fact]
    public void UserConnection_HasValidYotoConnection_FalseWhenNoToken()
    {
        var user = TestData.CreateUserConnection(yotoAccessToken: null);
        user.HasValidYotoConnection.Should().BeFalse();
    }

    // =========================================================================
    // CardTransfer
    // =========================================================================

    [Fact]
    public void CardTransfer_EffectiveAge_UsesSuggestedWhenNoOverride()
    {
        var transfer = TestData.CreateCardTransfer();
        transfer.SuggestedMinAge = 5;
        transfer.SuggestedMaxAge = 10;
        transfer.OverrideMinAge = null;
        transfer.OverrideMaxAge = null;

        transfer.EffectiveMinAge.Should().Be(5);
        transfer.EffectiveMaxAge.Should().Be(10);
    }

    [Fact]
    public void CardTransfer_EffectiveAge_UsesOverrideWhenSet()
    {
        var transfer = TestData.CreateCardTransfer();
        transfer.SuggestedMinAge = 5;
        transfer.SuggestedMaxAge = 10;
        transfer.OverrideMinAge = 3;
        transfer.OverrideMaxAge = 7;

        transfer.EffectiveMinAge.Should().Be(3);
        transfer.EffectiveMaxAge.Should().Be(7);
    }

    [Fact]
    public void CardTransfer_EffectiveAge_PartialOverrideMixesValues()
    {
        var transfer = TestData.CreateCardTransfer();
        transfer.SuggestedMinAge = 5;
        transfer.SuggestedMaxAge = 10;
        transfer.OverrideMinAge = 2;
        transfer.OverrideMaxAge = null; // Keep suggested max

        transfer.EffectiveMinAge.Should().Be(2);
        transfer.EffectiveMaxAge.Should().Be(10);
    }

    // =========================================================================
    // TrackMapping
    // =========================================================================

    [Fact]
    public void TrackMapping_IsUploaded_TrueWhenSha256Set()
    {
        var mapping = TestData.CreateTrackMapping(sha256: "abc123");
        mapping.IsUploaded.Should().BeTrue();
    }

    [Fact]
    public void TrackMapping_IsUploaded_FalseWhenSha256Null()
    {
        var mapping = TestData.CreateTrackMapping(sha256: null);
        mapping.IsUploaded.Should().BeFalse();
    }

    [Fact]
    public void TrackMapping_IsUploaded_FalseWhenSha256Empty()
    {
        var mapping = TestData.CreateTrackMapping();
        mapping.YotoTranscodedSha256 = "";
        mapping.IsUploaded.Should().BeFalse();
    }

    // =========================================================================
    // BaseEntity
    // =========================================================================

    [Fact]
    public void BaseEntity_NewEntity_HasIdAndTimestamps()
    {
        var transfer = TestData.CreateCardTransfer();
        transfer.Id.Should().NotBeEmpty();
        transfer.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        transfer.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
