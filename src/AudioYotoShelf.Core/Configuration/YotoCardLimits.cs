namespace AudioYotoShelf.Core.Configuration;

/// <summary>
/// Yoto MYO card capacity limits. Bound from configuration section "Yoto:CardLimits".
/// Kept config-driven so limits can be tuned without a code change if Yoto adjusts them.
/// Defaults follow what Yoto actually accepts, which differs from its support article
/// ("500 MB / 5 hours" per card); see docs/specs/yoto-card-limits.md.
/// </summary>
public class YotoCardLimits
{
    public const string SectionName = "Yoto:CardLimits";

    public int MaxTracks { get; set; } = 100;

    /// <summary>Per-track cap, checked against the file we upload (untested against Yoto, so kept conservative).</summary>
    public long MaxTrackBytes { get; set; } = 100L * 1024 * 1024;     // 100 MB

    public double MaxTrackDurationSeconds { get; set; } = 3600;        // 1 hour

    /// <summary>
    /// Card total, checked against Yoto's transcoded size rather than the source files: Yoto
    /// accepted a card uploaded as 629 MB of source audio that it stored as 476 MB of opus.
    /// </summary>
    public long MaxTotalBytes { get; set; } = 500L * 1024 * 1024;     // 500 MB

    /// <summary>
    /// Optional total-duration cap; null means not enforced. Yoto documents 5 hours but accepts
    /// and plays cards of 9.6 h and 11.5 h, so the byte cap is what actually binds.
    /// </summary>
    public double? MaxTotalDurationSeconds { get; set; }

    /// <summary>
    /// Bitrate used to estimate Yoto's transcoded size. Measured 73–96 kbps opus on real cards;
    /// 100 keeps the estimate slightly pessimistic so a card that passes here fits on Yoto.
    /// </summary>
    public int TranscodedBitrateKbps { get; set; } = 100;

    public long EstimateTranscodedBytes(double durationSeconds) =>
        (long)(durationSeconds * TranscodedBitrateKbps * 1000 / 8);
}
