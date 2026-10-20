using System;
using System.Linq;
using System.Numerics;
using Realm.Gen.Props;
using Xunit;

namespace Realm.Gen.Tests.Props;

public class PropModelTests
{
    // With 5 rings the widest ring sits at 72 degrees from the pole, so crowns reach sin(72) of their nominal radius.
    private static readonly double Widest = Math.Sin(2 * Math.PI / 5);

    private static void Near(double expected, double actual, int digits) => Assert.Equal(expected, actual, digits);

    [Fact]
    public void Cottage_IsOneStorey_4_2MetresToTheRidge()
    {
        PropMesh m = HouseModels.Cottage();

        Near(2.4f + 1.8f, m.HeightMeters, 3);
        Near(-HouseModels.FoundationDepthMeters, m.Min.Y, 3);
    }

    [Fact]
    public void TownHouse_IsTwoStorey_7_4MetresToTheRidge()
    {
        Near(5f + 2.4f, HouseModels.TownHouse().HeightMeters, 3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HouseFootprint_IsRoofOverhangPlusWalls(bool cottage)
    {
        PropMesh m = cottage ? HouseModels.Cottage() : HouseModels.TownHouse();
        float width = cottage ? 6f : 7f;
        float depth = cottage ? 5f : 6f;

        Near(width + (2 * 0.35f), m.Max.X - m.Min.X, 3);
        Near(depth + (2 * 0.35f), m.Max.Z - m.Min.Z, 2); // door and windows add at most 6 cm on the front
    }

    [Theory]
    [InlineData(6.0, 5.0, 2.4, 1.8)]
    [InlineData(7.0, 6.0, 5.0, 2.4)]
    public void House_EnclosesAtLeastItsWallsAndRoof_AndNotMuchMore(double w, double d, double wall, double rise)
    {
        PropMesh m = w == 6.0 ? HouseModels.Cottage() : HouseModels.TownHouse();

        // Independent hand calculation of the main solids.
        double foundation = (w + 0.2) * (d + 0.2) * (0.3 + 1.5);
        double walls = w * d * (wall - 0.3);
        double roof = (w + 0.7) * (d + 0.7) * rise / 2;
        double main = foundation + walls + roof;

        double volume = m.SignedVolume();
        Assert.True(volume > main, $"volume {volume:F1} should exceed main solids {main:F1} (frame, door and windows add to it)");
        Assert.True(volume < main * 1.08, $"volume {volume:F1} is much more than main solids {main:F1}");
    }

    [Theory]
    [InlineData(TreeKind.Pine)]
    [InlineData(TreeKind.Oak)]
    [InlineData(TreeKind.Poplar)]
    public void Tree_TipIsExactlyTheRequestedHeight(TreeKind kind)
    {
        foreach (float height in new[] { 4f, 6.5f, 10f })
        {
            PropMesh tree = TreeModels.Build(kind, height);
            Near(height, tree.HeightMeters, 2);
            Assert.True(tree.Min.Y < 0 && tree.Min.Y >= -0.31f, "roots sink a little below the ground");
        }
    }

    [Theory]
    [InlineData(3.9f)]
    [InlineData(10.1f)]
    public void Tree_RejectsHeightsOutsideTheRange(float height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TreeModels.Build(TreeKind.Oak, height));

    [Fact]
    public void ThreeKinds_HaveDifferentSilhouettes()
    {
        var widths = new[] { TreeKind.Pine, TreeKind.Oak, TreeKind.Poplar }
            .Select(k => { PropMesh t = TreeModels.Build(k, 8f); return Math.Round(t.Max.X - t.Min.X, 1); })
            .ToArray();

        Assert.Equal(3, widths.Distinct().Count());
        Assert.True(widths[2] < widths[1], "poplar is narrower than oak");
    }

    [Fact]
    public void Poplar_IsNarrowAndOak_IsWide_ByHandCalculation()
    {
        Near(Widest * 2 * 0.12f * 8f, TreeModels.Build(TreeKind.Poplar, 8f).Max.X - TreeModels.Build(TreeKind.Poplar, 8f).Min.X, 1);
        Near(Widest * 2 * 0.36f * 8f, TreeModels.Build(TreeKind.Oak, 8f).Max.X - TreeModels.Build(TreeKind.Oak, 8f).Min.X, 1);
    }

    [Fact]
    public void AllModels_HaveOutwardWinding_NoDegenerateTriangles_AndValidColours()
    {
        PropMesh[] all =
        {
            HouseModels.Cottage(), HouseModels.TownHouse(),
            TreeModels.Build(TreeKind.Pine, 8f), TreeModels.Build(TreeKind.Oak, 6f), TreeModels.Build(TreeKind.Poplar, 10f),
        };

        foreach (PropMesh m in all)
        {
            Assert.True(m.SignedVolume() > 0, $"{m.Name} is wound inside out");
            foreach (PropTriangle t in m.Triangles)
            {
                Assert.True(Vector3.Cross(t.B - t.A, t.C - t.A).Length() > 1e-6f, $"{m.Name} has a degenerate triangle");
                Assert.InRange(t.Colour.X, 0f, 1f);
                Assert.InRange(t.Colour.Y, 0f, 1f);
                Assert.InRange(t.Colour.Z, 0f, 1f);
            }
        }
    }

    [Fact]
    public void Models_AreDeterministic() =>
        Assert.Equal(HouseModels.Cottage().Triangles, HouseModels.Cottage().Triangles);
}
