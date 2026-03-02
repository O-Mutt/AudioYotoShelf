using AudioYotoShelf.Core.Enums;

namespace AudioYotoShelf.Core.Entities;

/// <summary>
/// Tracks the transfer of an audiobook from Audiobookshelf to a Yoto MYO card.
/// Preserves both suggested and overridden age ranges for audit/improvement.
/// </summary>
public class CardTransfer : BaseEntity
{
    public Guid UserConnectionId { get; set; }
    public UserConnection UserConnection { get; set; } = null!;

    // Audiobookshelf source
    public required string AbsLibraryItemId { get; set; }
    public required string BookTitle { get; set; }
    public string? BookAuthor { get; set; }
    public string? SeriesName { get; set; }
    public float? SeriesSequence { get; set; }

    // Yoto target
    public string? YotoCardId { get; set; }
    public YotoCategory Category { get; set; } = YotoCategory.Stories;
    public PlaybackType PlaybackType { get; set; } = PlaybackType.Linear;

    // Age range - suggested by system
    public int SuggestedMinAge { get; set; }
    public int SuggestedMaxAge { get; set; }
    public required string AgeSuggestionReason { get; set; }
    public AgeRangeSource AgeSuggestionSource { get; set; }

    // Age range - user override (null = accepted suggestion)
    public int? OverrideMinAge { get; set; }
    public int? OverrideMaxAge { get; set; }

    public int EffectiveMinAge => OverrideMinAge ?? SuggestedMinAge;
    public int EffectiveMaxAge => OverrideMaxAge ?? SuggestedMaxAge;

    // Transfer state
    public TransferStatus Status { get; set; } = TransferStatus.Pending;
    public int ProgressPercent { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    // Navigation
    public ICollection<TrackMapping> TrackMappings { get; set; } = [];
}
