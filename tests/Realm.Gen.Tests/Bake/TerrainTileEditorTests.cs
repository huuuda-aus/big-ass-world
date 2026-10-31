using Realm.Gen.Bake;
using Realm.Gen.Terrain;
using Xunit;

namespace Realm.Gen.Tests.Bake;

public class TerrainTileEditorTests
{
    private const int Size = 8;
    private const float Spacing = 4;
    private const double PaletteTop = 100;
    private const double PaletteDepth = 50;

    private static RegionTile FlatTile(float height = 0, uint colour = 0xFF808080)
    {
        var heights = new float[Size * Size];
        var colours = new uint[Size * Size];
        for (int i = 0; i < heights.Length; i++)
        {
            heights[i] = height;
            colours[i] = colour;
        }

        return new RegionTile(0, 0, Size, Spacing, heights, colours);
    }

    private static int Index(int x, int z) => (z * Size) + x;

    [Fact]
    public void Raise_IncreasesHeightAtCentre_AndLeavesFarVerticesUntouched()
    {
        RegionTile tile = FlatTile();
        var stroke = new BrushStroke { CenterX = 4, CenterZ = 4, RadiusVertices = 2, Strength = 10, Mode = BrushMode.Raise };

        RegionTile edited = TerrainTileEditor.ApplyBrush(tile, stroke, PaletteTop, PaletteDepth);

        Assert.Equal(10.0, edited.Heights[Index(4, 4)], 3);
        Assert.Equal(0f, edited.Heights[Index(0, 0)]); // outside the radius
    }

    [Fact]
    public void Lower_DecreasesHeightAtCentre()
    {
        RegionTile tile = FlatTile(height: 20);
        var stroke = new BrushStroke { CenterX = 4, CenterZ = 4, RadiusVertices = 2, Strength = 5, Mode = BrushMode.Lower };

        RegionTile edited = TerrainTileEditor.ApplyBrush(tile, stroke, PaletteTop, PaletteDepth);

        Assert.Equal(15.0, edited.Heights[Index(4, 4)], 3);
    }

    [Fact]
    public void Flatten_AtFullRateAndCentre_SnapsToTheTarget()
    {
        RegionTile tile = FlatTile(height: 7);
        var stroke = new BrushStroke { CenterX = 4, CenterZ = 4, RadiusVertices = 2, Strength = 1, Mode = BrushMode.Flatten, FlattenHeight = 50 };

        RegionTile edited = TerrainTileEditor.ApplyBrush(tile, stroke, PaletteTop, PaletteDepth);

        Assert.Equal(50.0, edited.Heights[Index(4, 4)], 3);
    }

    [Fact]
    public void Smooth_PullsASpikeTowardItsNeighboursAverage()
    {
        RegionTile tile = FlatTile(height: 0);
        tile.Heights[Index(4, 4)] = 100;
        var stroke = new BrushStroke { CenterX = 4, CenterZ = 4, RadiusVertices = 1, Strength = 1, Mode = BrushMode.Smooth };

        RegionTile edited = TerrainTileEditor.ApplyBrush(tile, stroke, PaletteTop, PaletteDepth);

        Assert.True(edited.Heights[Index(4, 4)] < 100f);
        Assert.True(edited.Heights[Index(4, 4)] > 0f);
    }

    [Fact]
    public void ApplyBrush_DoesNotMutateTheInputTile()
    {
        RegionTile tile = FlatTile();
        var originalHeights = (float[])tile.Heights.Clone();
        var stroke = new BrushStroke { CenterX = 4, CenterZ = 4, RadiusVertices = 2, Strength = 10, Mode = BrushMode.Raise };

        TerrainTileEditor.ApplyBrush(tile, stroke, PaletteTop, PaletteDepth);

        Assert.Equal(originalHeights, tile.Heights);
    }

    [Fact]
    public void WaterVertices_AreNeverReshaded()
    {
        RegionTile tile = FlatTile(colour: TerrainColouring.WaterPacked);
        var stroke = new BrushStroke { CenterX = 4, CenterZ = 4, RadiusVertices = 2, Strength = 10, Mode = BrushMode.Raise };

        RegionTile edited = TerrainTileEditor.ApplyBrush(tile, stroke, PaletteTop, PaletteDepth);

        Assert.Equal(TerrainColouring.WaterPacked, edited.Colours[Index(4, 4)]);
    }

    [Fact]
    public void NonWaterVertices_AreReshadedAfterAHeightChange()
    {
        RegionTile tile = FlatTile();
        uint flatColour = tile.Colours[Index(4, 4)];
        var stroke = new BrushStroke { CenterX = 4, CenterZ = 4, RadiusVertices = 3, Strength = 40, Mode = BrushMode.Raise };

        RegionTile edited = TerrainTileEditor.ApplyBrush(tile, stroke, PaletteTop, PaletteDepth);

        Assert.NotEqual(flatColour, edited.Colours[Index(4, 4)]);
    }

    [Fact]
    public void NonPositiveRadius_Throws()
    {
        RegionTile tile = FlatTile();
        var stroke = new BrushStroke { CenterX = 4, CenterZ = 4, RadiusVertices = 0, Strength = 1, Mode = BrushMode.Raise };

        Assert.Throws<System.ArgumentException>(() => TerrainTileEditor.ApplyBrush(tile, stroke, PaletteTop, PaletteDepth));
    }
}
