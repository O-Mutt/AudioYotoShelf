using AudioYotoShelf.Api.Configuration;
using AudioYotoShelf.Api.Controllers;
using AudioYotoShelf.Core.DTOs.Transfer;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace AudioYotoShelf.Api.Tests;

public class ValidatorTests
{
    // =========================================================================
    // AbsConnectRequestValidator
    // =========================================================================

    private readonly AbsConnectRequestValidator _absValidator = new();

    [Fact]
    public void AbsConnect_ValidRequest_Passes()
    {
        var request = new AuthController.AbsConnectRequest(
            "http://abs.local:13378", "admin", "password");
        var result = _absValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("", "admin", "pass")]
    [InlineData("not-a-url", "admin", "pass")]
    [InlineData("ftp://bad.com", "admin", "pass")]
    public void AbsConnect_InvalidUrl_Fails(string url, string user, string pass)
    {
        var request = new AuthController.AbsConnectRequest(url, user, pass);
        var result = _absValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.BaseUrl);
    }

    [Fact]
    public void AbsConnect_EmptyUsername_Fails()
    {
        var request = new AuthController.AbsConnectRequest("http://abs.local", "", "pass");
        var result = _absValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Username);
    }

    [Fact]
    public void AbsConnect_EmptyPassword_Fails()
    {
        var request = new AuthController.AbsConnectRequest("http://abs.local", "admin", "");
        var result = _absValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void AbsConnect_HttpsUrl_Passes()
    {
        var request = new AuthController.AbsConnectRequest("https://abs.example.com", "admin", "pass");
        var result = _absValidator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.BaseUrl);
    }

    // =========================================================================
    // CreateTransferRequestValidator
    // =========================================================================

    private readonly CreateTransferRequestValidator _transferValidator = new();

    [Fact]
    public void Transfer_ValidRequest_Passes()
    {
        var request = new CreateTransferRequest("item-123");
        var result = _transferValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Transfer_EmptyItemId_Fails()
    {
        var request = new CreateTransferRequest("");
        var result = _transferValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.AbsLibraryItemId);
    }

    [Fact]
    public void Transfer_ValidAgeOverride_Passes()
    {
        var request = new CreateTransferRequest("item-123", OverrideMinAge: 3, OverrideMaxAge: 12);
        var result = _transferValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Transfer_MinAgeOverMax_Fails()
    {
        var request = new CreateTransferRequest("item-123", OverrideMinAge: 15, OverrideMaxAge: 5);
        var result = _transferValidator.TestValidate(request);
        result.ShouldHaveAnyValidationError();
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(19, null)]
    [InlineData(null, 20)]
    [InlineData(null, -5)]
    public void Transfer_AgeOutOfRange_Fails(int? min, int? max)
    {
        var request = new CreateTransferRequest("item-123", OverrideMinAge: min, OverrideMaxAge: max);
        var result = _transferValidator.TestValidate(request);
        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    public void Transfer_NullAgeOverrides_Passes()
    {
        var request = new CreateTransferRequest("item-123", OverrideMinAge: null, OverrideMaxAge: null);
        var result = _transferValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // =========================================================================
    // CreateSeriesTransferRequestValidator
    // =========================================================================

    private readonly CreateSeriesTransferRequestValidator _seriesValidator = new();

    [Fact]
    public void SeriesTransfer_ValidRequest_Passes()
    {
        var request = new CreateSeriesTransferRequest("series-1", "lib-1");
        var result = _seriesValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void SeriesTransfer_EmptySeriesId_Fails()
    {
        var request = new CreateSeriesTransferRequest("", "lib-1");
        var result = _seriesValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.AbsSeriesId);
    }

    [Fact]
    public void SeriesTransfer_EmptyLibraryId_Fails()
    {
        var request = new CreateSeriesTransferRequest("series-1", "");
        var result = _seriesValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.AbsLibraryId);
    }
}
