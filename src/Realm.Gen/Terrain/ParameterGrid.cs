using System;

namespace Realm.Gen.Terrain;

/// <summary>
/// A square grid of one scalar value per macro cell, with a separable box blur and smooth sampling. The
/// height-field step uses three of them (base height, relief, roughness) so biome borders blend smoothly.
/// </summary>
/// <remarks>Determinism: double arithmetic in fixed order. Thread-safety: not thread-safe while blurring; read-only afterwards.</remarks>
internal sealed class ParameterGrid
{
    private const double CellCentreOffset = 0.5;
    private const double HalfDivisor = 2.0;
    private const int BlurPasses = 2;

    private double[] _values;

    public ParameterGrid(int side, int cellMeters, double[] values)
    {
        Side = side;
        CellMeters = cellMeters;
        _values = values;
    }

    /// <summary>
    /// Blur radius per pass, in macro cells, so that two passes spread a step about <paramref name="blendMeters"/>
    /// to each side (rounded up to whole cells; 0 means no blur).
    /// </summary>
    public static int PassRadiusCells(int blendMeters, int cellMeters) =>
        blendMeters <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(blendMeters / (double)cellMeters / BlurPasses));

    public int Side { get; }

    public int CellMeters { get; }

    public ReadOnlySpan<double> Values => _values;

    /// <summary>
    /// Blurs with two passes of a box filter of the given radius in cells (approximates a gaussian). The
    /// passes add up: a step is spread over about twice this radius to each side.
    /// </summary>
    public void Blur(int radiusCells)
    {
        if (radiusCells <= 0)
        {
            return;
        }

        var scratch = new double[_values.Length];
        for (int pass = 0; pass < BlurPasses; pass++)
        {
            BoxBlurRows(_values, scratch, radiusCells);
            BoxBlurColumns(scratch, _values, radiusCells);
        }
    }

    /// <summary>Bilinear value at a world position (metres from the world centre); clamped at the edges.</summary>
    public double Sample(double x, double z)
    {
        double half = Side * (double)CellMeters / HalfDivisor;
        double u = Math.Clamp(((x + half) / CellMeters) - CellCentreOffset, 0, Side - 1);
        double v = Math.Clamp(((z + half) / CellMeters) - CellCentreOffset, 0, Side - 1);
        int u0 = Math.Min((int)Math.Floor(u), Side - 2 < 0 ? 0 : Side - 2);
        int v0 = Math.Min((int)Math.Floor(v), Side - 2 < 0 ? 0 : Side - 2);
        int u1 = Math.Min(u0 + 1, Side - 1);
        int v1 = Math.Min(v0 + 1, Side - 1);
        double tu = u - u0;
        double tv = v - v0;

        double a = _values[(v0 * Side) + u0];
        double b = _values[(v0 * Side) + u1];
        double c = _values[(v1 * Side) + u0];
        double d = _values[(v1 * Side) + u1];
        double top = a + (tu * (b - a));
        double bottom = c + (tu * (d - c));
        return top + (tv * (bottom - top));
    }

    private void BoxBlurRows(double[] source, double[] target, int radius)
    {
        for (int row = 0; row < Side; row++)
        {
            for (int col = 0; col < Side; col++)
            {
                double sum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    sum += source[(row * Side) + Math.Clamp(col + k, 0, Side - 1)];
                }

                target[(row * Side) + col] = sum / ((2 * radius) + 1);
            }
        }
    }

    private void BoxBlurColumns(double[] source, double[] target, int radius)
    {
        for (int row = 0; row < Side; row++)
        {
            for (int col = 0; col < Side; col++)
            {
                double sum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    sum += source[(Math.Clamp(row + k, 0, Side - 1) * Side) + col];
                }

                target[(row * Side) + col] = sum / ((2 * radius) + 1);
            }
        }
    }
}
