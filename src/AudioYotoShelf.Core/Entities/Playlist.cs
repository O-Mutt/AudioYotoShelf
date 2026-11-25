using AudioYotoShelf.Core.Enums;

namespace AudioYotoShelf.Core.Entities;

/// <summary>
/// A user-curated, ordered collection of books destined for a single Yoto MYO card.
/// Capacity (100 tracks / 500 MB / 5 hr) is validated before transfer.
/// </summary>
public class Playlist : BaseEntity
{
    public Guid UserConnectionId { get; set; }
    public UserConnection UserConnection { get; set; } = null!;

    public required string Name { get; set; }
    public PlaylistStatus Status { get; set; } = PlaylistStatus.Draft;

    /// <summary>Default track-grouping policy applied to items without an explicit override.</summary>
    public TrackGrouping DefaultGrouping { get; set; } = TrackGrouping.Auto;

    /// <summary>Set once the playlist has been transferred to a card.</summary>
    public string? YotoCardId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? TransferredAt { get; set; }

    /// <summary>
    /// When <see cref="Status"/> last changed. Set on save, so it tracks sending only; renaming or
    /// editing a card moves <see cref="BaseEntity.UpdatedAt"/> but not this.
    /// </summary>
    public DateTimeOffset? StatusChangedAt { get; set; }

    /// <summary>Set when this playlist is one card of a series card set.</summary>
    public Guid? CardSetId { get; set; }
    public CardSet? CardSet { get; set; }

    /// <summary>0-based position of this card within its card set.</summary>
    public int? SetIndex { get; set; }

    public ICollection<PlaylistItem> Items { get; set; } = [];
}
