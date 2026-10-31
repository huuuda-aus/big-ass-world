using System.Numerics;
using Realm.Gen.Bake;
using Xunit;

namespace Realm.Gen.Tests.Bake;

public class RegionTileRaycastTests
{
    private const int Size = 8;
    private const float Spacing = 4;

    private static RegionTile Tile(System.Func<int, int, float> height)
    {
        var heights = new float[Size * Size];
        var colours = new uint[Size * Size];
        for (int z = 0; z < Size; z++)
        {
            for (int x = 0; x < Size; x++)
            {
                heights[(z * Size) + x] = height(x, z);
            }
        }

        return new RegionTile(0, 0, Size, Spacing, heights, colours);
    }

    [Fact]
    public void StraightDownRay_HitsTheFlatGroundAtItsHeight()
    {
        RegionTile tile = Tile((_, _) => 10);

        bool found = RegionTileRaycast.TryIntersect(tile, new Vector3(12, 100, 12), new Vector3(0, -1, 0), 200, out Vector3 hit);

        Assert.True(found);
        Assert.Equal(10.0, hit.Y, 2);
        Assert.Equal(12.0, hit.X, 2);
        Assert.Equal(12.0, hit.Z, 2);
    }

    [Fact]
    public void RayPointingAwayFromTheGround_Misses()
    {
        RegionTile tile = Tile((_, _) => 10);

        bool found = RegionTileRaycast.TryIntersect(tile, new Vector3(12, 100, 12), new Vector3(0, 1, 0), 200, out _);

        Assert.False(found);
    }

    [Fact]
    public void SlopedGround_HitHeightMatchesTheBilinearSample()
    {
        RegionTile tile = Tile((x, _) => x * 2);

        bool found = RegionTileRaycast.TryIntersect(tile, new Vector3(20, 100, 8), new Vector3(0, -1, 0), 200, out Vector3 hit);

        Assert.True(found);
        Assert.Equal((double)RegionTileRaycast.SampleHeight(tile, 20, 8), hit.Y, 2);
    }

    [Fact]
    public void OriginOutsideTheTileMovingAwayFromIt_Misses()
    {
        RegionTile tile = Tile((_, _) => 10);

        bool found = RegionTileRaycast.TryIntersect(tile, new Vector3(-50, 50, 8), new Vector3(-1, -0.01f, 0), 200, out _);

        Assert.False(found);
    }

    [Fact]
    public void ZeroDirection_Misses()
    {
        RegionTile tile = Tile((_, _) => 10);

        bool found = RegionTileRaycast.TryIntersect(tile, new Vector3(12, 100, 12), Vector3.Zero, 200, out _);

        Assert.False(found);
    }

    [Fact]
    public void ToVertexCoordinates_DividesByTheSpacing()
    {
        RegionTile tile = Tile((_, _) => 0);

        (double vx, double vz) = RegionTileRaycast.ToVertexCoordinates(tile, 12, 8);

        Assert.Equal(3.0, vx, 3);
        Assert.Equal(2.0, vz, 3);
    }
}
