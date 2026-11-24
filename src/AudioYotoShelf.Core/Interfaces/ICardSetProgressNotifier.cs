using AudioYotoShelf.Core.DTOs.CardSet;

namespace AudioYotoShelf.Core.Interfaces;

/// <summary>Tells a series page that a card of its set changed state while the set is being sent.</summary>
public interface ICardSetProgressNotifier
{
    Task CardChangedAsync(CardSetProgressUpdate update, CancellationToken ct = default);
}
