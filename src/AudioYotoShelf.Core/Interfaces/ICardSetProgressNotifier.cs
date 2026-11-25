using AudioYotoShelf.Core.DTOs.CardSet;

namespace AudioYotoShelf.Core.Interfaces;

/// <summary>Tells the series page and the Transfers page that a set's cards changed state while it is being sent.</summary>
public interface ICardSetProgressNotifier
{
    Task CardChangedAsync(CardSetProgressUpdate update, CancellationToken ct = default);

    /// <summary>
    /// Tells the Transfers page that a set started or stopped sending, so it re-reads the list. It
    /// shares the book transfers' list-changed message: the page re-reads both on either.
    /// </summary>
    Task SendsChangedAsync(CancellationToken ct = default);
}
