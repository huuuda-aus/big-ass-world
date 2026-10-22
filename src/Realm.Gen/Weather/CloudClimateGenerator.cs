using System;
using System.Collections.Generic;
using Realm.Core.Ids;
using Realm.Core.Random;
using Realm.Data.Content;
using Realm.Gen.Terrain;

namespace Realm.Gen.Weather;

/// <summary>
/// Makes the <see cref="CloudClimate"/> of a world: wetter biomes are cloudier and their clouds deeper, high ground gathers extra cloud
/// (mountain peaks wear hats), the open sea is moderately cloudy, and low-frequency noise breaks up the biome shapes so the borders are
/// soft and no two valleys are alike.
/// </summary>
/// <remarks>
/// Determinism: seeded through <see cref="Rng"/>; the same inputs give the same bytes. Thread-safety: pure.
/// </remarks>
public static class CloudClimateGenerator
{
    /// <summary>Generator identifier recorded with baked output.</summary>
    public const string GeneratorId = "gen.weather.clouds";

    /// <summary>Generator version; bump when output changes.</summary>
    public const int GeneratorVersion = 1;

    /// <summary>Cells per side of the climate maps (each about 80 m in the shipped world).</summary>
    public const int DefaultSide = 256;

    private const string RainfallProperty = "rainfall";
    private const double SeaCover = 0.5;
    private const double SeaThickness = 0.3;
    private const double BaseCover = 0.08;
    private const double CoverPerRain = 0.85;
    private const double ThicknessPerRain = 0.8;
    private const double MountainHeightStart = 250;
    private const double MountainHeightFull = 900;
    private const double MountainCover = 0.3;
    private const double MountainThickness = 0.35;
    private const double NoiseScaleMeters = 3500;
    private const double NoiseCover = 0.22;
    private const double NoiseThickness = 0.18;
    private const int BlendMeters = 1500;

    /// <summary>Generates the climate.</summary>
    /// <param name="map">The macro map (region to biome).</param>
    /// <param name="regions">The rasterised regions.</param>
    /// <param name="biomes">The biomes by ID.</param>
    /// <param name="field">The finished terrain, for the mountain effect.</param>
    /// <param name="seed">World seed.</param>
    /// <param name="side">Cells per side.</param>
    /// <param name="blendMeters">How far the climate of a biome blends into its neighbours across a border.</param>
    /// <returns>The climate.</returns>
    public static CloudClimate Generate(
        MacroMapDefinition map,
        RegionField regions,
        IReadOnlyDictionary<ContentId, BiomeDefinition> biomes,
        HeightField field,
        ulong seed,
        int side = DefaultSide,
        int blendMeters = BlendMeters)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(field);

        // Per macro cell: the climate of its region's biome.
        var biomeOf = new Dictionary<ContentId, ContentId>();
        foreach (RegionDefinition region in map.Regions)
        {
            biomeOf[region.Id] = region.Biome;
        }

        IReadOnlyList<ContentId> ids = regions.Regions;
        var coverOf = new double[ids.Count];
        var thicknessOf = new double[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            (coverOf[i], thicknessOf[i]) = BiomeClimate(biomeOf.TryGetValue(ids[i], out ContentId biomeId) && biomes.TryGetValue(biomeId, out BiomeDefinition? biome) ? biome : null);
        }

        ReadOnlySpan<ushort> cells = regions.Cells;
        var coverValues = new double[cells.Length];
        var thicknessValues = new double[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            int r = cells[i] == RegionField.Unassigned ? 0 : cells[i] - 1;
            coverValues[i] = coverOf[r];
            thicknessValues[i] = thicknessOf[r];
        }

        int blur = ParameterGrid.PassRadiusCells(blendMeters, regions.CellMeters);
        var coverGrid = new ParameterGrid(regions.CellsPerSide, regions.CellMeters, coverValues);
        var thicknessGrid = new ParameterGrid(regions.CellsPerSide, regions.CellMeters, thicknessValues);
        coverGrid.Blur(blur);
        thicknessGrid.Blur(blur);

        var noise = new GradientNoise(Rng.ForKey(seed, "weather", "clouds", "noise"));
        var cover = new byte[side * side];
        var thickness = new byte[side * side];
        double half = field.WorldSizeMeters / 2.0;
        double cell = (double)field.WorldSizeMeters / side;
        for (int row = 0; row < side; row++)
        {
            for (int col = 0; col < side; col++)
            {
                double x = -half + ((col + 0.5) * cell);
                double z = -half + ((row + 0.5) * cell);
                double height = Math.Max(0, field.Sample(Math.Clamp(x, -half, half), Math.Clamp(z, -half, half)));
                double mountain = SmoothStep(MountainHeightStart, MountainHeightFull, height);
                double wander = noise.Fractal(x / NoiseScaleMeters, z / NoiseScaleMeters, 3, 2, 0.5);
                int i = (row * side) + col;
                cover[i] = ToByte(coverGrid.Sample(x, z) + (MountainCover * mountain) + (NoiseCover * wander));
                thickness[i] = ToByte(thicknessGrid.Sample(x, z) + (MountainThickness * mountain) + (NoiseThickness * wander));
            }
        }

        return new CloudClimate(field.WorldSizeMeters, side, cover, thickness);
    }

    // Cover and thickness of a biome: from its rainfall property; the sea (no rainfall) gets fixed middling values.
    private static (double Cover, double Thickness) BiomeClimate(BiomeDefinition? biome)
    {
        if (biome is null || !biome.Properties.TryGetValue(RainfallProperty, out double rain))
        {
            return (SeaCover, SeaThickness);
        }

        return (BaseCover + (CoverPerRain * rain), ThicknessPerRain * rain);
    }

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - (2 * t));
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);
}
