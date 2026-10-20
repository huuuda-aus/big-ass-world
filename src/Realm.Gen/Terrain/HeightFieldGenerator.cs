using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Realm.Core.Ids;
using Realm.Core.Random;
using Realm.Core.Validation;
using Realm.Data.Content;

namespace Realm.Gen.Terrain;

/// <summary>
/// Builds terrain heights from the macro map (CLAUDE.md section 6.1, step 2): each region's biome gives a base
/// height, a relief and a roughness; these are blended smoothly across region borders and combined with
/// fractal gradient noise: <c>height = base + relief * noise</c>.
/// </summary>
/// <remarks>
/// Determinism: identified by <see cref="GeneratorId"/>, <see cref="GeneratorVersion"/>, the world seed, the
/// settings and the input content. Noise is derived with <c>Rng.ForKey(seed, "terrain", "height")</c>. Each
/// vertex depends only on its own position, so parallel evaluation does not change the result.
/// Thread-safety: stateless; runs its own worker tasks internally.
/// </remarks>
public static class HeightFieldGenerator
{
    /// <summary>Identifies this generator in saved or baked data.</summary>
    public const string GeneratorId = "terrain.height_field";

    /// <summary>
    /// Bump when the output for the same input changes, and regenerate golden tests in the same change
    /// (CLAUDE.md section 7.4).
    /// </summary>
    public const int GeneratorVersion = 1;

    private const double HalfDivisor = 2.0;

    /// <summary>Generates the height field and runs <see cref="HeightFieldValidator"/> on it.</summary>
    /// <param name="map">The macro map.</param>
    /// <param name="regions">Its rasterised regions; must be complete (no unassigned cells).</param>
    /// <param name="biomes">All loaded biomes.</param>
    /// <param name="worldSeed">Root seed of the world.</param>
    /// <param name="settings">Tuning; <see cref="HeightFieldSettings"/> defaults when null.</param>
    /// <param name="report">Receives every issue.</param>
    /// <returns>The field, or null when inputs are invalid (the report says why).</returns>
    public static HeightField? Generate(
        MacroMapDefinition map,
        RegionField regions,
        IReadOnlyDictionary<ContentId, BiomeDefinition> biomes,
        ulong worldSeed,
        HeightFieldSettings? settings,
        ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(report);
        settings ??= new HeightFieldSettings();

        string where = map.Id.Value;
        if (regions.CellsPerSide == 0 || regions.UnassignedCount() > 0)
        {
            report.Error(
                TerrainCodes.RegionFieldIncomplete,
                where,
                "The region field is empty or has unassigned cells, so heights cannot be generated.",
                "Fix the coverage errors reported by the macro map first.");
            return null;
        }

        if (!settings.TryValidate(map.WorldSizeMeters, out string? problem))
        {
            report.Error(TerrainCodes.HeightSettingsInvalid, where, problem!);
            return null;
        }

        BiomeTerrainParameters?[] perRegion = ResolveBiomes(map, regions, biomes, report);
        if (perRegion.Any(p => p is null))
        {
            return null;
        }

        int blurCells = ParameterGrid.PassRadiusCells(settings.BlendRadiusMeters, regions.CellMeters);
        ParameterGrid baseGrid = BuildGrid(regions, perRegion, p => p.BaseHeightMeters, blurCells);
        ParameterGrid reliefGrid = BuildGrid(regions, perRegion, p => p.ReliefMeters, blurCells);
        ParameterGrid roughnessGrid = BuildGrid(regions, perRegion, p => p.Roughness, blurCells);

        var noise = new GradientNoise(Rng.ForKey(worldSeed, "terrain", "height"));
        int side = (map.WorldSizeMeters / settings.SpacingMeters) + 1;
        double half = map.WorldSizeMeters / HalfDivisor;
        var heights = new float[side * side];

        Parallel.For(0, side, row =>
        {
            double z = -half + (row * (double)settings.SpacingMeters);
            for (int col = 0; col < side; col++)
            {
                double x = -half + (col * (double)settings.SpacingMeters);
                double baseHeight = baseGrid.Sample(x, z);
                double relief = reliefGrid.Sample(x, z);
                double gain = settings.MinGain + ((settings.MaxGain - settings.MinGain) * roughnessGrid.Sample(x, z));
                double n = noise.Fractal(
                    x / settings.FeatureWavelengthMeters,
                    z / settings.FeatureWavelengthMeters,
                    settings.Octaves,
                    settings.Lacunarity,
                    gain);
                heights[(row * side) + col] = (float)(baseHeight + (relief * n));
            }
        });

        var field = new HeightField(map.WorldSizeMeters, settings.SpacingMeters, heights);
        HeightFieldValidator.Validate(field, regions, report);
        return field;
    }

    private static BiomeTerrainParameters?[] ResolveBiomes(
        MacroMapDefinition map,
        RegionField regions,
        IReadOnlyDictionary<ContentId, BiomeDefinition> biomes,
        ValidationReport report)
    {
        // Indexed like regions.Regions (sorted by ID); each biome is reported once, in sorted order.
        var perRegion = new BiomeTerrainParameters?[regions.Regions.Count];
        var checkedBiomes = new Dictionary<ContentId, BiomeTerrainParameters?>();
        for (int i = 0; i < regions.Regions.Count; i++)
        {
            RegionDefinition region = map.Regions.First(r => r.Id == regions.Regions[i]);
            if (!biomes.TryGetValue(region.Biome, out BiomeDefinition? biome))
            {
                report.Error(
                    TerrainCodes.BiomeMissing,
                    map.Id.Value,
                    $"Region '{region.Id}' uses biome '{region.Biome}', which is not loaded.",
                    "Add the biome or fix the ID.");
                continue;
            }

            if (!checkedBiomes.TryGetValue(region.Biome, out BiomeTerrainParameters? parameters))
            {
                parameters = BiomeTerrainParameters.TryFrom(biome, report);
                checkedBiomes[region.Biome] = parameters;
            }

            perRegion[i] = parameters;
        }

        return perRegion;
    }

    private static ParameterGrid BuildGrid(
        RegionField regions,
        BiomeTerrainParameters?[] perRegion,
        Func<BiomeTerrainParameters, double> select,
        int blurCells)
    {
        ReadOnlySpan<ushort> cells = regions.Cells;
        var values = new double[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            // Safe: ResolveBiomes returned only when every entry is non-null, and the field has no unassigned cells.
            values[i] = select(perRegion[cells[i] - 1]!);
        }

        var grid = new ParameterGrid(regions.CellsPerSide, regions.CellMeters, values);
        grid.Blur(blurCells);
        return grid;
    }
}
