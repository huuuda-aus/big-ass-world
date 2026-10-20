using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Realm.Core.Validation;
using Realm.Gen.Bake;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;
using Xunit;

namespace Realm.Gen.Tests.Bake;

public class TerrainBakerTests
{
    // A 64 m world with 4 m spacing: 17 x 17 vertices. Regions of 4 vertices are 16 m wide: 4 x 4 regions, coordinates -2..1.
    private const int World = 64;
    private const int Spacing = 4;
    private const int RegionSize = 4;

    private static HeightField Field(Func<int, int, float> height)
    {
        int side = (World / Spacing) + 1;
        var heights = new float[side * side];
        for (int z = 0; z < side; z++)
        {
            for (int x = 0; x < side; x++)
            {
                heights[(z * side) + x] = height(x, z);
            }
        }

        return new HeightField(World, Spacing, heights);
    }

    // Distinct value per vertex: encodes the vertex index.
    private static HeightField Indexed() => Field((x, z) => (z * 100) + x);

    private static IEnumerable<RegionTile> AllTiles(HeightField field, RiverNetwork? network = null)
    {
        for (int rz = -2; rz <= 1; rz++)
        {
            for (int rx = -2; rx <= 1; rx++)
            {
                yield return TerrainBaker.BuildTile(field, network, rx, rz, RegionSize);
            }
        }
    }

    [Fact]
    public void RegionZeroZero_StartsAtTheWorldOrigin()
    {
        HeightField field = Indexed();

        RegionTile tile = TerrainBaker.BuildTile(field, null, 0, 0, RegionSize);

        // The world origin is the middle vertex (8, 8): heights there encode 8 * 100 + 8.
        Assert.Equal(808f, tile.Heights[0]);
        Assert.Equal((float)field.Sample(0, 0), tile.Heights[0]);
        Assert.Equal(field.At(8 + 3, 8 + 3), tile.Heights[(3 * RegionSize) + 3]);
    }

    [Fact]
    public void Tiles_AgreeWithTheField_AtEveryWorldPosition()
    {
        HeightField field = Indexed();
        double half = World / 2.0;

        foreach (RegionTile tile in AllTiles(field))
        {
            for (int iz = 0; iz < RegionSize; iz++)
            {
                for (int ix = 0; ix < RegionSize; ix++)
                {
                    double x = (tile.RegionX * tile.ExtentMeters) + (ix * (double)Spacing);
                    double z = (tile.RegionZ * tile.ExtentMeters) + (iz * (double)Spacing);
                    Assert.True(Math.Abs(x) <= half && Math.Abs(z) <= half);
                    Assert.Equal((float)field.Sample(x, z), tile.Heights[(iz * RegionSize) + ix]);
                }
            }
        }
    }

    [Fact]
    public void EveryFieldVertexExceptTheFarEdge_BelongsToExactlyOneTile()
    {
        HeightField field = Indexed();
        var seen = new HashSet<float>();

        foreach (RegionTile tile in AllTiles(field))
        {
            foreach (float h in tile.Heights)
            {
                Assert.True(seen.Add(h), $"height code {h} appears in two tiles");
            }
        }

        // 16 x 16 vertices; the 17th row and column (the world's far edge) are not part of any region.
        Assert.Equal(16 * 16, seen.Count);
        Assert.DoesNotContain(field.At(16, 0), seen);
        Assert.DoesNotContain(field.At(0, 16), seen);
    }

    [Fact]
    public void SeaFloorIsBlueish_AndHighGroundIsPale()
    {
        // West half below sea level, east half a tall peak.
        HeightField field = Field((x, _) => x < 8 ? -100f : 400f);

        RegionTile west = TerrainBaker.BuildTile(field, null, -2, 0, RegionSize);
        RegionTile east = TerrainBaker.BuildTile(field, null, 1, 0, RegionSize);

        uint sea = west.Colours[0];
        uint peak = east.Colours[0];
        Assert.Equal(0xFFu, sea >> 24);
        Assert.True(((sea >> 16) & 0xFF) > (sea & 0xFF), "sea should have more blue than red");
        Assert.True((peak & 0xFF) > (sea & 0xFF), "snow-capped ground is brighter than deep water");
    }

    [Fact]
    public void HillShading_DarkensSlopesFacingAwayFromTheLight_AndBrightensTheOthers()
    {
        // Heights rise toward the south-east (+x and +z): the slope faces north-west, toward the light, so it is lit...
        HeightField towardLight = Field((x, z) => 100f + ((x + z) * 0.4f)); // gentle: slope 0.1 per axis, below where rock colour starts
        // ...and the mirror image falls toward the south-east, facing away from the light.
        HeightField awayFromLight = Field((x, z) => 100f - ((x + z) * 0.4f));
        HeightField flat = Field((_, _) => 100f);

        double Brightness(HeightField f)
        {
            uint c = TerrainBaker.BuildTile(f, null, 0, 0, RegionSize).Colours[RegionSize + 1];
            return (c & 0xFF) + ((c >> 8) & 0xFF) + ((c >> 16) & 0xFF);
        }

        Assert.True(Brightness(towardLight) > Brightness(flat), "a slope facing the light should be brighter than flat ground");
        Assert.True(Brightness(awayFromLight) < Brightness(flat), "a slope facing away from the light should be darker");
    }

    [Fact]
    public void HillShading_MatchesAnIndependentFormula_AtEveryVertex_IncludingTileEdges()
    {
        HeightField field = Field((x, z) => (float)(50 + (30 * Math.Sin(x * 0.7) * Math.Cos(z * 0.5))));
        int last = field.VerticesPerSide - 1;
        double top = field.MaxHeight;
        double depth = -field.MinHeight;

        foreach (RegionTile tile in AllTiles(field))
        {
            int originX = (tile.RegionX + 2) * RegionSize;
            int originZ = (tile.RegionZ + 2) * RegionSize;
            for (int iz = 0; iz < RegionSize; iz++)
            {
                for (int ix = 0; ix < RegionSize; ix++)
                {
                    int vx = originX + ix;
                    int vz = originZ + iz;
                    // Neighbours come from the whole field, so a vertex on a tile edge sees across the edge.
                    double sx = (field.At(Math.Min(vx + 1, last), vz) - field.At(Math.Max(vx - 1, 0), vz)) / (2.0 * Spacing);
                    double sz = (field.At(vx, Math.Min(vz + 1, last)) - field.At(vx, Math.Max(vz - 1, 0))) / (2.0 * Spacing);
                    double shade = Math.Clamp(1.0 + (0.9 * (sx + sz)), 0.45, 1.35);
                    (double r, double g, double b) = TerrainColouring.Albedo(field.At(vx, vz), top, depth, Math.Sqrt((sx * sx) + (sz * sz)));

                    Assert.Equal(TerrainColouring.Pack(r * shade, g * shade, b * shade), tile.Colours[(iz * RegionSize) + ix]);
                }
            }
        }
    }

    [Fact]
    public void River_IsPaintedExactlyWithinItsHalfWidth()
    {
        HeightField field = Field((_, _) => 50f);
        // A river along z = 0 from x = -20 to 20 carrying 100 km2: half width max(2, 0.6 * sqrt(100)) = 6 m.
        var river = new River(
            0,
            new[] { new RiverPoint(-20, 0, 50, 100), new RiverPoint(20, 0, 50, 100) },
            RiverMouth.WorldEdge,
            null,
            40);
        var network = new RiverNetwork(new[] { river }, new List<Lake>());
        uint water = TerrainColouring.WaterPacked;

        foreach (RegionTile tile in AllTiles(field, network))
        {
            for (int iz = 0; iz < RegionSize; iz++)
            {
                for (int ix = 0; ix < RegionSize; ix++)
                {
                    double x = (tile.RegionX * tile.ExtentMeters) + (ix * (double)Spacing);
                    double z = (tile.RegionZ * tile.ExtentMeters) + (iz * (double)Spacing);
                    double distance = Math.Abs(z); // distance to the segment's line; the segment spans x in [-20, 20]
                    double alongX = Math.Max(Math.Abs(x) - 20, 0);
                    bool expected = Math.Sqrt((distance * distance) + (alongX * alongX)) <= 6;
                    Assert.Equal(expected, tile.Colours[(iz * RegionSize) + ix] == water);
                }
            }
        }
    }

    [Fact]
    public void Lake_IsPaintedAsItsCellSquares()
    {
        HeightField field = Field((_, _) => 50f);
        var lake = new Lake(0, 60, 0.001, 16, 16, new List<(double X, double Z)> { (16, 16) });
        var network = new RiverNetwork(new List<River>(), new[] { lake }, workingSpacingMeters: 8);

        int painted = 0;
        foreach (RegionTile tile in AllTiles(field, network))
        {
            for (int iz = 0; iz < RegionSize; iz++)
            {
                for (int ix = 0; ix < RegionSize; ix++)
                {
                    double x = (tile.RegionX * tile.ExtentMeters) + (ix * (double)Spacing);
                    double z = (tile.RegionZ * tile.ExtentMeters) + (iz * (double)Spacing);
                    bool expected = Math.Abs(x - 16) <= 4 && Math.Abs(z - 16) <= 4; // a 8 m cell centred on (16, 16)
                    bool isWater = tile.Colours[(iz * RegionSize) + ix] == TerrainColouring.WaterPacked;
                    Assert.Equal(expected, isWater);
                    painted += isWater ? 1 : 0;
                }
            }
        }

        Assert.Equal(9, painted); // 3 x 3 vertices at 4 m spacing fall inside a 8 m square including its edges
    }

    [Fact]
    public void Validation_RejectsBadRegionGeometry()
    {
        HeightField field = Indexed();

        Assert.True(TerrainBaker.TryValidate(field, 4, 32, out _));
        Assert.False(TerrainBaker.TryValidate(field, 1, 32, out string? tiny));
        Assert.Contains("at least 2", tiny);
        Assert.False(TerrainBaker.TryValidate(field, 5, 32, out string? notDividing));
        Assert.Contains("does not divide", notDividing);
        Assert.False(TerrainBaker.TryValidate(field, 4, 3, out string? tooMany));
        Assert.Contains("at most 3", tooMany);
        Assert.False(TerrainBaker.TryValidate(field, 16, 32, out string? odd)); // 64 / 64 = 1 region: odd count
        Assert.Contains("even number", odd);
    }

    [Fact]
    public void Bake_WritesAllTiles_AndAManifest_WithoutTemporaryFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), "realm-bake-" + Guid.NewGuid().ToString("N"));
        try
        {
            var river = new River(
                0,
                new[] { new RiverPoint(-20, -8, 50, 10), new RiverPoint(0, 0, 40, 12), new RiverPoint(20, 8, 30, 14) },
                RiverMouth.WorldEdge,
                null,
                50);
            var network = new RiverNetwork(new[] { river }, new List<Lake>());
            var report = new ValidationReport();

            TerrainManifest? manifest = TerrainBaker.Bake(Indexed(), network, "world.test", 7, new[] { "gen v1" }, dir, report, RegionSize);

            Assert.Empty(report.Issues);
            Assert.NotNull(manifest);
            Assert.Equal(4, manifest!.RegionsPerAxis);
            Assert.Equal(-2, manifest.MinRegion);
            Assert.Equal(1, manifest.MaxRegion);
            Assert.Equal(16, manifest.RegionExtentMeters);
            Assert.Equal(1, manifest.RiverCount);
            Assert.Equal(0, manifest.LakeCount);
            Assert.Equal(7UL, manifest.Seed);

            string[] files = Directory.GetFiles(dir).Select(f => Path.GetFileName(f)!).OrderBy(n => n).ToArray();
            Assert.Equal(16 + 1, files.Length);
            Assert.Contains("manifest.json", files);
            Assert.Contains("region_-2_-2.tile", files);
            Assert.Contains("region_1_1.tile", files);
            Assert.DoesNotContain(files, f => f.EndsWith(".tmp", StringComparison.Ordinal));

            RegionTile roundTrip = RegionTileFile.Read(Path.Combine(dir, "region_0_0.tile"));
            Assert.Equal(808f, roundTrip.Heights[0]);

            TerrainManifest parsed = TerrainManifest.FromJson(File.ReadAllText(Path.Combine(dir, "manifest.json")));
            Assert.Equal(manifest, parsed);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Bake_IsDeterministic_AndTheChecksumTracksContent()
    {
        string a = Path.Combine(Path.GetTempPath(), "realm-bake-" + Guid.NewGuid().ToString("N"));
        string b = Path.Combine(Path.GetTempPath(), "realm-bake-" + Guid.NewGuid().ToString("N"));
        string c = Path.Combine(Path.GetTempPath(), "realm-bake-" + Guid.NewGuid().ToString("N"));
        try
        {
            TerrainManifest first = TerrainBaker.Bake(Indexed(), null, "w", 1, Array.Empty<string>(), a, new ValidationReport(), RegionSize)!;
            TerrainManifest second = TerrainBaker.Bake(Indexed(), null, "w", 1, Array.Empty<string>(), b, new ValidationReport(), RegionSize)!;
            TerrainManifest changed = TerrainBaker.Bake(Field((x, z) => (z * 100) + x + (x == 3 && z == 5 ? 0.5f : 0)), null, "w", 1, Array.Empty<string>(), c, new ValidationReport(), RegionSize)!;

            Assert.Equal(first.Checksum, second.Checksum);
            Assert.NotEqual(first.Checksum, changed.Checksum);
            Assert.Equal(16, first.Checksum.Length);
            Assert.Equal(File.ReadAllBytes(Path.Combine(a, "region_-1_0.tile")), File.ReadAllBytes(Path.Combine(b, "region_-1_0.tile")));
        }
        finally
        {
            foreach (string dir in new[] { a, b, c })
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }
    }

    [Fact]
    public void Bake_ReportsInvalidSettings_AndWritesNothing()
    {
        string dir = Path.Combine(Path.GetTempPath(), "realm-bake-" + Guid.NewGuid().ToString("N"));
        var report = new ValidationReport();

        TerrainManifest? manifest = TerrainBaker.Bake(Indexed(), null, "world.test", 1, Array.Empty<string>(), dir, report, 5);

        Assert.Null(manifest);
        Assert.Equal(TerrainCodes.BakeSettingsInvalid, Assert.Single(report.Issues).Code);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void Spawn_IsBesideTheMiddleOfTheLongestRiver_ElseTheCentre()
    {
        var river = new River(
            0,
            new[] { new RiverPoint(-20, -8, 50, 10), new RiverPoint(0, 4, 40, 12), new RiverPoint(20, 8, 30, 14) },
            RiverMouth.WorldEdge,
            null,
            50);
        string a = Path.Combine(Path.GetTempPath(), "realm-bake-" + Guid.NewGuid().ToString("N"));
        string b = Path.Combine(Path.GetTempPath(), "realm-bake-" + Guid.NewGuid().ToString("N"));
        try
        {
            TerrainManifest withRiver = TerrainBaker.Bake(Indexed(), new RiverNetwork(new[] { river }, new List<Lake>()), "w", 1, Array.Empty<string>(), a, new ValidationReport(), RegionSize)!;
            TerrainManifest without = TerrainBaker.Bake(Indexed(), null, "w", 1, Array.Empty<string>(), b, new ValidationReport(), RegionSize)!;

            Assert.Equal(16, withRiver.SpawnX); // middle point x = 0 plus four vertex spacings
            Assert.Equal(4, withRiver.SpawnZ);
            Assert.Equal(0, without.SpawnX);
            Assert.Equal(0, without.SpawnZ);
        }
        finally
        {
            foreach (string dir in new[] { a, b })
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }
    }

    [Fact]
    public void Manifest_RejectsGarbage()
    {
        Assert.ThrowsAny<Exception>(() => TerrainManifest.FromJson("not json"));
        Assert.ThrowsAny<Exception>(() => TerrainManifest.FromJson("null"));
    }
}
