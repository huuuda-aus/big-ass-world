using System;
using Realm.Core.Random;

namespace Realm.Gen.Terrain;

/// <summary>
/// Deterministic 2D gradient (Perlin-style) noise with fractal summation. The lattice permutation comes
/// from <see cref="Rng"/>, so the same seed gives the same terrain on every machine.
/// </summary>
/// <remarks>
/// Determinism: only IEEE double add/multiply/floor on inputs; no randomness after construction.
/// Thread-safety: immutable after construction, safe to call from several threads.
/// </remarks>
public sealed class GradientNoise
{
    private const int TableSize = 256;
    private const int TableMask = TableSize - 1;
    private const double NormaliseToUnit = 1.4142135623730951; // 2D gradient noise peaks at sqrt(2)/2
    private const double FadeA = 6.0;
    private const double FadeB = 15.0;
    private const double FadeC = 10.0;

    private static readonly (double X, double Z)[] Gradients =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1),
        (0.7071067811865476, 0.7071067811865476), (-0.7071067811865476, 0.7071067811865476),
        (0.7071067811865476, -0.7071067811865476), (-0.7071067811865476, -0.7071067811865476),
    };

    private readonly byte[] _permutation = new byte[TableSize * 2];

    /// <summary>Creates noise from a generator; the generator is advanced (it shuffles the lattice table).</summary>
    /// <param name="rng">Source of randomness; derive it with <see cref="Rng.ForKey"/>.</param>
    public GradientNoise(Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        var table = new byte[TableSize];
        for (int i = 0; i < TableSize; i++)
        {
            table[i] = (byte)i;
        }

        for (int i = TableSize - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (table[i], table[j]) = (table[j], table[i]);
        }

        for (int i = 0; i < _permutation.Length; i++)
        {
            _permutation[i] = table[i & TableMask];
        }
    }

    /// <summary>Samples one octave of noise.</summary>
    /// <param name="x">Lattice-space X (any finite value).</param>
    /// <param name="z">Lattice-space Z (any finite value).</param>
    /// <returns>A smooth value in roughly [-1, 1]; exactly 0 at integer lattice points.</returns>
    public double Sample(double x, double z)
    {
        double fx = Math.Floor(x);
        double fz = Math.Floor(z);
        int ix = (int)(long)fx & TableMask;
        int iz = (int)(long)fz & TableMask;
        double dx = x - fx;
        double dz = z - fz;

        double n00 = Dot(ix, iz, dx, dz);
        double n10 = Dot(ix + 1, iz, dx - 1, dz);
        double n01 = Dot(ix, iz + 1, dx, dz - 1);
        double n11 = Dot(ix + 1, iz + 1, dx - 1, dz - 1);

        double u = Fade(dx);
        double v = Fade(dz);
        double top = n00 + (u * (n10 - n00));
        double bottom = n01 + (u * (n11 - n01));
        return (top + (v * (bottom - top))) * NormaliseToUnit;
    }

    /// <summary>Sums octaves of noise, each at a higher frequency and a lower amplitude.</summary>
    /// <param name="x">Lattice-space X of the first octave.</param>
    /// <param name="z">Lattice-space Z of the first octave.</param>
    /// <param name="octaves">Number of octaves; at least 1.</param>
    /// <param name="lacunarity">Frequency multiplier per octave; usually 2.</param>
    /// <param name="gain">Amplitude multiplier per octave, between 0 and 1.</param>
    /// <returns>A value normalised into [-1, 1].</returns>
    public double Fractal(double x, double z, int octaves, double lacunarity, double gain)
    {
        double sum = 0;
        double amplitude = 1;
        double total = 0;
        double frequency = 1;
        for (int o = 0; o < octaves; o++)
        {
            sum += amplitude * Sample(x * frequency, z * frequency);
            total += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }

        return Math.Clamp(sum / total, -1.0, 1.0);
    }

    /// <summary>
    /// Ridged fractal: bright crest lines where the underlying noise crosses zero, falling away to valleys. Gives
    /// mountain ridges instead of rounded hills.
    /// </summary>
    /// <param name="x">Lattice-space X.</param>
    /// <param name="z">Lattice-space Z.</param>
    /// <param name="octaves">Number of octaves, at least 1.</param>
    /// <param name="lacunarity">Frequency multiplier per octave.</param>
    /// <param name="gain">Amplitude multiplier per octave.</param>
    /// <returns>A value in [0, 1]; 1 on a crest line.</returns>
    public double Ridged(double x, double z, int octaves, double lacunarity, double gain)
    {
        double sum = 0;
        double amplitude = 1;
        double total = 0;
        double frequency = 1;
        for (int o = 0; o < octaves; o++)
        {
            double crest = 1.0 - Math.Abs(Sample(x * frequency, z * frequency));
            sum += amplitude * crest * crest;
            total += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }

        return Math.Clamp(sum / total, 0.0, 1.0);
    }

    private static double Fade(double t) => t * t * t * ((t * ((t * FadeA) - FadeB)) + FadeC);

    private double Dot(int ix, int iz, double dx, double dz)
    {
        int hash = _permutation[(_permutation[ix & TableMask] + iz) & (TableSize * 2 - 1)];
        (double gx, double gz) = Gradients[hash & (Gradients.Length - 1)];
        return (gx * dx) + (gz * dz);
    }
}
