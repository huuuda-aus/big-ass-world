namespace Realm.Core.Diagnostics;

/// <summary>Summary of a run of frame times, as computed by <see cref="FrameStats"/>.</summary>
/// <param name="Count">Number of frames.</param>
/// <param name="MeanMs">Average frame time in milliseconds.</param>
/// <param name="P50Ms">Median frame time (nearest rank).</param>
/// <param name="P95Ms">95th percentile frame time (nearest rank).</param>
/// <param name="P99Ms">99th percentile frame time (nearest rank).</param>
/// <param name="P999Ms">99.9th percentile frame time (nearest rank).</param>
/// <param name="MaxMs">Longest frame time.</param>
/// <param name="HitchCount">Frames counted as hitches (see <see cref="FrameStats.Summarise"/>).</param>
/// <param name="HitchThresholdMs">The frame time above which a frame counted as a hitch.</param>
public readonly record struct FrameStatsSummary(
    int Count,
    double MeanMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double P999Ms,
    double MaxMs,
    int HitchCount,
    double HitchThresholdMs);
