using AudioYotoShelf.Core.Enums;

namespace AudioYotoShelf.Core.Entities;

/// <summary>
/// A series packed onto an ordered set of Yoto MYO cards. Each card is a <see cref="Playlist"/>
/// with <see cref="Playlist.CardSetId"/> and <see cref="Playlist.SetIndex"/> set. See
/// docs/specs/series-card-sets.md.
/// </summary>
public class CardSet : BaseEntity
{
    public Guid UserConnectionId { get; set; }
    public UserConnection UserConnection { get; set; } = null!;

    public required string AbsSeriesId { get; set; }
    public required string AbsLibraryId { get; set; }
    public required string Name { get; set; }
    public PackingMode PackingMode { get; set; } = PackingMode.Balance;

    /// <summary>Series books the user left out, such as a second edition of the same book.</summary>
    public List<string> ExcludedItemIds { get; set; } = [];

    public ICollection<Playlist> Cards { get; set; } = [];
}
