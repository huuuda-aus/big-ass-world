using System;
using System.Collections.Generic;

namespace Realm.Gen.Terrain.Erosion;

/// <summary>
/// How wide a river is. A base that grows with the square root of its flow is changed along the river by: the slope (gentle reaches
/// are wide, steep ones narrow), a smooth noise (so no two stretches match), pools (wide, still stretches), a narrow source and, at the
/// end, an estuary where a river opens out as it reaches the sea, or a terminal pond where it dries up. One definition for the baker's
/// painted water, the road planner's crossings, the settlement water tests and the city bridges.
/// </summary>
/// <remarks>Determinism: pure (all variation is hashed from the river index and the distance along the river). Thread-safety: pure.</remarks>
public static class RiverWidth
{
    /// <summary>Half the width of the smallest river, in metres (3.5 times the old 2 m).</summary>
    public const double MinimumHalfWidthMeters = 7.0;

    /// <summary>Half width per square root of the flow in square kilometres (3.5 times the old 0.6).</summary>
    public const double HalfWidthPerRootKm2 = 2.1;

    /// <summary>Narrowest a river gets compared with its base width.</summary>
    public const double LeastFactor = 0.55;

    /// <summary>Widest a river gets compared with its base width.</summary>
    public const double MostFactor = 3.4;

    /// <summary>Slope (metres of fall per metre) at which the slope changes nothing.</summary>
    public const double ReferenceSlope = 0.012;

    /// <summary>How much wider the mouth is than the river above it where it reaches the sea (the estuary).</summary>
    public const double EstuaryFactor = 2.6;

    /// <summary>How much wider the end is where a river dries up in a pond.</summary>
    public const double PondFactor = 2.1;

    /// <summary>Longest estuary or pond, in metres along the river.</summary>
    public const double MaxMouthLengthMeters = 420;

    /// <summary>Length over which a river grows from its source, in metres.</summary>
    public const double SourceLengthMeters = 150;

    /// <summary>Largest change of width between neighbouring points, as a ratio.</summary>
    public const double MaxStep = 1.12;

    private const double NoiseLongMeters = 380;
    private const double NoiseShortMeters = 130;
    private const double NoiseLongAmplitude = 0.28;
    private const double NoiseShortAmplitude = 0.10;
    private const double PoolMeters = 650;
    private const double PoolThreshold = 0.35;
    private const double PoolExtra = 0.9;
    private const double GentleSlope = 0.03;
    private const double SlopeExponent = 0.35;
    private const double SlopeSoften = 0.003;
    private const double MinSlopeFactor = 0.7;
    private const double MaxSlopeFactor = 1.7;
    private const int SlopeWindowPoints = 3;
    private const int SmoothingPasses = 2;

    /// <summary>Half the width a river of this flow has before anything changes it, in metres.</summary>
    /// <param name="flowKm2">Flow in square kilometres of catchment.</param>
    /// <returns>At least <see cref="MinimumHalfWidthMeters"/>, growing with the square root of the flow.</returns>
    public static double BaseHalfWidthMeters(double flowKm2) =>
        Math.Max(MinimumHalfWidthMeters, HalfWidthPerRootKm2 * Math.Sqrt(Math.Max(0, flowKm2)));

    /// <summary>Gives every point of a river its width.</summary>
    /// <param name="points">The river's points from source to mouth (their heights give the slope).</param>
    /// <param name="riverIndex">Index of the river (each river varies differently).</param>
    /// <param name="mouth">Where the river ends: it opens into an estuary at the sea and into a pond where it dries up.</param>
    /// <returns>New points with <see cref="RiverPoint.HalfWidthMeters"/> set.</returns>
    public static List<RiverPoint> Apply(IReadOnlyList<RiverPoint> points, int riverIndex, RiverMouth mouth = RiverMouth.Confluence)
    {
        int n = points.Count;
        var along = new double[n];
        for (int i = 1; i < n; i++)
        {
            along[i] = along[i - 1] + Math.Sqrt(((points[i].X - points[i - 1].X) * (points[i].X - points[i - 1].X)) + ((points[i].Z - points[i - 1].Z) * (points[i].Z - points[i - 1].Z)));
        }

        double total = n == 0 ? 0 : along[n - 1];
        double mouthLength = Math.Min(MaxMouthLengthMeters, total * 0.3);
        double mouthFactor = mouth switch
        {
            RiverMouth.Sea => EstuaryFactor,
            RiverMouth.Dried => PondFactor,
            _ => 1.0,
        };

        var factor = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = along[i];
            double noise = 1 + (NoiseLongAmplitude * Noise(s, riverIndex, NoiseLongMeters, 1)) + (NoiseShortAmplitude * Noise(s, riverIndex, NoiseShortMeters, 2));
            double slope = SlopeAt(points, along, i);
            double slopeFactor = Math.Clamp(Math.Pow(ReferenceSlope / (slope + SlopeSoften), SlopeExponent), MinSlopeFactor, MaxSlopeFactor);

            // Pools: where the river is gentle, now and then it opens into a wide, still stretch.
            double gentle = Math.Clamp(1 - (slope / GentleSlope), 0, 1);
            double pool = 1 + (PoolExtra * gentle * Smooth((Noise(s, riverIndex, PoolMeters, 3) - PoolThreshold) / (1 - PoolThreshold)));

            double source = 0.6 + (0.4 * Smooth(s / SourceLengthMeters));
            double end = mouthLength > 1 && mouthFactor > 1 ? 1 + ((mouthFactor - 1) * Smooth((s - (total - mouthLength)) / mouthLength)) : 1;
            factor[i] = Math.Clamp(noise * slopeFactor * pool * source * end, LeastFactor, MostFactor);
        }

        Smoothen(factor);
        var result = new List<RiverPoint>(n);
        for (int i = 0; i < n; i++)
        {
            result.Add(points[i] with { HalfWidthMeters = BaseHalfWidthMeters(points[i].FlowKm2) * factor[i] });
        }

        return result;
    }

    // Local fall of the river (metres per metre) over a few points either side, never negative.
    private static double SlopeAt(IReadOnlyList<RiverPoint> points, double[] along, int i)
    {
        int a = Math.Max(0, i - SlopeWindowPoints), b = Math.Min(points.Count - 1, i + SlopeWindowPoints);
        double run = along[b] - along[a];
        return run <= 0 ? 0 : Math.Max(0, (points[a].Height - points[b].Height) / run);
    }

    // Averages neighbours a couple of times, then stops any step bigger than MaxStep, forwards and backwards, so the width never jumps.
    private static void Smoothen(double[] factor)
    {
        for (int pass = 0; pass < SmoothingPasses; pass++)
        {
            var copy = (double[])factor.Clone();
            for (int i = 0; i < factor.Length; i++)
            {
                double sum = copy[i], count = 1;
                if (i > 0) { sum += copy[i - 1]; count++; }
                if (i + 1 < factor.Length) { sum += copy[i + 1]; count++; }
                factor[i] = sum / count;
            }
        }

        for (int i = 1; i < factor.Length; i++)
        {
            factor[i] = Math.Clamp(factor[i], factor[i - 1] / MaxStep, factor[i - 1] * MaxStep);
        }

        for (int i = factor.Length - 2; i >= 0; i--)
        {
            factor[i] = Math.Clamp(factor[i], factor[i + 1] / MaxStep, factor[i + 1] * MaxStep);
        }
    }

    private static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - (2 * t));
    }

    // Smooth value noise in [-1, 1] along the river: random values at every wavelength, blended with a smoothstep.
    private static double Noise(double s, int river, double wavelengthMeters, int channel)
    {
        double x = s / wavelengthMeters;
        int i0 = (int)Math.Floor(x);
        double t = Smooth(x - i0);
        return (Hash(river, i0, channel) * (1 - t)) + (Hash(river, i0 + 1, channel) * t);
    }

    private static double Hash(int river, int cell, int channel)
    {
        unchecked
        {
            uint h = (uint)((river * 73856093) ^ (cell * 19349663) ^ (channel * 83492791));
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            h *= 3266489917u;
            h ^= h >> 16;
            return ((h / (double)uint.MaxValue) * 2) - 1;
        }
    }
}
