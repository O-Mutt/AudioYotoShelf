namespace AudioYotoShelf.Core.Entities;

/// <summary>
/// Stores a user's connections to Audiobookshelf and Yoto.
/// ABS token is obtained via login delegation; Yoto tokens via OAuth authorization code flow.
/// </summary>
public class UserConnection : BaseEntity
{
    public required string Username { get; set; }

    // Audiobookshelf connection
    public required string AudiobookshelfUrl { get; set; }
    public string? AudiobookshelfToken { get; set; }
    public DateTimeOffset? AudiobookshelfTokenValidatedAt { get; set; }

    // Yoto OAuth connection
    public string? YotoAccessToken { get; set; }
    public string? YotoRefreshToken { get; set; }
    public DateTimeOffset? YotoTokenExpiresAt { get; set; }
    /// <summary>Stores OAuth state nonce during authorization code flow.</summary>
    public string? YotoDeviceCode { get; set; }

    // Preferences
    public string? DefaultLibraryId { get; set; }
    public int DefaultMinAge { get; set; } = 5;
    public int DefaultMaxAge { get; set; } = 10;

    // Navigation
    public ICollection<CardTransfer> CardTransfers { get; set; } = [];
    public ICollection<GeneratedIcon> GeneratedIcons { get; set; } = [];

    public bool HasValidAbsConnection =>
        !string.IsNullOrEmpty(AudiobookshelfToken) &&
        AudiobookshelfTokenValidatedAt.HasValue;

    public bool HasValidYotoConnection =>
        !string.IsNullOrEmpty(YotoAccessToken) &&
        YotoTokenExpiresAt.HasValue &&
        YotoTokenExpiresAt > DateTimeOffset.UtcNow;
}
