using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Random;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Generation;

/// <summary>
/// Picks which buildings of a city are its service buildings (inn, blacksmith, ...). Inside the wall a service takes over an existing
/// lot, chosen by ward preference and nearness to the square, a gate or the wall. Services outside the wall get a new building on a
/// farmland patch next to the wall.
/// </summary>
/// <remarks>
/// Determinism: depends only on the city plan, the specs and the rng; the specs are processed in a fixed order. Thread-safety: pure.
/// </remarks>
public static class ServiceAssigner
{
    /// <summary>Furthest a service building stands from a gate it prefers, as the distance that costs one ward-rank step, in metres.</summary>
    private const double MetresPerRankStep = 250;

    /// <summary>Random spread added to every candidate's score, as a share of one rank step.</summary>
    private const double JitterShare = 0.15;

    /// <summary>Clear ground kept between a farm building and the edge of its patch, in metres.</summary>
    private const double FarmMargin = 6;

    /// <summary>Clear ground kept between a farm building and a gate, so roads reach the gate, in metres.</summary>
    private const double GateClearance = 30;

    /// <summary>How much a farm footprint shrinks each time it does not fit its patch.</summary>
    private const double FarmShrink = 0.85;

    /// <summary>Two buildings of the same service closer than this are spread apart, in metres.</summary>
    private const double SameServiceSpacingMeters = 120;

    /// <summary>How many rank steps being right on top of another of the same service costs.</summary>
    private const double SameServicePenalty = 1.5;

    private const int FarmFitAttempts = 6;
    private const double FarmAspect = 1.5;

    /// <summary>Marks service buildings inside the wall and builds the outside ones.</summary>
    /// <param name="cityPatches">The patches inside the wall, nearest the centre first.</param>
    /// <param name="allPatches">Every patch, inside and outside the wall.</param>
    /// <param name="wall">The wall corners.</param>
    /// <param name="gates">The gate positions.</param>
    /// <param name="services">The services to place.</param>
    /// <param name="rng">The random source for ties and jitter.</param>
    /// <param name="water">Where the water is; farm buildings are not built in it. Null for a dry site.</param>
    /// <param name="waterBuffer">Least distance from a farm building to the water, in metres.</param>
    public static void Assign(
        IReadOnlyList<Patch> cityPatches,
        IReadOnlyList<Patch> allPatches,
        IReadOnlyList<Vec2> wall,
        IReadOnlyList<Vec2> gates,
        IReadOnlyList<ServiceSpec> services,
        Rng rng,
        IWaterSampler? water = null,
        double waterBuffer = 0)
    {
        ArgumentNullException.ThrowIfNull(cityPatches);
        ArgumentNullException.ThrowIfNull(allPatches);
        ArgumentNullException.ThrowIfNull(services);
        if (services.Count == 0)
        {
            return;
        }

        // Fussy services (few allowed wards) choose first so they are not left with the leftovers.
        List<ServiceSpec> ordered = services
            .OrderBy(s => s.OutsideWall ? 1 : 0)
            .ThenBy(s => s.Wards.Count == 0 ? int.MaxValue : s.Wards.Count)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

        var outsidePatches = new Queue<Patch>(FarmlandNextToWall(cityPatches, allPatches));
        foreach (ServiceSpec spec in ordered)
        {
            for (int i = 0; i < spec.Count; i++)
            {
                if (spec.OutsideWall)
                {
                    PlaceOutside(spec, outsidePatches, gates, rng, water, waterBuffer);
                }
                else
                {
                    PlaceInside(spec, cityPatches, wall, gates, rng);
                }
            }
        }
    }

    private static void PlaceInside(ServiceSpec spec, IReadOnlyList<Patch> cityPatches, IReadOnlyList<Vec2> wall, IReadOnlyList<Vec2> gates, Rng rng)
    {
        Building? best = null;
        double bestScore = double.PositiveInfinity;
        List<Vec2> sameService = cityPatches.SelectMany(p => p.Buildings).Where(b => b.Service?.Id == spec.Id).Select(b => b.Footprint.Centroid).ToList();
        bool anyBigEnough = cityPatches.SelectMany(p => p.Buildings).Any(b => Eligible(b) && b.Footprint.Area >= spec.MinAreaMeters2);
        foreach (Patch patch in cityPatches)
        {
            foreach (Building b in patch.Buildings)
            {
                if (!Eligible(b) || (anyBigEnough && b.Footprint.Area < spec.MinAreaMeters2))
                {
                    continue;
                }

                double score = WardRank(spec, b.Ward) + (Proximity(spec.Near, b.Footprint.Centroid, wall, gates) / MetresPerRankStep) + (JitterShare * rng.NextDouble());
                foreach (Vec2 other in sameService)
                {
                    // A second inn on top of the first is no use to anyone: prefer a spot well away from the others.
                    double distance = Vec2.Distance(other, b.Footprint.Centroid);
                    if (distance < SameServiceSpacingMeters)
                    {
                        score += SameServicePenalty * (1 - (distance / SameServiceSpacingMeters));
                    }
                }

                if (!anyBigEnough)
                {
                    score -= b.Footprint.Area / 1000.0; // nothing is big enough: take the largest
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = b;
                }
            }
        }

        if (best is not null)
        {
            best.Service = spec;
        }
    }

    private static bool Eligible(Building b) => b.Service is null && b.Ward != WardType.Cathedral;

    // 0 for the most preferred ward, one more for each step down the list, and more again for a ward the service did not list.
    private static double WardRank(ServiceSpec spec, WardType ward)
    {
        if (spec.Wards.Count == 0)
        {
            return 0;
        }

        int index = -1;
        for (int i = 0; i < spec.Wards.Count; i++)
        {
            if (spec.Wards[i] == ward)
            {
                index = i;
                break;
            }
        }

        return index >= 0 ? index : spec.Wards.Count + 2;
    }

    private static double Proximity(ServiceNear near, Vec2 at, IReadOnlyList<Vec2> wall, IReadOnlyList<Vec2> gates)
    {
        switch (near)
        {
            case ServiceNear.Square:
                return at.Length;
            case ServiceNear.Gate:
                return gates.Count == 0 ? 0 : gates.Min(g => Vec2.Distance(g, at));
            case ServiceNear.Wall:
                return wall.Count == 0 ? 0 : wall.Min(w => Vec2.Distance(w, at));
            default:
                return 0;
        }
    }

    // Farmland patches that touch a city patch, nearest the centre first: those are the fields right outside the wall.
    private static List<Patch> FarmlandNextToWall(IReadOnlyList<Patch> cityPatches, IReadOnlyList<Patch> allPatches)
    {
        var inCity = new HashSet<int>(cityPatches.Select(p => p.Index));
        return allPatches
            .Where(p => !inCity.Contains(p.Index) && p.Neighbors.Any(inCity.Contains))
            .OrderBy(p => p.Site.Length)
            .ThenBy(p => p.Index)
            .ToList();
    }

    private static void PlaceOutside(ServiceSpec spec, Queue<Patch> patches, IReadOnlyList<Vec2> gates, Rng rng, IWaterSampler? water, double waterBuffer)
    {
        // Skip patches it does not fit in; a patch that fits is used once.
        int tries = patches.Count;
        while (tries-- > 0)
        {
            Patch patch = patches.Dequeue();
            ConvexPoly? footprint = FitFarm(patch, spec.MinAreaMeters2, gates, rng, water, waterBuffer);
            if (footprint is null)
            {
                patches.Enqueue(patch);
                continue;
            }

            int door = 0; // FitFarm winds the footprint so edge 0 is the front, facing the city
            int id = (patch.Index * 1000) + patch.Buildings.Count;
            patch.Buildings.Add(new Building(id, footprint, door, WardType.Outskirts) { Service = spec });
            return;
        }
    }

    // A rectangle on the patch whose front (edge 0) faces the city centre, shrunk until it fits inside the patch with a margin.
    private static ConvexPoly? FitFarm(Patch patch, double minArea, IReadOnlyList<Vec2> gates, Rng rng, IWaterSampler? water, double waterBuffer)
    {
        ConvexPoly? inner = patch.Shape.Inset(Enumerable.Repeat(FarmMargin, patch.Shape.Count).ToArray());
        if (inner is null)
        {
            return null;
        }

        Vec2 centre = inner.Centroid;
        if (gates.Any(g => Vec2.Distance(g, centre) < GateClearance))
        {
            return null;
        }

        Vec2 toCity = (-centre).Normalized();
        Vec2 across = new Vec2(-toCity.Y, toCity.X);
        double area = minArea;
        for (int attempt = 0; attempt < FarmFitAttempts; attempt++)
        {
            double width = Math.Sqrt(area * FarmAspect);
            double depth = area / width;
            Vec2 a = across * (width / 2);
            Vec2 d = toCity * (depth / 2);

            // Counter-clockwise from the front-left corner; edge 0 is the front, which faces the city.
            Vec2[] corners = { centre + d - a, centre + d + a, centre - d + a, centre - d - a };
            // Winding check: (front-left, front-right, back-right, back-left) must be counter-clockwise seen with y up.
            if (Signed(corners) < 0)
            {
                corners = new[] { corners[1], corners[0], corners[3], corners[2] };
            }

            if (corners.All(c => Inside(inner, c)) && (water is null || !CityGenerator.TouchesWater(new ConvexPoly(corners, new[] { 0, 0, 0, 0 }), water, waterBuffer)))
            {
                return new ConvexPoly(corners, new[] { 1, 0, 0, 0 });
            }

            area *= FarmShrink;
        }

        return null;
    }

    private static double Signed(Vec2[] p)
    {
        double sum = 0;
        for (int i = 0; i < p.Length; i++)
        {
            Vec2 a = p[i];
            Vec2 b = p[(i + 1) % p.Length];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return sum;
    }

    private static bool Inside(ConvexPoly poly, Vec2 p)
    {
        for (int i = 0; i < poly.Count; i++)
        {
            if (Vec2.Cross(poly.Edge(i), p - poly.Points[i]) < 0)
            {
                return false;
            }
        }

        return true;
    }
}
