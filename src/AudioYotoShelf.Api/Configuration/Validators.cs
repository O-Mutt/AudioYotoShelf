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

/// <summary>
/// Phase 2: Validates batch transfer requests.
/// ISP: Separate validator for the batch-specific concerns (array bounds).
/// </summary>
public class BatchTransferRequestValidator : AbstractValidator<BatchTransferRequest>
{
    public BatchTransferRequestValidator()
    {
        RuleFor(x => x.AbsLibraryItemIds)
            .NotEmpty().WithMessage("At least one item is required")
            .Must(ids => ids.Length <= 50).WithMessage("Maximum 50 items per batch");

        RuleForEach(x => x.AbsLibraryItemIds)
            .NotEmpty().WithMessage("Item ID cannot be empty");

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

/// <summary>
/// Phase 3: Validates settings update requests.
/// SRP: Only validates age range constraints, not business logic.
/// </summary>
public class UpdateSettingsRequestValidator : AbstractValidator<UpdateSettingsRequest>
{
    public UpdateSettingsRequestValidator()
    {
        RuleFor(x => x.DefaultMinAge)
            .InclusiveBetween(0, 18)
            .When(x => x.DefaultMinAge.HasValue);

        RuleFor(x => x.DefaultMaxAge)
            .InclusiveBetween(0, 18)
            .When(x => x.DefaultMaxAge.HasValue);

        RuleFor(x => x)
            .Must(x => !x.DefaultMinAge.HasValue || !x.DefaultMaxAge.HasValue ||
                       x.DefaultMinAge < x.DefaultMaxAge)
            .WithMessage("Min age must be less than max age");
    }
}
