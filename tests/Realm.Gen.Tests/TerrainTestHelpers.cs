using System.Collections.Generic;
using System.Linq;
using Realm.Core.Geometry;
using Realm.Core.Ids;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Terrain;
using Xunit;

namespace Realm.Gen.Tests;

/// <summary>Builders for small test worlds.</summary>
internal static class TerrainTestHelpers
{
    public const int Cell = 64;
    public const int PinnedBlendMeters = 256;

    public static Polygon Rect(double x0, double z0, double x1, double z1)
    {
        var pts = new[] { new Point2D(x0, z0), new Point2D(x1, z0), new Point2D(x1, z1), new Point2D(x0, z1) };
        Assert.True(Polygon.TryCreate(pts, out Polygon? p, out string? problem), problem);
        return p!;
    }

    public static BiomeDefinition Biome(string id, bool ocean, params (string Name, double Value)[] properties) =>
        new(
            ContentId.Parse(id),
            id,
            TagSet.From(ocean ? new[] { "ocean" } : new[] { "land" }),
            properties.ToDictionary(p => p.Name, p => p.Value));

    public static BiomeDefinition Land(string id, double baseHeight, double relief, double roughness) =>
        Biome(id, false, ("base_height_m", baseHeight), ("relief_m", relief), ("roughness", roughness));

    public static BiomeDefinition Sea(string id, double depth, double relief, double roughness) =>
        Biome(id, true, ("water_depth_m", depth), ("relief_m", relief), ("roughness", roughness));

    public static RegionDefinition Region(string id, string biome, params Polygon[] polygons) =>
        new(ContentId.Parse(id), id, ContentId.Parse(biome), polygons);

    public static IReadOnlyDictionary<ContentId, BiomeDefinition> Biomes(params BiomeDefinition[] biomes) =>
        biomes.ToDictionary(b => b.Id);

    /// <summary>A 1,024 m world: land in the west half, sea in the east half, border at x = 0.</summary>
    public static MacroMapDefinition LandAndSea() => new(
        ContentId.Parse("world.test"),
        "Test",
        1024,
        new[]
        {
            Region("region.land", "biome.land", Rect(-512, -512, 0, 512)),
            Region("region.sea", "biome.sea", Rect(0, -512, 512, 512)),
        });

    public static RegionField Rasterize(MacroMapDefinition map)
    {
        var report = new ValidationReport();
        RegionField field = MacroRasterizer.Rasterize(map, report, Cell);
        Assert.Empty(report.Issues);
        return field;
    }

    public static HeightField? Generate(
        MacroMapDefinition map,
        IReadOnlyDictionary<ContentId, BiomeDefinition> biomes,
        ValidationReport report,
        ulong seed = 1,
        HeightFieldSettings? settings = null)
    {
        RegionField regions = Rasterize(map);
        // The blend is pinned so oracle tests do not depend on the tuning default (HeightFieldSettings.DefaultBlendRadiusMeters).
        return HeightFieldGenerator.Generate(
            map, regions, biomes, seed, settings ?? new HeightFieldSettings { SpacingMeters = 16, BlendRadiusMeters = PinnedBlendMeters }, report);
    }
}
