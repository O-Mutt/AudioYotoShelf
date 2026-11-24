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

    /// <summary>
    /// Per-track cap, checked against the file we upload. Yoto accepted 265 MB uploads, so this is
    /// stricter than Yoto; it stays because the transfer splits tracks by the same rule (#121).
    /// </summary>
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
    /// Bitrate used to estimate Yoto's transcoded size. Measured 65–96 kbps opus on real cards, and
    /// Yoto's bitrate does not follow the source's, so this is a best guess with a little headroom,
    /// not a guarantee that Yoto stores less.
    /// </summary>
    public int TranscodedBitrateKbps { get; set; } = 100;

    public long EstimateTranscodedBytes(double durationSeconds) =>
        (long)(durationSeconds * TranscodedBitrateKbps * 1000 / 8);

    /// <summary>
    /// Fails startup on settings that would silently switch a check off: a bitrate of 0 makes every
    /// card weigh nothing, and a zero or negative cap would refuse every card.
    /// </summary>
    public void Validate()
    {
        var problems = new List<string>();
        if (TranscodedBitrateKbps <= 0)
            problems.Add($"{nameof(TranscodedBitrateKbps)} must be greater than 0 (got {TranscodedBitrateKbps}).");
        if (MaxTracks <= 0)
            problems.Add($"{nameof(MaxTracks)} must be greater than 0 (got {MaxTracks}).");
        if (MaxTrackBytes <= 0)
            problems.Add($"{nameof(MaxTrackBytes)} must be greater than 0 (got {MaxTrackBytes}).");
        if (MaxTrackDurationSeconds <= 0)
            problems.Add($"{nameof(MaxTrackDurationSeconds)} must be greater than 0 (got {MaxTrackDurationSeconds}).");
        if (MaxTotalBytes <= 0)
            problems.Add($"{nameof(MaxTotalBytes)} must be greater than 0 (got {MaxTotalBytes}).");
        if (MaxTotalDurationSeconds is <= 0)
            problems.Add($"{nameof(MaxTotalDurationSeconds)} must be unset or greater than 0 (got {MaxTotalDurationSeconds}).");

        if (problems.Count > 0)
            throw new InvalidOperationException($"Invalid {SectionName} settings: {string.Join(" ", problems)}");
    }
}
