using AudioYotoShelf.Core.DTOs.Audiobookshelf;
using AudioYotoShelf.Core.DTOs.Transfer;

namespace AudioYotoShelf.Core.Interfaces;

/// <summary>
/// Suggests age ranges for audiobooks based on metadata signals.
/// Uses genre, keywords, duration, and other heuristics.
/// </summary>
public interface IAgeSuggestionService
{
    AgeSuggestionResponse SuggestAgeRange(AbsBookMetadata metadata, double durationSeconds, int chapterCount);
}
