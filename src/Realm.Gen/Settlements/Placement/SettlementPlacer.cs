using System;
using System.Collections.Generic;
using System.Globalization;
using Realm.Core.Random;
using Realm.Core.Validation;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;

namespace Realm.Gen.Settlements.Placement;

/// <summary>
/// Chooses where cities and towns stand: flat land above sea level, clear of rivers and lakes, away from each other
/// and from the world edge (CLAUDE.md section 6.1, step 6). Large cities are placed first, then towns.
/// </summary>
/// <remarks>
/// Determinism: identified by <see cref="GeneratorId"/>, <see cref="GeneratorVersion"/>, the world seed, the settings and
/// the input terrain. City seeds come from <c>Rng.ForKey(seed, "settlement", tier, index)</c>; candidates are visited in a fixed
/// order. Thread-safety: pure.
/// </remarks>
public static class SettlementPlacer
{
    /// <summary>Identifies this generator in saved or baked data.</summary>
    public const string GeneratorId = "settlement.placement";

    /// <summary>Bump when the output for the same input changes, and regenerate the golden tests in the same change.</summary>
    public const int GeneratorVersion = 3; // v3: natural ground (no flattening), steeper sites, mountain city

    private const int RingsPerCandidate = 3;
    private const int PointsPerRing = 8;
    private const double JitterShare = 0.02; // tie-breaking noise so equally good sites do not all line up on the grid
    private const double HalfDivisor = 2.0;
    private const int SeedMask = 0x7fffffff;

    /// <summary>Places settlements on the terrain.</summary>
    /// <param name="field">Final terrain heights (after erosion), before flattening.</param>
    /// <param name="network">Rivers and lakes, or null when the world has none.</param>
    /// <param name="worldSeed">Root seed of the world.</param>
    /// <param name="settings">Tuning; defaults when null.</param>
    /// <param name="report">Receives <see cref="SettlementCodes"/> issues.</param>
    /// <param name="highlandArea">Tells whether a world position is in mountainous terrain (where the mountain cities go); when null, ground at or above <see cref="SettlementSettings.MountainMinHeightMeters"/> counts.</param>
    /// <returns>The settlements: cities, then mountain cities, then towns. Fewer than requested when the land has no more suitable sites (warning SETL002).</returns>
    public static IReadOnlyList<Settlement> Place(
        HeightField field,
        RiverNetwork? network,
        ulong worldSeed,
        SettlementSettings? settings,
        ValidationReport report,
        Func<double, double, bool>? highlandArea = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(report);
        settings ??= new SettlementSettings();
        string where = "settlements";
        if (!settings.TryValidate(out string? problem))
        {
            report.Error(SettlementCodes.SettingsInvalid, where, problem!);
            return Array.Empty<Settlement>();
        }

        var water = new WaterMask(field.WorldSizeMeters, network);
        var placed = new List<Settlement>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        int requested = settings.Cities + settings.MountainCities + settings.Towns;
        for (int i = 0; i < requested; i++)
        {
            bool city = i < settings.Cities;
            bool mountain = !city && i < settings.Cities + settings.MountainCities;
            SettlementTier tier = city || mountain ? SettlementTier.City : SettlementTier.Town;
            string kind = city ? "city" : mountain ? "mountain" : "town";
            int index = city ? i : mountain ? i - settings.Cities : i - settings.Cities - settings.MountainCities;
            Settlement? settlement = PlaceOne(field, water, worldSeed, settings, tier, kind, index, mountain, highlandArea, placed, usedNames);
            if (settlement is not null)
            {
                placed.Add(settlement);
            }
        }

        if (placed.Count < requested)
        {
            report.Warning(
                SettlementCodes.TooFewPlaced,
                where,
                $"Only {placed.Count} of {requested} settlements found a suitable site.",
                "Allow steeper ground (MaxReliefPerRadius), reduce the minimum spacing, or ask for fewer or smaller settlements.");
        }

        return placed;
    }

    private static Settlement? PlaceOne(
        HeightField field, WaterMask water, ulong worldSeed, SettlementSettings settings, SettlementTier tier, string kind, int index,
        bool highland, Func<double, double, bool>? highlandArea, List<Settlement> placed, ISet<string> usedNames)
    {
        Rng seedRng = Rng.ForKey(worldSeed, "settlement", kind, index);
        int citySeed = (int)(seedRng.NextUInt64() & SeedMask);
        double rotation = seedRng.NextDouble() * 2 * Math.PI;
        int buildings = tier == SettlementTier.City ? settings.CityBuildings : settings.TownBuildings;
        CentrepieceKind centre = tier == SettlementTier.City ? CentrepieceKind.Fountain : CentrepieceKind.Market;
        var cityParams = new CityParams { Seed = citySeed, MaxBuildings = buildings, Cathedral = tier == SettlementTier.City, Centre = centre };

        City city = CityGenerator.Generate(cityParams);
        double wallRadius = 0;
        foreach (Vec2 corner in city.Wall)
        {
            wallRadius = Math.Max(wallRadius, corner.Length);
        }

        double pad = wallRadius + settings.PadMarginMeters;
        double half = field.WorldSizeMeters / HalfDivisor;
        double edge = pad + settings.BlendMeters + settings.EdgeMarginMeters;
        double reliefLimit = (highland ? settings.MountainMaxReliefPerRadius : settings.MaxReliefPerRadius) * pad;
        double minHeight = highland ? settings.MountainMinHeightMeters : settings.MinHeightMeters;
        double maxHeight = highland ? settings.MountainMaxHeightMeters : settings.MaxHeightMeters;

        double bestScore = double.NegativeInfinity;
        double bestX = 0;
        double bestZ = 0;
        double bestGround = 0;
        int steps = (int)Math.Floor(((2 * half) - (2 * edge)) / settings.CandidateStepMeters);
        for (int iz = 0; iz <= steps; iz++)
        {
            double z = -half + edge + (iz * settings.CandidateStepMeters);
            for (int ix = 0; ix <= steps; ix++)
            {
                double x = -half + edge + (ix * settings.CandidateStepMeters);
                if (!FarFromOthers(x, z, pad, placed, settings))
                {
                    continue;
                }

                if (highland && highlandArea is not null && !highlandArea(x, z))
                {
                    continue;
                }

                if (!TrySurvey(field, x, z, pad, minHeight, maxHeight, reliefLimit, out double relief, out double mean))
                {
                    continue;
                }

                if (water.AnyWithin(x, z, pad + settings.WaterClearanceMeters))
                {
                    continue;
                }

                double score = -relief / reliefLimit;
                if (water.AnyWithin(x, z, pad + settings.WaterClearanceMeters + settings.RiverAttractionMeters))
                {
                    score += settings.RiverBonus;
                }

                score += JitterShare * Rng.ForKey(worldSeed, "settlement", kind, index, "site", iz, ix).NextDouble();
                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = x;
                    bestZ = z;
                    bestGround = mean;
                }
            }
        }

        if (double.IsNegativeInfinity(bestScore))
        {
            return null;
        }

        string number = (index + 1).ToString(CultureInfo.InvariantCulture);
        string name = SettlementNamer.Generate(Rng.ForKey(worldSeed, "settlement", kind, index, "name"), tier, usedNames);
        return new Settlement(
            $"settlement.{kind}_{number}",
            name,
            tier,
            bestX,
            bestZ,
            Math.Round(rotation, 6),
            Math.Round(bestGround, 2),
            Math.Round(wallRadius, 2),
            Math.Round(pad, 2),
            citySeed,
            buildings,
            tier == SettlementTier.City,
            centre,
            null,
            highland);
    }

    private static bool FarFromOthers(double x, double z, double pad, List<Settlement> placed, SettlementSettings settings)
    {
        foreach (Settlement other in placed)
        {
            double needed = Math.Max(settings.MinSpacingMeters, pad + other.PadRadiusMeters + (2 * settings.BlendMeters));
            if (other.DistanceTo(x, z) < needed)
            {
                return false;
            }
        }

        return true;
    }

    // Samples the ground on rings across the pad: everything must be above the minimum height, below the maximum and
    // within the allowed relief. Returns the relief and the mean height (the settlement's ground height).
    private static bool TrySurvey(
        HeightField field, double x, double z, double pad, double minHeight, double maxHeight, double reliefLimit, out double relief, out double mean)
    {
        double min = field.Sample(x, z);
        double max = min;
        double sum = min;
        int count = 1;
        for (int ring = 1; ring <= RingsPerCandidate; ring++)
        {
            double radius = pad * ring / RingsPerCandidate;
            for (int p = 0; p < PointsPerRing; p++)
            {
                double angle = 2 * Math.PI * p / PointsPerRing;
                double h = field.Sample(x + (radius * Math.Cos(angle)), z + (radius * Math.Sin(angle)));
                min = Math.Min(min, h);
                max = Math.Max(max, h);
                sum += h;
                count++;
            }
        }

        relief = max - min;
        mean = sum / count;
        return min >= minHeight && max <= maxHeight && relief <= reliefLimit;
    }
}
