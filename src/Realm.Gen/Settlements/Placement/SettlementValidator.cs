using System;
using System.Collections.Generic;
using Realm.Core.Validation;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;

namespace Realm.Gen.Settlements.Placement;

/// <summary>Checks placed settlements against the terrain they stand on (CLAUDE.md section 7.1).</summary>
/// <remarks>Determinism: pure.</remarks>
public static class SettlementValidator
{
    private const double FlatToleranceMeters = 0.5;
    private const int RingPoints = 16;

    /// <summary>Validates settlements on the flattened terrain.</summary>
    /// <param name="settlements">The settlements.</param>
    /// <param name="flattened">The terrain after <see cref="SettlementTerrain.Flatten"/>.</param>
    /// <param name="network">Rivers and lakes, or null.</param>
    /// <param name="settings">The settings used to place them; defaults when null.</param>
    /// <param name="report">Receives SETL003 to SETL005 issues.</param>
    public static void Validate(
        IReadOnlyList<Settlement> settlements,
        HeightField flattened,
        RiverNetwork? network,
        SettlementSettings? settings,
        ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(settlements);
        ArgumentNullException.ThrowIfNull(flattened);
        ArgumentNullException.ThrowIfNull(report);
        settings ??= new SettlementSettings();
        var water = new WaterMask(flattened.WorldSizeMeters, network);

        for (int i = 0; i < settlements.Count; i++)
        {
            Settlement s = settlements[i];
            for (int j = i + 1; j < settlements.Count; j++)
            {
                double distance = s.DistanceTo(settlements[j].X, settlements[j].Z);
                if (distance < settings.MinSpacingMeters)
                {
                    report.Error(
                        SettlementCodes.TooClose,
                        s.Id,
                        $"'{s.Id}' and '{settlements[j].Id}' are {distance:F0} m apart; the minimum is {settings.MinSpacingMeters:F0} m.",
                        "Increase the spacing or ask for fewer settlements.");
                }
            }

            double worst = 0;
            double lowest = double.MaxValue;
            // Sample inside the pad by one grid cell: right at its edge a bilinear sample already mixes in blended vertices.
            double checkRadius = Math.Max(0, s.PadRadiusMeters - flattened.SpacingMeters);
            for (int ring = 0; ring <= 3; ring++)
            {
                double radius = checkRadius * ring / 3.0;
                for (int p = 0; p < (ring == 0 ? 1 : RingPoints); p++)
                {
                    double angle = 2 * Math.PI * p / RingPoints;
                    double h = flattened.Sample(s.X + (radius * Math.Cos(angle)), s.Z + (radius * Math.Sin(angle)));
                    worst = Math.Max(worst, Math.Abs(h - s.GroundHeightMeters));
                    lowest = Math.Min(lowest, h);
                }
            }

            if (worst > FlatToleranceMeters || lowest < settings.MinHeightMeters - FlatToleranceMeters || s.GroundHeightMeters > settings.MaxHeightMeters)
            {
                report.Error(
                    SettlementCodes.PadUnsuitable,
                    s.Id,
                    $"The pad of '{s.Id}' is not level (worst difference {worst:F2} m) or lies outside {settings.MinHeightMeters:F0} to {settings.MaxHeightMeters:F0} m (ground {s.GroundHeightMeters:F1} m, lowest point {lowest:F1} m).",
                    "Flatten the terrain after placing, and keep sites above sea level.");
            }

            if (water.AnyWithin(s.X, s.Z, s.PadRadiusMeters))
            {
                report.Error(
                    SettlementCodes.WaterOnPad,
                    s.Id,
                    $"A river or lake reaches the pad of '{s.Id}'.",
                    "Move the settlement away from water or raise WaterClearanceMeters.");
            }
        }
    }
}
