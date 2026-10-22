using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Realm.Core.Ids;
using Realm.Data.Content;
using Realm.Gen.Terrain;
using Realm.Gen.Textures;
using Realm.Gen.Weather;
using Xunit;

namespace Realm.Gen.Tests.Weather;

public class CloudTests
{
    private const int World = 1024;
    private const int Spacing = 8;
    private const int Side = (World / Spacing) + 1;

    private static HeightField Flat(float height = 50f, Func<double, double, float>? shape = null)
    {
        var h = new float[Side * Side];
        for (int row = 0; row < Side; row++)
        {
            for (int col = 0; col < Side; col++)
            {
                h[(row * Side) + col] = shape?.Invoke(-512.0 + (col * (double)Spacing), -512.0 + (row * (double)Spacing)) ?? height;
            }
        }

        return new HeightField(World, Spacing, h);
    }

    // The west half is a wet biome (rainfall 0.9), the east half a dry one (rainfall 0.1).
    private static (MacroMapDefinition Map, IReadOnlyDictionary<ContentId, BiomeDefinition> Biomes) WetAndDry()
    {
        var map = new MacroMapDefinition(
            ContentId.Parse("world.test"), "Test", World,
            new[]
            {
                TerrainTestHelpers.Region("region.wet", "biome.wet", TerrainTestHelpers.Rect(-512, -512, 0, 512)),
                TerrainTestHelpers.Region("region.dry", "biome.dry", TerrainTestHelpers.Rect(0, -512, 512, 512)),
            });
        var biomes = TerrainTestHelpers.Biomes(
            TerrainTestHelpers.Biome("biome.wet", false, ("rainfall", 0.9)),
            TerrainTestHelpers.Biome("biome.dry", false, ("rainfall", 0.1)));
        return (map, biomes);
    }

    private static CloudClimate Climate(HeightField field, ulong seed = 1, int side = 32)
    {
        (MacroMapDefinition map, IReadOnlyDictionary<ContentId, BiomeDefinition> biomes) = WetAndDry();
        return CloudClimateGenerator.Generate(map, TerrainTestHelpers.Rasterize(map), biomes, field, seed, side, blendMeters: 100);
    }

    [Fact]
    public void WetterBiomes_AreCloudierAndDeeper()
    {
        CloudClimate c = Climate(Flat());

        Assert.True(c.CoverAt(-380, 0) > c.CoverAt(380, 0) + 0.25, $"wet {c.CoverAt(-380, 0):F2} dry {c.CoverAt(380, 0):F2}");
        Assert.True(c.ThicknessAt(-380, 0) > c.ThicknessAt(380, 0) + 0.2);
    }

    [Fact]
    public void HighGround_GathersExtraCloud()
    {
        CloudClimate low = Climate(Flat(50));
        CloudClimate high = Climate(Flat(1000));

        Assert.True(high.CoverAt(380, 0) > low.CoverAt(380, 0) + 0.15);
        Assert.True(high.ThicknessAt(380, 0) > low.ThicknessAt(380, 0) + 0.15);
    }

    [Fact]
    public void SameInputs_SameClimate_DifferentSeed_Different()
    {
        CloudClimate a = Climate(Flat());
        Assert.Equal(a.Cover, Climate(Flat()).Cover);
        Assert.NotEqual(a.Cover, Climate(Flat(), seed: 2).Cover);
    }

    [Fact]
    public void Sampling_InterpolatesBetweenCells_AndClampsAtTheEdges()
    {
        var cover = new byte[] { 0, 255, 0, 255 };
        var climate = new CloudClimate(100, 2, cover, new byte[4]);

        Assert.Equal(0, climate.CoverAt(-50, 0), 6);
        Assert.Equal(1, climate.CoverAt(50, 0), 6);
        Assert.Equal(0.5, climate.CoverAt(0, 0), 2);
        Assert.Equal(1, climate.CoverAt(5000, 0), 6);
        Assert.Throws<ArgumentException>(() => new CloudClimate(100, 2, new byte[3], new byte[4]));
        Assert.Equal(0.4, CloudClimate.Uniform(100, 0.4, 0.2).CoverAt(10, -10), 2);
    }

    [Fact]
    public void TheFile_RoundTrips_AndRejectsGarbage()
    {
        CloudClimate climate = Climate(Flat());

        CloudClimate back = CloudFile.FromBytes(CloudFile.ToBytes(climate));

        Assert.Equal(climate.Side, back.Side);
        Assert.Equal(climate.WorldSizeMeters, back.WorldSizeMeters);
        Assert.Equal(climate.Cover, back.Cover);
        Assert.Equal(climate.Thickness, back.Thickness);
        Assert.Throws<InvalidDataException>(() => CloudFile.FromBytes(new byte[] { 1, 2, 3, 4, 5 }));
        byte[] cut = CloudFile.ToBytes(climate);
        Assert.Throws<InvalidDataException>(() => CloudFile.FromBytes(cut[..(cut.Length - 10)]));
    }

    [Fact]
    public void TheNoiseTexture_IsDeterministic_Tileable_AndVariedInEveryChannel()
    {
        byte[] a = CloudTextureGenerator.Noise(7, 64);

        Assert.Equal(a, CloudTextureGenerator.Noise(7, 64));
        Assert.NotEqual(a, CloudTextureGenerator.Noise(8, 64));
        Assert.Equal(64 * 64 * 4, a.Length);
        for (int channel = 0; channel < 4; channel++)
        {
            var values = Enumerable.Range(0, 64 * 64).Select(i => (int)a[(i * 4) + channel]).ToList();
            Assert.True(values.Max() - values.Min() > 80, $"channel {channel} is flat");
            // Tileable: the step across the wrap-around edge is no bigger than the typical step inside the texture.
            double inside = Enumerable.Range(0, 64).SelectMany(y => Enumerable.Range(0, 63).Select(x => Math.Abs(a[((y * 64) + x + 1) * 4 + channel] - a[((y * 64) + x) * 4 + channel]))).Average();
            double wrap = Enumerable.Range(0, 64).Select(y => Math.Abs(a[(y * 64) * 4 + channel] - a[((y * 64) + 63) * 4 + channel])).Average();
            Assert.True(wrap < inside * 2.5 + 3, $"channel {channel} has a seam: wrap step {wrap:F1}, inside {inside:F1}");
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => CloudTextureGenerator.Noise(1, 100));
    }

    [Fact]
    public void TheStreakChannel_RunsAlongTheRows()
    {
        byte[] a = CloudTextureGenerator.Noise(3, 128);
        double row = 0, column = 0;
        for (int y = 1; y < 127; y++)
        {
            for (int x = 1; x < 127; x++)
            {
                int i = ((y * 128) + x) * 4 + 3;
                row += Math.Abs(a[i + 4] - a[i]);
                column += Math.Abs(a[i + (128 * 4)] - a[i]);
            }
        }

        Assert.True(row < column, $"streaks do not run along the rows: {row} along, {column} across");
    }

    [Fact]
    public void Golden_NoiseTexture_IsStable()
    {
        byte[] pixels = CloudTextureGenerator.Noise(1, 64);
        ulong hash = 14695981039346656037UL;
        foreach (byte b in pixels)
        {
            hash = (hash ^ b) * 1099511628211UL;
        }

        // Golden: a change means the texture changed. If intended, bump CloudTextureGenerator.Version and recapture.
        Assert.Equal(GoldenChecksum, hash);
    }

    private const ulong GoldenChecksum = 5372056789878938765UL;
}
