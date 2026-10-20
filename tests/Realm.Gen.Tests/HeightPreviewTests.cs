using System;
using Realm.Gen.Terrain;
using Xunit;

namespace Realm.Gen.Tests;

public class HeightPreviewTests
{
    private static HeightField Field(int side, Func<int, int, float> height)
    {
        var heights = new float[side * side];
        for (int z = 0; z < side; z++)
        {
            for (int x = 0; x < side; x++)
            {
                heights[(z * side) + x] = height(x, z);
            }
        }

        return new HeightField(side - 1, 1, heights);
    }

    [Fact]
    public void Render_SizesTheImageToTheLimit()
    {
        HeightField field = Field(101, (_, _) => 0);

        byte[] pixels = HeightPreview.Render(field, 100, out int w, out int h);

        Assert.True(w <= 100 && h <= 100);
        Assert.Equal(w, h);
        Assert.Equal(w * h * 3, pixels.Length);
    }

    [Fact]
    public void Render_SmallFields_AreNotUpscaled()
    {
        byte[] pixels = HeightPreview.Render(Field(9, (_, _) => 5), 1000, out int w, out int h);

        Assert.Equal(9, w);
        Assert.Equal(9, h);
        Assert.Equal(9 * 9 * 3, pixels.Length);
    }

    [Fact]
    public void Render_IsDeterministic()
    {
        HeightField field = Field(33, (x, z) => (x * 3) - (z * 2));

        Assert.Equal(HeightPreview.Render(field, 33, out _, out _), HeightPreview.Render(field, 33, out _, out _));
    }

    [Fact]
    public void Water_IsBlueish_AndHighGroundIsLighter()
    {
        // West half sea (-100 m), east half mountain (+500 m).
        HeightField field = Field(21, (x, _) => x < 10 ? -100 : 500);
        byte[] pixels = HeightPreview.Render(field, 21, out int w, out _);

        int sea = ((10 * w) + 2) * 3;
        int peak = ((10 * w) + 18) * 3;
        Assert.True(pixels[sea + 2] > pixels[sea], "sea should be more blue than red");
        Assert.True(pixels[peak] + pixels[peak + 1] + pixels[peak + 2] > pixels[sea] + pixels[sea + 1] + pixels[sea + 2]);
    }

    [Fact]
    public void Render_RejectsBadArguments()
    {
        Assert.Throws<ArgumentNullException>(() => HeightPreview.Render(null!, 10, out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => HeightPreview.Render(Field(5, (_, _) => 0), 0, out _, out _));
    }

    [Fact]
    public void RegionBorders_AreDrawnWhereRegionsMeet_AndOnlyThere()
    {
        // A 16 m world of 4 x 4 cells (4 m each): west half region 1, east half region 2.
        var cells = new ushort[16];
        for (int z = 0; z < 4; z++)
        {
            for (int x = 0; x < 4; x++)
            {
                cells[(z * 4) + x] = (ushort)(x < 2 ? 1 : 2);
            }
        }

        var regions = new RegionField(16, 4, new[] { Realm.Core.Ids.ContentId.Parse("region.a"), Realm.Core.Ids.ContentId.Parse("region.b") }, cells);
        HeightField field = Field(17, (_, _) => 0.5f);

        byte[] plain = HeightPreview.Render(field, 17, out int w, out _);
        byte[] marked = HeightPreview.Render(field, 17, out _, out _, regions: regions);

        // Pixel columns 7 and 8 straddle the border at x = 0 (vertex 8): column 7 is the last pixel before it.
        int border = ((8 * w) + 7) * 3;
        Assert.NotEqual(plain[border], marked[border]);
        Assert.True(marked[border] > plain[border], "border pixels are pushed toward white");
        int inside = ((8 * w) + 2) * 3;
        Assert.Equal(plain[inside], marked[inside]);
        int farEast = ((8 * w) + 14) * 3;
        Assert.Equal(plain[farEast], marked[farEast]);
    }

    [Fact]
    public void RegionBorders_AreOptional_AndNullChangesNothing()
    {
        HeightField field = Field(9, (x, _) => x * 10f);

        Assert.Equal(
            HeightPreview.Render(field, 9, out _, out _),
            HeightPreview.Render(field, 9, out _, out _, regions: null));
    }

    [Fact]
    public void Lighting_ComesFromTheNorthWest()
    {
        // Three fields with the same centre height (50) and the same extremes (10 and 90), so the palette is identical
        // and only the shading differs at the centre. Rising toward the south-east faces the light.
        HeightField flat = Field(21, (x, z) => (x, z) == (0, 0) ? 10f : (x, z) == (20, 20) ? 90f : 50f);
        HeightField lit = Field(21, (x, z) => 50f + ((x + z - 20) * 2f));
        HeightField dark = Field(21, (x, z) => 50f - ((x + z - 20) * 2f));

        byte[] flatPixels = HeightPreview.Render(flat, 21, out int w, out _);
        byte[] litPixels = HeightPreview.Render(lit, 21, out _, out _);
        byte[] darkPixels = HeightPreview.Render(dark, 21, out _, out _);

        int centre = ((10 * w) + 10) * 3;
        int Sum(byte[] p) => p[centre] + p[centre + 1] + p[centre + 2];
        Assert.True(Sum(litPixels) > Sum(flatPixels), "a slope facing the light should be brighter");
        Assert.True(Sum(darkPixels) < Sum(flatPixels), "a slope facing away should be darker");
    }
}
