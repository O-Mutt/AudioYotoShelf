namespace AudioYotoShelf.Core.Configuration;

/// <summary>
/// Yoto MYO card capacity limits. Bound from configuration section "Yoto:CardLimits";
/// defaults match the documented MYO constraints. Kept config-driven so limits can be
/// tuned without a code change if Yoto adjusts them.
/// </summary>
public class YotoCardLimits
{
    public const string SectionName = "Yoto:CardLimits";

    public int MaxTracks { get; set; } = 100;
    public long MaxTrackBytes { get; set; } = 100L * 1024 * 1024;     // 100 MB
    public double MaxTrackDurationSeconds { get; set; } = 3600;        // 1 hour
    public long MaxTotalBytes { get; set; } = 500L * 1024 * 1024;     // 500 MB
    public double MaxTotalDurationSeconds { get; set; } = 18000;       // 5 hours
}
