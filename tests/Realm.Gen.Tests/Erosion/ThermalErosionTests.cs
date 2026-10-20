using System;
using System.Linq;
using Realm.Gen.Terrain.Erosion;
using Xunit;

namespace Realm.Gen.Tests.Erosion;

public class ThermalErosionTests
{
    private const int Side = 21;
    private const double Spacing = 10;

    private static float[] Spike(float height) =>
        Enumerable.Range(0, Side * Side).Select(i => i == (10 * Side) + 10 ? height : 0f).ToArray();

    private static double[] Slope(double degrees) =>
        Enumerable.Repeat(Math.Tan(degrees * Math.PI / 180.0), Side * Side).ToArray();

    private static double Total(float[] h) => h.Sum(v => (double)v);

    private static double WorstExcess(float[] h, double tan)
    {
        double worst = 0;
        for (int z = 0; z < Side; z++)
        {
            for (int x = 0; x < Side; x++)
            {
                foreach ((int dx, int dz, double f) in new[] { (1, 0, 1.0), (0, 1, 1.0), (1, 1, Math.Sqrt(2)), (-1, 1, Math.Sqrt(2)) })
                {
                    int nx = x + dx;
                    int nz = z + dz;
                    if ((uint)nx >= Side || (uint)nz >= Side)
                    {
                        continue;
                    }

                    double slope = Math.Abs(h[(z * Side) + x] - h[(nz * Side) + nx]) / (Spacing * f);
                    worst = Math.Max(worst, slope - tan);
                }
            }
        }

        return worst;
    }

    [Fact]
    public void FlatTerrain_IsUnchanged()
    {
        float[] flat = Enumerable.Repeat(7f, Side * Side).ToArray();

        Assert.Equal(flat, ThermalEroder.Relax(flat, Side, Spacing, Slope(30), 10));
    }

    [Fact]
    public void SlopesBelowTheLimit_AreUnchangedExactly()
    {
        // A plane rising 5 m per 10 m is a slope of 0.5, below 45 degrees (1.0).
        float[] plane = Enumerable.Range(0, Side * Side).Select(i => (i % Side) * 5f).ToArray();

        Assert.Equal(plane, ThermalEroder.Relax(plane, Side, Spacing, Slope(45), 25));
    }

    [Fact]
    public void ASpike_SlumpsDown_UntilEverySlopeIsStable()
    {
        float[] relaxed = ThermalEroder.Relax(Spike(100), Side, Spacing, Slope(45), 600);

        Assert.True(relaxed[(10 * Side) + 10] < 100);
        Assert.True(WorstExcess(relaxed, 1.0) < 0.02, $"worst excess slope {WorstExcess(relaxed, 1.0)}");
    }

    [Fact]
    public void MassIsConserved()
    {
        float[] start = Spike(100);
        float[] relaxed = ThermalEroder.Relax(start, Side, Spacing, Slope(30), 200);

        Assert.Equal(Total(start), Total(relaxed), 1);
    }

    [Fact]
    public void ValuesStayWithinTheOriginalRange()
    {
        float[] relaxed = ThermalEroder.Relax(Spike(100), Side, Spacing, Slope(20), 100);

        Assert.All(relaxed, v => Assert.InRange(v, -1e-3f, 100.001f));
    }

    [Fact]
    public void ASymmetricSpike_StaysSymmetric()
    {
        float[] relaxed = ThermalEroder.Relax(Spike(100), Side, Spacing, Slope(40), 80);

        for (int z = 0; z < Side; z++)
        {
            for (int x = 0; x < Side; x++)
            {
                Assert.Equal((double)relaxed[(z * Side) + x], relaxed[(z * Side) + (Side - 1 - x)], 3);
                Assert.Equal((double)relaxed[(z * Side) + x], relaxed[((Side - 1 - z) * Side) + x], 3);
                Assert.Equal((double)relaxed[(z * Side) + x], relaxed[(x * Side) + z], 3);
            }
        }
    }

    [Fact]
    public void SteeperLimit_ChangesLess()
    {
        double gentle = Spike(100)[(10 * Side) + 10] - ThermalEroder.Relax(Spike(100), Side, Spacing, Slope(20), 50)[(10 * Side) + 10];
        double steep = Spike(100)[(10 * Side) + 10] - ThermalEroder.Relax(Spike(100), Side, Spacing, Slope(70), 50)[(10 * Side) + 10];

        Assert.True(gentle > steep, $"gentle {gentle} should lower the spike more than steep {steep}");
    }

    [Fact]
    public void NearVerticalLimit_ChangesNothing()
    {
        float[] start = Spike(100);

        Assert.Equal(start, ThermalEroder.Relax(start, Side, Spacing, Slope(89), 50));
    }

    [Fact]
    public void ZeroIterations_ReturnsACopyOfTheInput()
    {
        float[] start = Spike(100);
        float[] result = ThermalEroder.Relax(start, Side, Spacing, Slope(30), 0);

        Assert.Equal(start, result);
        Assert.NotSame(start, result);
    }

    [Fact]
    public void ParallelEvaluation_IsDeterministic()
    {
        float[] first = ThermalEroder.Relax(Spike(100), Side, Spacing, Slope(35), 60);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(first, ThermalEroder.Relax(Spike(100), Side, Spacing, Slope(35), 60));
        }
    }

    [Fact]
    public void PerVertexLimits_AreRespected()
    {
        // West half is gentle (20 degrees), east half is steep (80): material slumps only on the gentle side.
        double[] limit = Enumerable.Range(0, Side * Side).Select(i => Math.Tan((i % Side) < 10 ? 20 * Math.PI / 180.0 : 80 * Math.PI / 180.0)).ToArray();
        float[] cliff = Enumerable.Range(0, Side * Side).Select(i => (i % Side) < 10 ? 0f : 100f).ToArray();

        float[] relaxed = ThermalEroder.Relax(cliff, Side, Spacing, limit, 5);

        Assert.True(relaxed[(10 * Side) + 10] < 100, "the step is next to gentle terrain and must slump");
        Assert.Equal(100.0, (double)relaxed[(10 * Side) + 19], 3); // far to the east nothing is steep enough to move
    }
}
