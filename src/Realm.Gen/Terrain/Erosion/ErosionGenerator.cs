using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Realm.Core.Ids;
using Realm.Core.Validation;
using Realm.Data.Content;

namespace Realm.Gen.Terrain.Erosion;

/// <summary>
/// Erodes a height field and finds its rivers and lakes (CLAUDE.md section 6.1, steps 3 and 4). Water is
/// routed over a coarse grid (depressions filled, steepest descent, rainfall accumulated downstream with
/// per-biome loss); an implicit stream-power law carves valleys along the flow; the carve is applied to the
/// full-resolution field; thermal relaxation then slumps slopes beyond each biome's stable angle. Rivers are
/// the paths where flow reaches a threshold.
/// </summary>
/// <remarks>
/// Determinism: there is no randomness; the result is a pure function of the input field, the biomes, the
/// settings and the generator version. Parallel loops evaluate independent vertices only.
/// Thread-safety: stateless. Memory: allocates several arrays of the full field size.
/// </remarks>
public static class ErosionGenerator
{
    /// <summary>Identifies this generator in saved or baked data.</summary>
    public const string GeneratorId = "terrain.erosion";

    /// <summary>
    /// Bump when the output for the same input changes, and regenerate golden tests in the same change
    /// (CLAUDE.md section 7.4).
    /// </summary>
    public const int GeneratorVersion = 1;

    private const float SeaLevel = 0f;
    private const double HalfDivisor = 2.0;
    private const double DegreesToRadians = Math.PI / 180.0;

    /// <summary>Erodes <paramref name="source"/> and extracts rivers and lakes, then validates the result.</summary>
    /// <param name="map">The macro map.</param>
    /// <param name="regions">Its rasterised regions; must be complete.</param>
    /// <param name="biomes">All loaded biomes.</param>
    /// <param name="source">The uneroded height field; not modified.</param>
    /// <param name="settings">Tuning; <see cref="ErosionSettings"/> defaults when null.</param>
    /// <param name="report">Receives every issue.</param>
    /// <returns>The eroded terrain, or null when inputs are invalid (the report says why).</returns>
    public static ErodedTerrain? Apply(
        MacroMapDefinition map,
        RegionField regions,
        IReadOnlyDictionary<ContentId, BiomeDefinition> biomes,
        HeightField source,
        ErosionSettings? settings,
        ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(report);
        settings ??= new ErosionSettings();

        string where = map.Id.Value;
        if (regions.CellsPerSide == 0 || regions.UnassignedCount() > 0 || source.WorldSizeMeters != map.WorldSizeMeters)
        {
            report.Error(
                TerrainCodes.RegionFieldIncomplete,
                where,
                "The region field is empty, has unassigned cells, or does not match the height field's world size.",
                "Fix the coverage errors reported by the macro map first.");
            return null;
        }

        if (!settings.TryValidate(map.WorldSizeMeters, source.SpacingMeters, out string? problem))
        {
            report.Error(TerrainCodes.ErosionSettingsInvalid, where, problem!);
            return null;
        }

        BiomeErosionParameters?[] perRegion = ResolveBiomes(map, regions, biomes, report);
        if (perRegion.Any(p => p is null))
        {
            return null;
        }

        int blurCells = ParameterGrid.PassRadiusCells(settings.BlendRadiusMeters, regions.CellMeters);
        ParameterGrid rainfallGrid = BuildGrid(regions, perRegion, p => p.Rainfall, blurCells);
        ParameterGrid erodibilityGrid = BuildGrid(regions, perRegion, p => p.Erodibility, blurCells);
        ParameterGrid slopeGrid = BuildGrid(regions, perRegion, p => p.MaxSlopeDegrees, blurCells);
        ParameterGrid lossGrid = BuildGrid(regions, perRegion, p => p.RiverLoss, blurCells);

        // Coarse working grid: every k-th vertex of the field.
        int k = settings.WorkingSpacingMeters / source.SpacingMeters;
        int workSide = (map.WorldSizeMeters / settings.WorkingSpacingMeters) + 1;
        double half = map.WorldSizeMeters / HalfDivisor;
        var original = new float[workSide * workSide];
        var rainfall = new double[original.Length];
        var erodibility = new double[original.Length];
        var loss = new double[original.Length];
        for (int z = 0; z < workSide; z++)
        {
            for (int x = 0; x < workSide; x++)
            {
                int i = (z * workSide) + x;
                double wx = -half + (x * (double)settings.WorkingSpacingMeters);
                double wz = -half + (z * (double)settings.WorkingSpacingMeters);
                original[i] = source.At(x * k, z * k);
                rainfall[i] = rainfallGrid.Sample(wx, wz);
                erodibility[i] = erodibilityGrid.Sample(wx, wz);
                loss[i] = lossGrid.Sample(wx, wz);
            }
        }

        float lakeMinDepth = (float)settings.LakeMinDepthMeters;
        float[] working = original;
        for (int iteration = 0; iteration < settings.Iterations; iteration++)
        {
            FlowRouting routing = FlowRouter.Route(working, workSide, settings.WorkingSpacingMeters, SeaLevel, lakeMinDepth);
            working = RiverExtractor.FillSmallBasins(routing, working, settings.MinLakeAreaKm2);
            double[] flow = FlowAccumulator.Accumulate(routing, rainfall, loss);
            working = StreamPowerEroder.Step(routing, working, flow, erodibility, settings.Strength);
        }

        FlowRouting finalRouting = FlowRouter.Route(working, workSide, settings.WorkingSpacingMeters, SeaLevel, lakeMinDepth);
        working = RiverExtractor.FillSmallBasins(finalRouting, working, settings.MinLakeAreaKm2);
        double[] finalFlow = FlowAccumulator.Accumulate(finalRouting, rainfall, loss);
        var network = new RiverNetwork(
            RiverExtractor.ExtractRivers(finalRouting, working, finalFlow, settings.RiverThresholdKm2, SeaLevel),
            RiverExtractor.ExtractLakes(finalRouting, settings.MinLakeAreaKm2),
            settings.WorkingSpacingMeters);

        // Carry the coarse carve to the full-resolution field.
        double maxCarve = 0;
        var delta = new float[original.Length];
        for (int i = 0; i < delta.Length; i++)
        {
            delta[i] = working[i] - original[i];
            maxCarve = Math.Max(maxCarve, -delta[i]);
        }

        int side = source.VerticesPerSide;
        float[] carved = ApplyCarve(source, delta, workSide, k);

        // Thermal relaxation at full resolution, with each vertex's stable slope from its biome (blurred).
        double[] stable = StableSlopes(slopeGrid, side, source.SpacingMeters, half);
        float[] relaxed = ThermalEroder.Relax(carved, side, source.SpacingMeters, stable, settings.ThermalIterations);

        var field = new HeightField(source.WorldSizeMeters, source.SpacingMeters, relaxed);
        HeightFieldValidator.Validate(field, regions, report);
        RiverValidator.Validate(network, map.WorldSizeMeters, report);
        return new ErodedTerrain(field, network, maxCarve);
    }

    private static float[] ApplyCarve(HeightField source, float[] delta, int workSide, int k)
    {
        int side = source.VerticesPerSide;
        var result = new float[side * side];
        ReadOnlySpan<float> heights = source.Heights;
        float[] baseHeights = heights.ToArray();
        Parallel.For(0, side, z =>
        {
            double gz = z / (double)k;
            int z0 = Math.Min((int)Math.Floor(gz), workSide - 2);
            double tz = gz - z0;
            for (int x = 0; x < side; x++)
            {
                double gx = x / (double)k;
                int x0 = Math.Min((int)Math.Floor(gx), workSide - 2);
                double tx = gx - x0;
                double d00 = delta[(z0 * workSide) + x0];
                double d10 = delta[(z0 * workSide) + x0 + 1];
                double d01 = delta[((z0 + 1) * workSide) + x0];
                double d11 = delta[((z0 + 1) * workSide) + x0 + 1];
                double top = d00 + (tx * (d10 - d00));
                double bottom = d01 + (tx * (d11 - d01));
                result[(z * side) + x] = (float)(baseHeights[(z * side) + x] + top + (tz * (bottom - top)));
            }
        });

        return result;
    }

    private static double[] StableSlopes(ParameterGrid slopeDegrees, int side, int spacing, double half)
    {
        var tangents = new double[side * side];
        Parallel.For(0, side, z =>
        {
            double wz = -half + (z * (double)spacing);
            for (int x = 0; x < side; x++)
            {
                double wx = -half + (x * (double)spacing);
                tangents[(z * side) + x] = Math.Tan(slopeDegrees.Sample(wx, wz) * DegreesToRadians);
            }
        });

        return tangents;
    }

    private static BiomeErosionParameters?[] ResolveBiomes(
        MacroMapDefinition map,
        RegionField regions,
        IReadOnlyDictionary<ContentId, BiomeDefinition> biomes,
        ValidationReport report)
    {
        var perRegion = new BiomeErosionParameters?[regions.Regions.Count];
        var checkedBiomes = new Dictionary<ContentId, BiomeErosionParameters?>();
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

            if (!checkedBiomes.TryGetValue(region.Biome, out BiomeErosionParameters? parameters))
            {
                parameters = BiomeErosionParameters.TryFrom(biome, report);
                checkedBiomes[region.Biome] = parameters;
            }

            perRegion[i] = parameters;
        }

        return perRegion;
    }

    private static ParameterGrid BuildGrid(
        RegionField regions,
        BiomeErosionParameters?[] perRegion,
        Func<BiomeErosionParameters, double> select,
        int blurCells)
    {
        ReadOnlySpan<ushort> cells = regions.Cells;
        var values = new double[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            // Safe: Apply returned earlier unless every entry is non-null, and the field has no unassigned cells.
            values[i] = select(perRegion[cells[i] - 1]!);
        }

        var grid = new ParameterGrid(regions.CellsPerSide, regions.CellMeters, values);
        grid.Blur(blurCells);
        return grid;
    }
}
