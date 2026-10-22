using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Random;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Terrain;

namespace Realm.Gen.Foliage;

/// <summary>
/// Scatters trees and bushes over the finished world and makes a grass density map (CLAUDE.md section 6.1, step 11). Each species
/// has its own jittered candidate grid; a candidate is kept with a probability that follows the biome density, the species'
/// clumping noise and its height and slope limits, and is dropped on water, roads and settlements.
/// </summary>
/// <remarks>
/// Determinism: every candidate draws from <see cref="Rng"/> seeded by the world seed, the species ID and the candidate's cell, so
/// adding a species or changing another species' density never moves existing plants. Species and cells are processed in a fixed
/// order. Thread-safety: pure.
/// </remarks>
public static class FoliageGenerator
{
    /// <summary>Generator identifier recorded with baked output.</summary>
    public const string GeneratorId = "gen.foliage";

    /// <summary>Generator version; bump when output changes.</summary>
    public const int GeneratorVersion = 2;

    private const double SlopeProbeMeters = 1.5;
    private const double HectareSquareMeters = 10000;
    private const int ClusterOctaves = 3;
    private const double ClusterLacunarity = 2;
    private const double ClusterGain = 0.5;
    private const double ClumpLow = 0.35;
    private const double ClumpHigh = 0.65;
    private const double MaxTufts = 255;
    private const double SizeSkew = 1.5; // above 1 favours the smaller sizes, so a forest has many young trees and few giants
    private const ulong CellMixX = 0x9E3779B97F4A7C15UL;
    private const ulong CellMixZ = 0xC2B2AE3D27D4EB4FUL;

    /// <summary>Generates the foliage.</summary>
    /// <param name="map">The macro map (region to biome).</param>
    /// <param name="regions">The rasterised regions.</param>
    /// <param name="species">All foliage species by ID.</param>
    /// <param name="field">The finished terrain (after settlements and roads).</param>
    /// <param name="exclusion">Where plants must keep clear.</param>
    /// <param name="seed">World seed.</param>
    /// <param name="settings">Tuning; defaults when null.</param>
    /// <param name="report">Receives problems.</param>
    /// <returns>The foliage.</returns>
    public static FoliageData Generate(
        MacroMapDefinition map,
        RegionField regions,
        IReadOnlyDictionary<ContentId, FoliageDefinition> species,
        HeightField field,
        FoliageExclusion exclusion,
        ulong seed,
        FoliageSettings? settings,
        ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(species);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(exclusion);
        ArgumentNullException.ThrowIfNull(report);
        settings ??= new FoliageSettings();

        // Region index (as stored in the raster) to biome.
        var biomeOf = new Dictionary<ContentId, ContentId>();
        foreach (RegionDefinition region in map.Regions)
        {
            biomeOf[region.Id] = region.Biome;
        }

        List<FoliageDefinition> ordered = species.Values.OrderBy(s => s.Id.Value, StringComparer.Ordinal).ToList();
        var table = ordered.Select(s => new FoliageSpecies(s.Id.Value, s.Kind, s.Tags.ToList(), s.TrunkRadiusMeters)).ToList();
        int blurCells = ParameterGrid.PassRadiusCells(settings.BlendMeters, regions.CellMeters);

        var instances = new List<FoliageInstance>();
        int grassSide = (field.WorldSizeMeters + settings.GrassCellMeters - 1) / settings.GrassCellMeters;
        var grassBest = new double[grassSide * grassSide];
        var grassSpecies = new byte[grassSide * grassSide];

        for (int index = 0; index < ordered.Count; index++)
        {
            FoliageDefinition def = ordered[index];
            ParameterGrid density = DensityGrid(def, regions, biomeOf, blurCells);
            var noise = new GradientNoise(Rng.ForKey(seed, "foliage", def.Id.Value, "cluster"));
            ulong key = Rng.ForKey(seed, "foliage", def.Id.Value).NextUInt64();
            if (def.Kind == FoliageVocabulary.Grass)
            {
                ScatterGrass(def, index, density, noise, field, exclusion, settings, grassSide, grassBest, grassSpecies);
            }
            else
            {
                ScatterPlants(def, index, density, noise, key, field, exclusion, settings, instances);
            }
        }

        int chunk = settings.ChunkMeters;
        int chunks = (field.WorldSizeMeters + chunk - 1) / chunk;
        double half = field.WorldSizeMeters / 2.0;
        int ChunkOf(double v) => Math.Clamp((int)Math.Floor((v + half) / chunk), 0, chunks - 1);
        FoliageInstance[] sorted = instances
            .Select((inst, i) => (inst, i))
            .OrderBy(p => ChunkOf(p.inst.Z)).ThenBy(p => ChunkOf(p.inst.X)).ThenBy(p => p.inst.Species).ThenBy(p => p.i)
            .Select(p => p.inst)
            .ToArray();

        var tufts = new byte[grassBest.Length];
        for (int i = 0; i < tufts.Length; i++)
        {
            tufts[i] = (byte)Math.Min(MaxTufts, Math.Round(grassBest[i]));
        }

        return new FoliageData(field.WorldSizeMeters, chunk, table, sorted, settings.GrassCellMeters, grassSpecies, tufts);
    }

    /// <summary>Whether a species may grow at a place, judged on the ground alone (height and slope).</summary>
    /// <param name="def">The species.</param>
    /// <param name="field">The terrain.</param>
    /// <param name="x">World east in metres.</param>
    /// <param name="z">World south in metres.</param>
    /// <param name="settings">Sea margin.</param>
    /// <param name="height">The ground height there.</param>
    /// <returns><c>true</c> when the height and slope suit the species.</returns>
    public static bool GroundSuits(FoliageDefinition def, HeightField field, double x, double z, FoliageSettings settings, out double height)
    {
        height = Sample(field, x, z);
        if (height < Math.Max(def.MinHeightMeters, settings.MinHeightMeters) || height > def.MaxHeightMeters)
        {
            return false;
        }

        double dx = (Sample(field, x + SlopeProbeMeters, z) - Sample(field, x - SlopeProbeMeters, z)) / (2 * SlopeProbeMeters);
        double dz = (Sample(field, x, z + SlopeProbeMeters) - Sample(field, x, z - SlopeProbeMeters)) / (2 * SlopeProbeMeters);
        double slopeDegrees = Math.Atan(Math.Sqrt((dx * dx) + (dz * dz))) * 180.0 / Math.PI;
        return slopeDegrees <= def.MaxSlopeDegrees;
    }

    private static ParameterGrid DensityGrid(FoliageDefinition def, RegionField regions, Dictionary<ContentId, ContentId> biomeOf, int blurCells)
    {
        IReadOnlyList<ContentId> ids = regions.Regions;
        var perRegion = new double[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            perRegion[i] = biomeOf.TryGetValue(ids[i], out ContentId biome) ? def.DensityIn(biome) : 0;
        }

        ReadOnlySpan<ushort> cells = regions.Cells;
        var values = new double[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            values[i] = cells[i] == RegionField.Unassigned ? 0 : perRegion[cells[i] - 1];
        }

        var grid = new ParameterGrid(regions.CellsPerSide, regions.CellMeters, values);
        grid.Blur(blurCells);
        return grid;
    }

    private static void ScatterPlants(
        FoliageDefinition def, int index, ParameterGrid density, GradientNoise noise, ulong key,
        HeightField field, FoliageExclusion exclusion, FoliageSettings settings, List<FoliageInstance> output)
    {
        double max = def.MaxDensity;
        double cell = Math.Sqrt(HectareSquareMeters / max);
        int cells = (int)Math.Ceiling(field.WorldSizeMeters / cell);
        double half = field.WorldSizeMeters / 2.0;
        double spacing = def.MinSpacingMeters;
        var taken = new Dictionary<(int, int), List<(double X, double Z)>>();

        for (int cz = 0; cz < cells; cz++)
        {
            for (int cx = 0; cx < cells; cx++)
            {
                double centreX = -half + ((cx + 0.5) * cell);
                double centreZ = -half + ((cz + 0.5) * cell);
                if (density.Sample(centreX, centreZ) <= 0)
                {
                    continue;
                }

                var rng = new Rng(key ^ ((ulong)(uint)cx * CellMixX) ^ ((ulong)(uint)cz * CellMixZ));
                double x = centreX + ((rng.NextDouble() - 0.5) * settings.Jitter * cell);
                double z = centreZ + ((rng.NextDouble() - 0.5) * settings.Jitter * cell);
                double keep = rng.NextDouble();
                double yaw = rng.NextDouble() * 2 * Math.PI;
                double scale = def.MinScale + (Math.Pow(rng.NextDouble(), SizeSkew) * (def.MaxScale - def.MinScale));
                if (x < -half || x > half || z < -half || z > half)
                {
                    continue;
                }

                double local = density.Sample(x, z) * Clump(def, noise, x, z);
                if (keep >= local / max || exclusion.IsBlocked(x, z) || !GroundSuits(def, field, x, z, settings, out double y))
                {
                    continue;
                }

                if (TooClose(taken, x, z, spacing))
                {
                    continue;
                }

                var bucket = (((int)Math.Floor(x / spacing)), (int)Math.Floor(z / spacing));
                if (!taken.TryGetValue(bucket, out List<(double, double)>? list))
                {
                    taken[bucket] = list = new List<(double, double)>();
                }

                list.Add((x, z));
                output.Add(new FoliageInstance(index, x, z, y, yaw, scale));
            }
        }
    }

    private static bool TooClose(Dictionary<(int, int), List<(double X, double Z)>> taken, double x, double z, double spacing)
    {
        int bx = (int)Math.Floor(x / spacing);
        int bz = (int)Math.Floor(z / spacing);
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (taken.TryGetValue((bx + dx, bz + dz), out List<(double X, double Z)>? list))
                {
                    foreach ((double ox, double oz) in list)
                    {
                        if (((ox - x) * (ox - x)) + ((oz - z) * (oz - z)) < spacing * spacing)
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static void ScatterGrass(
        FoliageDefinition def, int index, ParameterGrid density, GradientNoise noise, HeightField field,
        FoliageExclusion exclusion, FoliageSettings settings, int side, double[] best, byte[] species)
    {
        double cell = settings.GrassCellMeters;
        double half = field.WorldSizeMeters / 2.0;
        double areaHectares = cell * cell / HectareSquareMeters;
        for (int cz = 0; cz < side; cz++)
        {
            for (int cx = 0; cx < side; cx++)
            {
                double x = -half + ((cx + 0.5) * cell);
                double z = -half + ((cz + 0.5) * cell);
                double perHectare = density.Sample(x, z);
                if (perHectare <= 0 || BlockedAround(exclusion, x, z, cell) || !GroundSuits(def, field, x, z, settings, out _))
                {
                    continue;
                }

                double tufts = perHectare * Clump(def, noise, x, z) * areaHectares;
                int i = (cz * side) + cx;
                if (tufts > best[i])
                {
                    best[i] = tufts;
                    species[i] = (byte)index;
                }
            }
        }
    }

    // A cell is clear only if its centre and the middle of each side are: a road across a corner would otherwise grow tufts on the road.
    private static bool BlockedAround(FoliageExclusion exclusion, double x, double z, double cell)
    {
        double h = cell / 2;
        return exclusion.IsBlocked(x, z) || exclusion.IsBlocked(x - h, z) || exclusion.IsBlocked(x + h, z) || exclusion.IsBlocked(x, z - h) || exclusion.IsBlocked(x, z + h)
            || exclusion.IsBlocked(x - h, z - h) || exclusion.IsBlocked(x + h, z + h) || exclusion.IsBlocked(x - h, z + h) || exclusion.IsBlocked(x + h, z - h);
    }

    // Clumping: 1 - strength in the gaps, up to 1 + strength inside a clump (so the average stays near the biome density).
    private static double Clump(FoliageDefinition def, GradientNoise noise, double x, double z)
    {
        if (def.ClusterMeters <= 0 || def.ClusterStrength <= 0)
        {
            return 1;
        }

        double n = Math.Clamp(0.5 + (0.5 * noise.Fractal(x / def.ClusterMeters, z / def.ClusterMeters, ClusterOctaves, ClusterLacunarity, ClusterGain)), 0, 1);
        double t = Math.Clamp((n - ClumpLow) / (ClumpHigh - ClumpLow), 0, 1);
        double smooth = t * t * (3 - (2 * t));
        return (1 - def.ClusterStrength) + (2 * def.ClusterStrength * smooth);
    }

    private static double Sample(HeightField field, double x, double z)
    {
        double half = field.WorldSizeMeters / 2.0;
        return field.Sample(Math.Clamp(x, -half, half), Math.Clamp(z, -half, half));
    }
}
