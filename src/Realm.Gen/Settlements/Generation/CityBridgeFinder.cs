using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Generation;

/// <summary>Finds where a city's streets cross a river or lake, so that a bridge can carry them.</summary>
/// <remarks>Determinism: pure. Thread-safety: pure.</remarks>
public static class CityBridgeFinder
{
    /// <summary>Distance between water tests along a street, in metres.</summary>
    private const double StepMeters = 1;

    /// <summary>Land taken on each bank for the bridge's ends, in metres.</summary>
    private const double BankMarginMeters = 3;

    /// <summary>A crossing shorter than this is a puddle, not a bridge, in metres.</summary>
    private const double MinSpanMeters = 6;

    /// <summary>A crossing longer than this is left without a bridge, in metres.</summary>
    private const double MaxSpanMeters = 110;

    /// <summary>Two bridges are at least this far apart, in metres.</summary>
    private const double MinSeparationMeters = 35;

    /// <summary>Width of the roadway of a city bridge, in metres.</summary>
    public const double RoadwayMeters = 7;

    /// <summary>Finds bridges where streets cross water.</summary>
    /// <param name="streets">The streets (polylines from the square to the gates).</param>
    /// <param name="water">Where the water is.</param>
    /// <param name="maxBridges">Most bridges to return; the crossings nearest the centre come first.</param>
    /// <returns>The bridges, nearest the centre first.</returns>
    public static IReadOnlyList<CityBridge> Find(IReadOnlyList<Street> streets, IWaterSampler water, int maxBridges)
    {
        ArgumentNullException.ThrowIfNull(streets);
        ArgumentNullException.ThrowIfNull(water);
        var found = new List<CityBridge>();
        foreach (Street street in streets)
        {
            found.AddRange(Crossings(street, water));
        }

        var chosen = new List<CityBridge>();
        foreach (CityBridge bridge in found.OrderBy(b => Middle(b).Length).ThenBy(b => b.From.X).ThenBy(b => b.From.Y))
        {
            if (chosen.Count >= maxBridges)
            {
                break;
            }

            if (chosen.All(c => Vec2.Distance(Middle(c), Middle(bridge)) >= MinSeparationMeters))
            {
                chosen.Add(bridge);
            }
        }

        return chosen;
    }

    private static Vec2 Middle(CityBridge b) => Vec2.Lerp(b.From, b.To, 0.5);

    private static IEnumerable<CityBridge> Crossings(Street street, IWaterSampler water)
    {
        // Walk the polyline in 1 m steps and note the stretches in water.
        var points = new List<Vec2>();
        for (int i = 0; i + 1 < street.Points.Count; i++)
        {
            Vec2 a = street.Points[i], b = street.Points[i + 1];
            int steps = Math.Max(1, (int)Math.Ceiling(Vec2.Distance(a, b) / StepMeters));
            for (int s = 0; s < steps; s++)
            {
                points.Add(Vec2.Lerp(a, b, s / (double)steps));
            }
        }

        points.Add(street.Points[^1]);
        int index = 0;
        while (index < points.Count)
        {
            if (!water.IsWater(points[index].X, points[index].Y))
            {
                index++;
                continue;
            }

            int start = index;
            while (index < points.Count && water.IsWater(points[index].X, points[index].Y))
            {
                index++;
            }

            int end = index - 1;
            double span = (end - start + 1) * StepMeters;
            int margin = (int)Math.Ceiling(BankMarginMeters / StepMeters);
            if (span < MinSpanMeters || span > MaxSpanMeters || start - margin < 0 || end + margin >= points.Count)
            {
                continue; // a puddle, a lake too wide to bridge, or the water runs off the end of the street
            }

            Vec2 from = points[start - margin], to = points[end + margin];
            if (!water.IsWater(from.X, from.Y) && !water.IsWater(to.X, to.Y))
            {
                yield return new CityBridge(from, to, RoadwayMeters);
            }
        }
    }
}
