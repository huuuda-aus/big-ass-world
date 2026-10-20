using System;
using Realm.Core.Validation;

namespace Realm.Gen.Terrain.Erosion;

/// <summary>Terrain validators for rivers (CLAUDE.md section 7.1): rivers flow downhill and end somewhere sensible.</summary>
/// <remarks>Determinism: fixed scan order. Thread-safety: stateless.</remarks>
public static class RiverValidator
{
    /// <summary>Largest allowed rise in water surface height going downstream, in metres.</summary>
    public const double UphillToleranceMeters = 0.01;

    private const double EdgeToleranceMeters = 1e-6;
    private const double HalfDivisor = 2.0;

    /// <summary>Checks every river of a network and reports each problem class once per river.</summary>
    /// <param name="network">The rivers and lakes.</param>
    /// <param name="worldSizeMeters">World side length in metres, used to check world-edge mouths.</param>
    /// <param name="report">Receives issues.</param>
    public static void Validate(RiverNetwork network, int worldSizeMeters, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(report);

        double half = worldSizeMeters / HalfDivisor;
        foreach (River river in network.Rivers)
        {
            string where = $"river {river.Index}";

            for (int i = 1; i < river.Points.Count; i++)
            {
                double rise = river.Points[i].Height - river.Points[i - 1].Height;
                if (rise > UphillToleranceMeters)
                {
                    report.Error(
                        TerrainCodes.RiverUphill,
                        where,
                        $"River {river.Index} rises {rise:F2} m going downstream near ({river.Points[i].X}, {river.Points[i].Z}).",
                        "Check the erosion settings; routing should never let water flow uphill.");
                    break;
                }
            }

            string? problem = MouthProblem(network, river, half);
            if (problem is not null)
            {
                report.Error(TerrainCodes.RiverDeadEnd, where, $"River {river.Index} {problem}", "This indicates a bug in river extraction.");
            }
        }
    }

    private static string? MouthProblem(RiverNetwork network, River river, double half)
    {
        RiverPoint last = river.Points[^1];
        switch (river.Mouth)
        {
            case RiverMouth.Sea:
                return last.Height <= UphillToleranceMeters ? null : $"is marked as ending in the sea but ends {last.Height:F2} m above sea level.";
            case RiverMouth.WorldEdge:
                bool onEdge = Math.Abs(Math.Abs(last.X) - half) <= EdgeToleranceMeters || Math.Abs(Math.Abs(last.Z) - half) <= EdgeToleranceMeters;
                return onEdge ? null : "is marked as leaving the world but does not end on the world edge.";
            case RiverMouth.Confluence:
                if (river.JoinsRiver is not int target || target < 0 || target >= network.Rivers.Count || target == river.Index)
                {
                    return "is marked as a tributary but joins no valid river.";
                }

                foreach (RiverPoint point in network.Rivers[target].Points)
                {
                    if (point.X == last.X && point.Z == last.Z)
                    {
                        return null;
                    }
                }

                return $"does not end on a point of the river it joins ({target}).";
            case RiverMouth.Dried:
                return null;
            default:
                return "has an unknown mouth kind.";
        }
    }
}
