using AudioYotoShelf.Core.Enums;

namespace AudioYotoShelf.Core.Entities;

/// <summary>
/// Caches generated or selected icons to avoid regenerating for the same content.
/// Icons are 16x16 pixel art for the Yoto player LED display.
/// </summary>
public class GeneratedIcon : BaseEntity
{
    public Guid UserConnectionId { get; set; }
    public UserConnection UserConnection { get; set; } = null!;

    // What this icon represents
    public required string Prompt { get; set; }
    public required string ContextTitle { get; set; }
    public bool IsBookCover { get; set; }

    // Source and storage
    public IconSource Source { get; set; }
    public string? YotoMediaId { get; set; }
    public string? YotoIconUrl { get; set; }
    public string? PublicIconId { get; set; }
    public byte[]? IconData { get; set; }

    // Reuse tracking
    public string? ContentHash { get; set; }
    public int TimesUsed { get; set; }
}
