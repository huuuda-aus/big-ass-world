using System;
using Realm.Core.Random;
using Realm.Gen.Terrain;
using Xunit;

namespace Realm.Gen.Tests;

public class GradientNoiseTests
{
    private static GradientNoise Noise(ulong seed = 1) => new(Rng.ForKey(seed, "test", "noise"));

    [Fact]
    public void SameSeed_GivesSameValues()
    {
        GradientNoise a = Noise(5);
        GradientNoise b = Noise(5);

        for (int i = 0; i < 200; i++)
        {
            double x = (i * 0.37) - 20;
            double z = (i * 0.91) - 50;
            Assert.Equal(a.Sample(x, z), b.Sample(x, z));
            Assert.Equal(a.Fractal(x, z, 5, 2, 0.5), b.Fractal(x, z, 5, 2, 0.5));
        }
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentTerrain()
    {
        GradientNoise a = Noise(1);
        GradientNoise b = Noise(2);

        int differing = 0;
        for (int i = 0; i < 200; i++)
        {
            if (a.Sample((i * 0.37) + 0.11, (i * 0.91) + 0.23) != b.Sample((i * 0.37) + 0.11, (i * 0.91) + 0.23))
            {
                differing++;
            }
        }

        Assert.True(differing > 150, $"only {differing} of 200 samples differed");
    }

    [Fact]
    public void IsZeroAtLatticePoints()
    {
        GradientNoise n = Noise();

        for (int x = -5; x <= 5; x++)
        {
            for (int z = -5; z <= 5; z++)
            {
                Assert.Equal(0.0, n.Sample(x, z), 12);
            }
        }
    }

    [Fact]
    public void StaysWithinUnitRange_AndIsFinite_AtLargeAndNegativeCoordinates()
    {
        GradientNoise n = Noise();
        var rng = new Rng(9);

        for (int i = 0; i < 20_000; i++)
        {
            double x = (rng.NextDouble() - 0.5) * 20_000;
            double z = (rng.NextDouble() - 0.5) * 20_000;
            double v = n.Sample(x, z);
            Assert.True(double.IsFinite(v) && Math.Abs(v) <= 1.0000001, $"({x}, {z}) -> {v}");
        }
    }

    [Fact]
    public void IsContinuous()
    {
        GradientNoise n = Noise();
        var rng = new Rng(3);
        const double step = 1e-4;

        for (int i = 0; i < 2_000; i++)
        {
            double x = (rng.NextDouble() - 0.5) * 100;
            double z = (rng.NextDouble() - 0.5) * 100;
            Assert.True(Math.Abs(n.Sample(x + step, z) - n.Sample(x, z)) < 1e-3);
            Assert.True(Math.Abs(n.Sample(x, z + step) - n.Sample(x, z)) < 1e-3);
        }
    }

    [Fact]
    public void IsPeriodicEvery256LatticeUnits()
    {
        GradientNoise n = Noise();

        Assert.Equal(n.Sample(3.3, 4.7), n.Sample(3.3 + 256, 4.7), 9);
        Assert.Equal(n.Sample(-3.3, -4.7), n.Sample(-3.3 + 256, -4.7 + 512), 9);
    }

    [Fact]
    public void HasSensibleStatistics()
    {
        GradientNoise n = Noise();
        double sum = 0;
        double sumSquares = 0;
        const int Side = 200;
        for (int i = 0; i < Side; i++)
        {
            for (int j = 0; j < Side; j++)
            {
                double v = n.Sample(i * 0.37, j * 0.41);
                sum += v;
                sumSquares += v * v;
            }
        }

        double count = Side * Side;
        double mean = sum / count;
        double deviation = Math.Sqrt((sumSquares / count) - (mean * mean));
        Assert.InRange(mean, -0.05, 0.05);
        Assert.InRange(deviation, 0.2, 0.7);
    }

    [Fact]
    public void Fractal_WithOneOctave_IsOneSample()
    {
        GradientNoise n = Noise();

        Assert.Equal(n.Sample(1.3, 2.9), n.Fractal(1.3, 2.9, 1, 2, 0.5), 12);
    }

    [Fact]
    public void Fractal_StaysInRange_AndLowerGainIsSmoother()
    {
        GradientNoise n = Noise();
        double roughEnergy = 0;
        double smoothEnergy = 0;
        const double step = 0.01;
        for (int i = 0; i < 2_000; i++)
        {
            double x = i * 0.0731;
            double z = i * 0.0517;
            double rough = n.Fractal(x, z, 6, 2, 0.6);
            double smooth = n.Fractal(x, z, 6, 2, 0.35);
            Assert.InRange(rough, -1.0, 1.0);
            Assert.InRange(smooth, -1.0, 1.0);
            roughEnergy += Math.Abs(n.Fractal(x + step, z, 6, 2, 0.6) - rough);
            smoothEnergy += Math.Abs(n.Fractal(x + step, z, 6, 2, 0.35) - smooth);
        }

        Assert.True(roughEnergy > smoothEnergy, $"rough {roughEnergy} should exceed smooth {smoothEnergy}");
    }

    [Fact]
    public void Constructor_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new GradientNoise(null!));
    }

    [Fact]
    public void Fractal_IsStableGolden()
    {
        // Golden: changing the noise reshapes every world. If intended, bump HeightFieldGenerator.GeneratorVersion.
        double v = Noise(42).Fractal(12.34, -56.78, 6, 2, 0.5);

        Assert.Equal(GoldenFractal, v, 12);
    }

    // Captured from the first accepted implementation (generator version 1); see Fractal_IsStableGolden.
    private const double GoldenFractal = 0.09142582937236886;

    [Fact]
    public void Ridged_StaysInZeroToOne_AndPeaksOnTheLatticeWhereTheNoiseIsZero()
    {
        GradientNoise noise = Noise(3);

        for (int i = 0; i < 2000; i++)
        {
            double r = noise.Ridged((i * 0.173) - 100, (i * 0.311) - 40, 4, 2.0, 0.5);
            Assert.InRange(r, 0.0, 1.0);
        }

        // Plain noise is exactly 0 at integer lattice points, and so is every octave when the lacunarity is 2: all crests, so exactly 1.
        Assert.Equal(1.0, noise.Ridged(7, -3, 4, 2.0, 0.5), 12);
        Assert.Equal(1.0, noise.Ridged(-12, 25, 5, 2.0, 0.6), 12);
    }

    [Fact]
    public void Ridged_IsDeterministic_AndDiffersFromFractal()
    {
        Assert.Equal(Noise(2).Ridged(1.3, 4.7, 4, 2, 0.5), Noise(2).Ridged(1.3, 4.7, 4, 2, 0.5));
        Assert.NotEqual(Noise(2).Ridged(1.3, 4.7, 4, 2, 0.5), (Noise(2).Fractal(1.3, 4.7, 4, 2, 0.5) + 1) / 2);
    }
}
