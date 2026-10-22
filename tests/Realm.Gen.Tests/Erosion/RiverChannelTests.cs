using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;
using Realm.Gen.Terrain.Water;
using Xunit;

namespace Realm.Gen.Tests.Erosion;

/// <summary>Rivers stand in a bed at a level that only falls downstream; the water mesh follows that level.</summary>
public class RiverChannelTests
{
    private const int World = 4096;
    private const int Spacing = 4;
    private const int Side = (World / Spacing) + 1;
    private const double Half = World / 2.0;

    // A valley that falls to the east, with a 20 m hump across the river's path and a cross slope to the south.
    private static float[] Terrain()
    {
        var h = new float[Side * Side];
        for (int row = 0; row < Side; row++)
        {
            for (int col = 0; col < Side; col++)
            {
                double x = -Half + (col * (double)Spacing);
                double z = -Half + (row * (double)Spacing);
                double hump = 20 * Math.Exp(-((x * x) / 40000.0));
                h[(row * Side) + col] = (float)(150 - (0.03 * (x + Half)) + hump + (0.08 * z));
            }
        }

        return h;
    }

    private static River RiverAlongZ0(RiverMouth mouth = RiverMouth.Dried)
    {
        var points = new List<RiverPoint>();
        for (double x = -1800; x <= 1800; x += 32)
        {
            points.Add(new RiverPoint(x, 0, 100, 10, 8));
        }

        return new River(0, points, mouth, null, 3600);
    }

    private static (HeightField Field, IReadOnlyList<RiverChannel> Channels) Run(River river)
    {
        float[] heights = Terrain();
        var field = new HeightField(World, Spacing, heights);
        IReadOnlyList<RiverChannel> channels = RiverChannelBuilder.Profile(field, new[] { river }, Array.Empty<Lake>(), 32);
        RiverChannelBuilder.Carve(heights, Side, Spacing, World, channels, Array.Empty<Lake>(), 32);
        return (new HeightField(World, Spacing, heights), channels);
    }

    [Fact]
    public void TheWater_NeverRisesDownstream()
    {
        (_, IReadOnlyList<RiverChannel> channels) = Run(RiverAlongZ0());

        IReadOnlyList<ChannelPoint> pts = channels.Single().Points;
        for (int i = 1; i < pts.Count; i++)
        {
            Assert.True(pts[i].Surface <= pts[i - 1].Surface + 1e-9, $"the water rises at point {i}");
        }

        Assert.True(pts[0].Surface > pts[^1].Surface + 20, "the river does not fall");
    }

    [Fact]
    public void TheBed_SitsBelowTheWater_AndBothBanksHoldIt()
    {
        (HeightField field, IReadOnlyList<RiverChannel> channels) = Run(RiverAlongZ0());

        foreach (ChannelPoint p in channels.Single().Points.Skip(8).SkipLast(8))
        {
            Assert.True(field.Sample(p.X, p.Z) < p.Surface - 0.5, $"no bed under the water at {p.X:F0}");
            Assert.True(field.Sample(p.X, p.Z + p.HalfWidth + 1) >= p.Surface - 0.5, $"the south bank leaks at {p.X:F0}");
            Assert.True(field.Sample(p.X, p.Z - p.HalfWidth - 1) >= p.Surface - 0.5, $"the north bank leaks at {p.X:F0}");
        }
    }

    [Fact]
    public void ARiverToTheSea_EndsAtSeaLevel()
    {
        (_, IReadOnlyList<RiverChannel> channels) = Run(RiverAlongZ0(RiverMouth.Sea));

        Assert.Equal(0, channels.Single().Points[^1].Surface, 6);
    }

    [Fact]
    public void Speed_GrowsWithTheSlope_AndStaysInRange()
    {
        Assert.True(RiverFlow.Speed(0.3, 10) > RiverFlow.Speed(0.01, 10));
        Assert.Equal(RiverFlow.MinimumSpeed, RiverFlow.Speed(0, 0), 9);
        Assert.Equal(RiverFlow.MaximumSpeed, RiverFlow.Speed(5, 1000), 9);
    }

    [Fact]
    public void TheMesh_FollowsTheProfile_AndRoundTripsThroughTheFile()
    {
        (_, IReadOnlyList<RiverChannel> channels) = Run(RiverAlongZ0());
        var network = new RiverNetwork(new[] { RiverAlongZ0() }, Array.Empty<Lake>(), 32, channels);

        IReadOnlyList<WaterTile> tiles = WaterMesher.Build(network, World, 1024);

        Assert.NotEmpty(tiles);
        Assert.All(tiles, t => Assert.All(t.Indices, i => Assert.InRange(i, 0, t.VertexCount - 1)));
        float top = tiles.SelectMany(t => Enumerable.Range(0, t.VertexCount).Select(i => t.Positions[(i * 3) + 1])).Max();
        Assert.True(top <= channels.Single().Points[0].Surface + 0.06);
        IReadOnlyList<WaterTile> back = WaterFile.FromBytes(WaterFile.ToBytes(tiles));
        Assert.Equal(tiles.Count, back.Count);
        Assert.Equal(tiles[0].Positions, back[0].Positions);
        Assert.Throws<System.IO.InvalidDataException>(() => WaterFile.FromBytes(new byte[] { 1, 2, 3 }));
    }
}
