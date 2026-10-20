using System;
using System.Collections.Generic;

namespace Realm.Core.Diagnostics;

/// <summary>
/// Statistics over frame times, for the traversal soak test (CLAUDE.md section 4.9): percentiles and a count of
/// hitches, meaning frames much longer than the typical one.
/// </summary>
/// <remarks>
/// Determinism: pure functions of the input. Thread-safety: stateless. Percentiles use the nearest-rank method:
/// the p-th percentile of n values is the value at 1-based rank ceil(p / 100 * n) in sorted order.
/// </remarks>
public static class FrameStats
{
    /// <summary>A frame is a hitch when it takes this many times the median frame time (and more than the absolute minimum).</summary>
    public const double DefaultHitchFactor = 2.0;

    /// <summary>A frame shorter than this many milliseconds is never a hitch, however much it exceeds a tiny median.</summary>
    public const double DefaultHitchMinimumMs = 4.0;

    /// <summary>Summarises frame times.</summary>
    /// <param name="frameTimesMs">Frame times in milliseconds; must contain at least one value.</param>
    /// <param name="hitchFactor">Multiple of the median above which a frame is a hitch.</param>
    /// <param name="hitchMinimumMs">Frames at or below this length are never hitches.</param>
    /// <returns>The summary.</returns>
    /// <exception cref="ArgumentException">The list is empty or contains a negative or non-finite value.</exception>
    public static FrameStatsSummary Summarise(
        IReadOnlyList<double> frameTimesMs,
        double hitchFactor = DefaultHitchFactor,
        double hitchMinimumMs = DefaultHitchMinimumMs)
    {
        ArgumentNullException.ThrowIfNull(frameTimesMs);
        if (frameTimesMs.Count == 0)
        {
            throw new ArgumentException("At least one frame time is required.", nameof(frameTimesMs));
        }

        var sorted = new double[frameTimesMs.Count];
        double sum = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            double value = frameTimesMs[i];
            if (!double.IsFinite(value) || value < 0)
            {
                throw new ArgumentException($"Frame time {i} is {value}, which is not a finite non-negative number.", nameof(frameTimesMs));
            }

            sorted[i] = value;
            sum += value;
        }

        Array.Sort(sorted);
        double median = Percentile(sorted, 50);
        double threshold = Math.Max(hitchMinimumMs, median * hitchFactor);
        int hitches = 0;
        foreach (double value in sorted)
        {
            if (value > threshold)
            {
                hitches++;
            }
        }

        return new FrameStatsSummary(
            sorted.Length,
            sum / sorted.Length,
            median,
            Percentile(sorted, 95),
            Percentile(sorted, 99),
            Percentile(sorted, 99.9),
            sorted[^1],
            hitches,
            threshold);
    }

    private static double Percentile(double[] sorted, double percent)
    {
        int rank = (int)Math.Ceiling(percent / 100.0 * sorted.Length);
        return sorted[Math.Clamp(rank, 1, sorted.Length) - 1];
    }
}
