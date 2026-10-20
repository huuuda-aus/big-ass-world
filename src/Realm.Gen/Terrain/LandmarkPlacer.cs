using System;
using System.Collections.Generic;
using Realm.Core.Ids;
using Realm.Core.Random;

namespace Realm.Gen.Terrain;

/// <summary>
/// Scatters landmarks (lonely hills, mountains, basins) over the world according to each biome's densities. The world is
/// cut into 1 km cells; every cell independently may hold one landmark of each kind, so results do not depend on
/// evaluation order and one biome's change never moves landmarks elsewhere.
/// </summary>
/// <remarks>
/// Determinism: <c>Rng.ForKey(seed, "terrain", "landmark", kind, cellX, cellZ)</c> with a fixed draw order.
/// Thread-safety: pure.
/// </remarks>
public static class LandmarkPlacer
{
    /// <summary>Side of a scatter cell in metres; one cell is exactly one square kilometre.</summary>
    public const int CellMeters = 1000;

    /// <summary>Landmarks vary in size by up to this factor either way (height and radius each).</summary>
    public const double SizeVariation = 0.3;

    private const double SquareKmPer100 = 100.0;
    private const double FootprintReach = 1.3; // the warped footprint reaches a little past the nominal radius
    private const int CompassPoints = 8;

    // A landmark whose footprint crosses into another region would make a steep step across the region border, so
    // landmarks only appear where the whole footprint lies inside one region.
    private static bool StaysInRegion(RegionField regions, double x, double z, double reach)
    {
        ContentId own = regions.RegionAt(x, z);
        double limit = regions.WorldSizeMeters / 2.0;
        for (int i = 0; i < CompassPoints; i++)
        {
            double angle = 2 * Math.PI * i / CompassPoints;
            double px = x + (reach * Math.Cos(angle));
            double pz = z + (reach * Math.Sin(angle));
            if (Math.Abs(px) >= limit || Math.Abs(pz) >= limit || regions.RegionAt(px, pz) != own)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Places the landmarks.</summary>
    /// <param name="regions">Region ownership of the world.</param>
    /// <param name="perRegion">Terrain parameters per region, indexed like <see cref="RegionField.Regions"/>.</param>
    /// <param name="seed">World seed.</param>
    /// <returns>Landmarks in cell order, then kind order (a stable order).</returns>
    public static IReadOnlyList<Landmark> Place(RegionField regions, IReadOnlyList<BiomeTerrainParameters?> perRegion, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(perRegion);

        var index = new Dictionary<ContentId, int>();
        for (int i = 0; i < regions.Regions.Count; i++)
        {
            index[regions.Regions[i]] = i;
        }

        double half = regions.WorldSizeMeters / 2.0;
        int cells = (regions.WorldSizeMeters + CellMeters - 1) / CellMeters; // the last row and column may stick out of the world
        var result = new List<Landmark>();
        for (int cz = 0; cz < cells; cz++)
        {
            for (int cx = 0; cx < cells; cx++)
            {
                foreach (LandmarkKind kind in Enum.GetValues<LandmarkKind>())
                {
                    Rng rng = Rng.ForKey(seed, "terrain", "landmark", kind.ToString(), cx, cz);
                    double roll = rng.NextDouble();
                    double x = -half + ((cx + rng.NextDouble()) * CellMeters);
                    double z = -half + ((cz + rng.NextDouble()) * CellMeters);
                    double heightScale = 1 + (SizeVariation * ((rng.NextDouble() * 2) - 1));
                    double radiusScale = 1 + (SizeVariation * ((rng.NextDouble() * 2) - 1));

                    if (x >= half || z >= half)
                    {
                        continue; // in the part of the last cell outside the world
                    }

                    BiomeTerrainParameters? biome = perRegion[index[regions.RegionAt(x, z)]];
                    if (biome is null || biome.IsOcean)
                    {
                        continue;
                    }

                    foreach (LandmarkSpec spec in biome.Landmarks)
                    {
                        if (spec.Kind != kind || roll >= spec.DensityPer100Km2 / SquareKmPer100)
                        {
                            continue;
                        }

                        double radius = spec.RadiusMeters * radiusScale;
                        if (StaysInRegion(regions, x, z, radius * FootprintReach))
                        {
                            result.Add(new Landmark(kind, x, z, spec.HeightMeters * heightScale, radius));
                        }
                    }
                }
            }
        }

        return result;
    }
}
