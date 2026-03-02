using AudioYotoShelf.Api.Controllers;
using AudioYotoShelf.Core.DTOs.Transfer;
using FluentValidation;

namespace AudioYotoShelf.Api.Configuration;

public class AbsConnectRequestValidator : AbstractValidator<AuthController.AbsConnectRequest>
{
    public AbsConnectRequestValidator()
    {
        RuleFor(x => x.BaseUrl)
            .NotEmpty().WithMessage("Server URL is required")
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                         (uri.Scheme == "http" || uri.Scheme == "https"))
            .WithMessage("Must be a valid HTTP/HTTPS URL");

        RuleFor(x => x.Username).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class CreateTransferRequestValidator : AbstractValidator<CreateTransferRequest>
{
    public CreateTransferRequestValidator()
    {
        RuleFor(x => x.AbsLibraryItemId).NotEmpty().MaximumLength(256);

        RuleFor(x => x.OverrideMinAge)
            .InclusiveBetween(0, 18)
            .When(x => x.OverrideMinAge.HasValue);

        RuleFor(x => x.OverrideMaxAge)
            .InclusiveBetween(0, 18)
            .When(x => x.OverrideMaxAge.HasValue);

        RuleFor(x => x)
            .Must(x => !x.OverrideMinAge.HasValue || !x.OverrideMaxAge.HasValue ||
                       x.OverrideMinAge < x.OverrideMaxAge)
            .WithMessage("Min age must be less than max age");
    }
}

public class CreateSeriesTransferRequestValidator : AbstractValidator<CreateSeriesTransferRequest>
{
    public CreateSeriesTransferRequestValidator()
    {
        RuleFor(x => x.AbsSeriesId).NotEmpty().MaximumLength(256);
        RuleFor(x => x.AbsLibraryId).NotEmpty().MaximumLength(256);

        RuleFor(x => x.OverrideMinAge)
            .InclusiveBetween(0, 18)
            .When(x => x.OverrideMinAge.HasValue);

        RuleFor(x => x.OverrideMaxAge)
            .InclusiveBetween(0, 18)
            .When(x => x.OverrideMaxAge.HasValue);
    }
}
