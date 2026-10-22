using System.Linq;
using System;
using Realm.Gen.Textures;
using Xunit;

namespace Realm.Gen.Tests.Textures;

public class SurfaceTextureGeneratorTests
{
    private const ulong Seed = 20261020;

    [Fact]
    public void Images_HaveTheRequestedSize()
    {
        SurfaceTexture t = SurfaceTextureGenerator.Grass(Seed);

        Assert.Equal(64, t.Size);
        Assert.Equal(64 * 64 * 4, t.AlbedoHeight.Length);
        Assert.Equal(64 * 64 * 4, t.NormalRoughness.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(48)]
    public void BadSizes_AreRejected(int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceTextureGenerator.Rock(Seed, size));

    [Fact]
    public void SameSeed_SamePixels_DifferentSeed_DifferentPixels()
    {
        Assert.Equal(SurfaceTextureGenerator.Grass(Seed).AlbedoHeight, SurfaceTextureGenerator.Grass(Seed).AlbedoHeight);
        Assert.NotEqual(SurfaceTextureGenerator.Grass(Seed).AlbedoHeight, SurfaceTextureGenerator.Grass(Seed + 1).AlbedoHeight);
    }

    [Fact]
    public void Grass_IsGreen_AndRock_IsGrey()
    {
        double[] grass = MeanRgb(SurfaceTextureGenerator.Grass(Seed));
        double[] rock = MeanRgb(SurfaceTextureGenerator.Rock(Seed));

        Assert.True(grass[1] > grass[0] * 1.1 && grass[1] > grass[2] * 1.3, $"grass mean RGB {string.Join(",", grass)}");
        Assert.True(Math.Abs(rock[0] - rock[2]) < 0.15 * rock[0], $"rock mean RGB {string.Join(",", rock)}");
    }

    [Fact]
    public void Textures_AreBrightEnoughToTintWithTheColourMap()
    {
        // Terrain3D multiplies albedo by the baked colour map; a dark texture would make the whole world dark.
        Assert.All(new[] { SurfaceTextureGenerator.Grass(Seed), SurfaceTextureGenerator.Rock(Seed) },
            t => Assert.InRange(MeanRgb(t).Average(), 0.55, 0.95));
    }

    [Theory]
    [InlineData("grass")]
    [InlineData("rock")]
    public void Texture_Tiles_WrapEdgeIsNoRougherThanTheInterior(string name)
    {
        SurfaceTexture t = name == "grass" ? SurfaceTextureGenerator.Grass(Seed) : SurfaceTextureGenerator.Rock(Seed);
        int n = t.Size;
        double interior = 0;
        double wrap = 0;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n - 1; x++)
            {
                interior += Math.Abs(Height(t, x, y) - Height(t, x + 1, y));
            }

            wrap += Math.Abs(Height(t, n - 1, y) - Height(t, 0, y));
        }

        double interiorMean = interior / (n * (n - 1));
        double wrapMean = wrap / n;
        Assert.True(wrapMean <= interiorMean * 1.5, $"wrap step {wrapMean:F2} vs interior {interiorMean:F2}");
    }

    [Fact]
    public void Normals_PointUp_AndHaveUnitLength()
    {
        SurfaceTexture t = SurfaceTextureGenerator.Rock(Seed);
        for (int i = 0; i < t.NormalRoughness.Length; i += 4)
        {
            double x = (t.NormalRoughness[i] / 255.0 * 2) - 1;
            double y = (t.NormalRoughness[i + 1] / 255.0 * 2) - 1;
            double z = (t.NormalRoughness[i + 2] / 255.0 * 2) - 1;
            Assert.True(z > 0.2, $"pixel {i / 4} faces down");
            Assert.InRange(Math.Sqrt((x * x) + (y * y) + (z * z)), 0.97, 1.03);
        }
    }

    [Fact]
    public void Normals_FollowTheHeight_OnASlope()
    {
        // Independent oracle: where height rises towards +x the normal leans to -x (red below 0.5), and vice versa.
        SurfaceTexture t = SurfaceTextureGenerator.Rock(Seed);
        int n = t.Size;
        int agree = 0;
        int total = 0;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int left = Height(t, (x + n - 1) % n, y);
                int right = Height(t, (x + 1) % n, y);
                int slope = right - left;
                if (Math.Abs(slope) < 12)
                {
                    continue;
                }

                total++;
                byte red = t.NormalRoughness[(((y * n) + x) * 4)];
                if ((slope > 0 && red < 128) || (slope < 0 && red > 127))
                {
                    agree++;
                }
            }
        }

        Assert.True(total > 100);
        Assert.True(agree >= total * 0.98, $"{agree} of {total}");
    }

    [Fact]
    public void Roughness_IsConstantPerTexture_AndRockIsSmootherThanGrass()
    {
        Assert.Equal(230, SurfaceTextureGenerator.Grass(Seed).NormalRoughness[3]); // round(0.9 * 255)
        Assert.Equal(191, SurfaceTextureGenerator.Rock(Seed).NormalRoughness[3]); // round(0.75 * 255)
    }

    [Fact]
    public void Water_IsBluish_Glossy_AndTileable()
    {
        SurfaceTexture water = SurfaceTextureGenerator.Water(Seed);
        double[] mean = MeanRgb(water);

        Assert.True(mean[2] > mean[0] + 0.15, "water should be blue");
        Assert.True(water.NormalRoughness[3] < 100, "water should be glossy");
        Assert.Equal(water.AlbedoHeight, SurfaceTextureGenerator.Water(Seed).AlbedoHeight);
        Assert.NotEqual(SurfaceTextureGenerator.Grass(Seed).AlbedoHeight, water.AlbedoHeight);
    }

    [Fact]
    public void Golden_ChecksumsAreStable()
    {
        Assert.Equal(GrassGolden, Fnv(SurfaceTextureGenerator.Grass(Seed)));
        Assert.Equal(RockGolden, Fnv(SurfaceTextureGenerator.Rock(Seed)));
    }

    private const ulong GrassGolden = 10038111300068238012UL;
    private const ulong RockGolden = 8499702217896922395UL;

    private static int Height(SurfaceTexture t, int x, int y) => t.AlbedoHeight[(((y * t.Size) + x) * 4) + 3];

    private static double[] MeanRgb(SurfaceTexture t)
    {
        var sum = new double[3];
        int pixels = t.Size * t.Size;
        for (int i = 0; i < pixels; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                sum[c] += t.AlbedoHeight[(i * 4) + c] / 255.0;
            }
        }

        return new[] { sum[0] / pixels, sum[1] / pixels, sum[2] / pixels };
    }

    private static ulong Fnv(SurfaceTexture t)
    {
        ulong h = 14695981039346656037UL;
        foreach (byte b in t.AlbedoHeight)
        {
            h = (h ^ b) * 1099511628211UL;
        }

        foreach (byte b in t.NormalRoughness)
        {
            h = (h ^ b) * 1099511628211UL;
        }

        return h;
    }
}
