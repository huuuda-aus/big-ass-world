using System;
using System.Linq;
using Realm.Core.Diagnostics;
using Xunit;

namespace Realm.Core.Tests;

public class FrameStatsTests
{
    [Fact]
    public void Percentiles_UseNearestRank()
    {
        // 1..100: the p-th percentile is exactly p under nearest-rank.
        double[] times = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();

        FrameStatsSummary s = FrameStats.Summarise(times);

        Assert.Equal(100, s.Count);
        Assert.Equal(50.5, s.MeanMs);
        Assert.Equal(50, s.P50Ms);
        Assert.Equal(95, s.P95Ms);
        Assert.Equal(99, s.P99Ms);
        Assert.Equal(100, s.P999Ms);
        Assert.Equal(100, s.MaxMs);
    }

    [Fact]
    public void InputOrder_DoesNotMatter()
    {
        double[] ordered = { 5, 1, 4, 2, 3, 9, 7, 8, 6, 10 };
        double[] reversed = ordered.Reverse().ToArray();

        Assert.Equal(FrameStats.Summarise(ordered), FrameStats.Summarise(reversed));
    }

    [Fact]
    public void SingleFrame_SummarisesToItself()
    {
        FrameStatsSummary s = FrameStats.Summarise(new[] { 16.7 });

        Assert.Equal(1, s.Count);
        Assert.Equal(16.7, s.P50Ms);
        Assert.Equal(16.7, s.P999Ms);
        Assert.Equal(16.7, s.MaxMs);
        Assert.Equal(0, s.HitchCount);
    }

    [Fact]
    public void Hitches_AreFramesBeyondTwiceTheMedian_ByDefault()
    {
        // 97 smooth frames at 10 ms and three spikes.
        double[] times = Enumerable.Repeat(10.0, 97).Concat(new[] { 25.0, 40.0, 19.9 }).ToArray();

        FrameStatsSummary s = FrameStats.Summarise(times);

        Assert.Equal(10, s.P50Ms);
        Assert.Equal(20, s.HitchThresholdMs);
        Assert.Equal(2, s.HitchCount); // 25 and 40; 19.9 is under the 20 ms threshold
    }

    [Fact]
    public void TinyMedians_DoNotMakeEveryFrameAHitch()
    {
        // Median 1 ms: twice the median is 2 ms, but the absolute minimum (4 ms) governs.
        double[] times = Enumerable.Repeat(1.0, 98).Concat(new[] { 3.0, 5.0 }).ToArray();

        FrameStatsSummary s = FrameStats.Summarise(times);

        Assert.Equal(4.0, s.HitchThresholdMs);
        Assert.Equal(1, s.HitchCount);
    }

    [Fact]
    public void HitchRules_AreConfigurable()
    {
        double[] times = Enumerable.Repeat(10.0, 9).Concat(new[] { 14.0 }).ToArray();

        Assert.Equal(0, FrameStats.Summarise(times).HitchCount);
        Assert.Equal(1, FrameStats.Summarise(times, hitchFactor: 1.2, hitchMinimumMs: 0).HitchCount);
    }

    [Fact]
    public void BadInput_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => FrameStats.Summarise(Array.Empty<double>()));
        Assert.Throws<ArgumentException>(() => FrameStats.Summarise(new[] { 1.0, -1.0 }));
        Assert.Throws<ArgumentException>(() => FrameStats.Summarise(new[] { 1.0, double.NaN }));
        Assert.Throws<ArgumentException>(() => FrameStats.Summarise(new[] { double.PositiveInfinity }));
        Assert.Throws<ArgumentNullException>(() => FrameStats.Summarise(null!));
    }
}
