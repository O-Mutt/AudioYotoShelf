using AudioYotoShelf.Core.DTOs.Icons;
using AudioYotoShelf.Core.DTOs.Yoto;

namespace AudioYotoShelf.Core.Interfaces;

/// <summary>
/// Generates 16x16 pixel art icons for Yoto card chapters.
/// Supports AI generation via Gemini, public library search, and cover art conversion.
/// </summary>
public interface IIconGenerationService
{
    /// <summary>
    /// Generate a 16x16 pixel art icon from a text prompt using Gemini.
    /// </summary>
    Task<byte[]> GenerateIconAsync(string prompt, CancellationToken ct = default);

    /// <summary>
    /// Generate an icon specifically for a book chapter, using contextual information.
    /// </summary>
    Task<byte[]> GenerateChapterIconAsync(string chapterTitle, string bookTitle, string? genre, CancellationToken ct = default);

    /// <summary>
    /// Convert an existing cover image to a 16x16 pixel art icon.
    /// </summary>
    Task<byte[]> ConvertCoverToIconAsync(Stream coverImage, CancellationToken ct = default);

    /// <summary>
    /// Search the Yoto public icon library for matching icons.
    /// </summary>
    Task<YotoPublicIcon[]> SearchPublicIconsAsync(string query, int maxResults = 10, CancellationToken ct = default);

    /// <summary>
    /// Build a Gemini prompt for a chapter icon based on metadata.
    /// </summary>
    string BuildChapterIconPrompt(string chapterTitle, string bookTitle, string? genre);
}
