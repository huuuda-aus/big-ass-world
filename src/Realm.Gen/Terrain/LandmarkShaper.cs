using System;
using System.Collections.Generic;

namespace Realm.Gen.Terrain;

/// <summary>Adds the shapes of placed landmarks to a height array.</summary>
/// <remarks>
/// Determinism: landmarks are applied one after another in list order; each vertex sum is plain double arithmetic.
/// Thread-safety: not safe to call concurrently on the same array.
/// </remarks>
public static class LandmarkShaper
{
    private const double RidgeWavelengthPerRadius = 0.7; // ridge crests repeat about every 0.7 radii
    private const double OutlineWarpPerRadius = 0.25;
    private const double OutlineWavelengthPerRadius = 1.1;
    private const double MountainBase = 0.30;
    private const double MountainRidge = 0.70;
    private const double HillBump = 0.12;
    private const int RidgeOctaves = 4;
    private const double RidgeLacunarity = 2.1;
    private const double RidgeGain = 0.5;
    private const double BasinFlatness = 1.5;

    /// <summary>The height a landmark adds at a world position.</summary>
    /// <param name="landmark">The landmark.</param>
    /// <param name="noise">Shape noise (ridges and outline).</param>
    /// <param name="x">World X in metres.</param>
    /// <param name="z">World Z in metres.</param>
    /// <returns>Metres to add (negative for a basin); 0 outside its footprint.</returns>
    public static double DeltaAt(Landmark landmark, GradientNoise noise, double x, double z)
    {
        ArgumentNullException.ThrowIfNull(noise);
        double r = landmark.RadiusMeters;
        double dx = x - landmark.X;
        double dz = z - landmark.Z;
        if ((dx * dx) + (dz * dz) >= r * r * 4)
        {
            return 0; // beyond the largest possible warped footprint
        }

        // A warped outline stops mountains and hills being perfect circles.
        double w = OutlineWarpPerRadius * r;
        double wl = OutlineWavelengthPerRadius * r;
        double wx = noise.Sample((x / wl) + 17.3, (z / wl) + 5.1) * w;
        double wz = noise.Sample((x / wl) + 91.7, (z / wl) + 44.9) * w;
        double u2 = (((dx + wx) * (dx + wx)) + ((dz + wz) * (dz + wz))) / (r * r);
        if (u2 >= 1)
        {
            return 0;
        }

        double envelope = (1 - u2) * (1 - u2);
        switch (landmark.Kind)
        {
            case LandmarkKind.Hill:
                return landmark.HeightMeters * envelope * (1 + (HillBump * noise.Sample(x / (r / 3), z / (r / 3))));
            case LandmarkKind.Mountain:
                double ridge = noise.Ridged(x / (r * RidgeWavelengthPerRadius), z / (r * RidgeWavelengthPerRadius), RidgeOctaves, RidgeLacunarity, RidgeGain);
                return landmark.HeightMeters * envelope * (MountainBase + (MountainRidge * ridge));
            case LandmarkKind.Basin:
                return -landmark.HeightMeters * Math.Min(1.0, BasinFlatness * envelope);
            default:
                throw new ArgumentOutOfRangeException(nameof(landmark), landmark.Kind, "Unknown landmark kind.");
        }
    }

    /// <summary>Adds every landmark to the heights.</summary>
    /// <param name="heights">Heights, row by row, <paramref name="side"/> x <paramref name="side"/> values.</param>
    /// <param name="side">Vertices per side.</param>
    /// <param name="spacingMeters">Metres between vertices.</param>
    /// <param name="worldSizeMeters">World side length in metres (centred on the origin).</param>
    /// <param name="landmarks">The landmarks, applied in order.</param>
    /// <param name="noise">Shape noise.</param>
    public static void Apply(float[] heights, int side, int spacingMeters, int worldSizeMeters, IReadOnlyList<Landmark> landmarks, GradientNoise noise)
    {
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(landmarks);
        double half = worldSizeMeters / 2.0;
        foreach (Landmark landmark in landmarks)
        {
            double reach = landmark.RadiusMeters * 2;
            int col0 = Math.Max(0, (int)Math.Floor((landmark.X - reach + half) / spacingMeters));
            int col1 = Math.Min(side - 1, (int)Math.Ceiling((landmark.X + reach + half) / spacingMeters));
            int row0 = Math.Max(0, (int)Math.Floor((landmark.Z - reach + half) / spacingMeters));
            int row1 = Math.Min(side - 1, (int)Math.Ceiling((landmark.Z + reach + half) / spacingMeters));
            for (int row = row0; row <= row1; row++)
            {
                double z = -half + (row * (double)spacingMeters);
                for (int col = col0; col <= col1; col++)
                {
                    double x = -half + (col * (double)spacingMeters);
                    heights[(row * side) + col] += (float)DeltaAt(landmark, noise, x, z);
                }
            }
        }
    }
}
