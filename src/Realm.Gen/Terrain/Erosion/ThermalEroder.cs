using System;
using System.Threading.Tasks;

namespace Realm.Gen.Terrain.Erosion;

/// <summary>
/// Thermal weathering: wherever the slope between neighbouring vertices exceeds the local stable slope, a
/// share of the excess slumps from the higher vertex to the lower one. Mass is conserved.
/// </summary>
/// <remarks>
/// Each iteration reads the old heights and writes new ones (Jacobi style), and the transfer for a pair of
/// vertices depends only on that pair, so rows can run in parallel without changing the result.
/// Determinism: no randomness; fixed iteration structure. Thread-safety: stateless.
/// </remarks>
internal static class ThermalEroder
{
    /// <summary>Share of the excess slope moved per iteration; below 1/8 so a vertex with eight lower neighbours cannot overshoot.</summary>
    public const double TransferShare = 0.1;

    private const double DiagonalFactor = 1.4142135623730951;

    private static readonly (int Dx, int Dz, double Distance)[] Neighbours =
    {
        (-1, -1, DiagonalFactor), (0, -1, 1), (1, -1, DiagonalFactor), (-1, 0, 1),
        (1, 0, 1), (-1, 1, DiagonalFactor), (0, 1, 1), (1, 1, DiagonalFactor),
    };

    /// <summary>Relaxes the heights toward the stable slope.</summary>
    /// <param name="heights">Heights row by row; not modified.</param>
    /// <param name="side">Vertices per side.</param>
    /// <param name="spacing">Metres between neighbouring vertices.</param>
    /// <param name="stableSlope">Per-vertex stable slope as rise over run (tangent of the angle).</param>
    /// <param name="iterations">Number of relaxation passes.</param>
    /// <returns>The relaxed heights.</returns>
    public static float[] Relax(float[] heights, int side, double spacing, double[] stableSlope, int iterations)
    {
        float[] current = (float[])heights.Clone();
        var next = new float[current.Length];

        for (int pass = 0; pass < iterations; pass++)
        {
            float[] source = current;
            Parallel.For(0, side, z =>
            {
                for (int x = 0; x < side; x++)
                {
                    int i = (z * side) + x;
                    double delta = 0;
                    foreach ((int dx, int dz, double factor) in Neighbours)
                    {
                        int nx = x + dx;
                        int nz = z + dz;
                        if ((uint)nx >= (uint)side || (uint)nz >= (uint)side)
                        {
                            continue;
                        }

                        int j = (nz * side) + nx;
                        double distance = spacing * factor;
                        double allowed = 0.5 * (stableSlope[i] + stableSlope[j]) * distance;
                        double drop = (double)source[i] - source[j];
                        double excess = Math.Abs(drop) - allowed;
                        if (excess > 0)
                        {
                            delta += drop > 0 ? -TransferShare * excess : TransferShare * excess;
                        }
                    }

                    next[i] = (float)(source[i] + delta);
                }
            });

            (current, next) = (next, current);
        }

        return current;
    }
}
