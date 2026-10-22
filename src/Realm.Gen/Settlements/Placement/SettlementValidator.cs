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
    private const double ReliefToleranceMeters = 0.5;
    private const int RingPoints = 16;

    /// <summary>Validates settlements on the terrain they stand on.</summary>
    /// <param name="settlements">The settlements.</param>
    /// <param name="terrain">The terrain the settlements stand on (natural ground unless <see cref="SettlementSettings.FlattenStrength"/> is positive).</param>
    /// <param name="network">Rivers and lakes, or null.</param>
    /// <param name="settings">The settings used to place them; defaults when null.</param>
    /// <param name="report">Receives SETL003 to SETL005 issues.</param>
    public static void Validate(
        IReadOnlyList<Settlement> settlements,
        HeightField terrain,
        RiverNetwork? network,
        SettlementSettings? settings,
        ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(settlements);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(report);
        settings ??= new SettlementSettings();
        var water = new WaterMask(terrain.WorldSizeMeters, network);

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

            double highest = double.MinValue;
            double lowest = double.MaxValue;
            // Sample on rings across the pad, the way the placer surveys it.
            for (int ring = 0; ring <= 3; ring++)
            {
                double radius = s.PadRadiusMeters * ring / 3.0;
                for (int p = 0; p < (ring == 0 ? 1 : RingPoints); p++)
                {
                    double angle = 2 * Math.PI * p / RingPoints;
                    double h = terrain.Sample(s.X + (radius * Math.Cos(angle)), s.Z + (radius * Math.Sin(angle)));
                    highest = Math.Max(highest, h);
                    lowest = Math.Min(lowest, h);
                }
            }

            double reliefLimit = (s.Highland ? settings.MountainMaxReliefPerRadius : settings.MaxReliefPerRadius) * s.PadRadiusMeters;
            double minHeight = s.Highland ? settings.MountainMinHeightMeters : settings.MinHeightMeters;
            double maxHeight = s.Highland ? settings.MountainMaxHeightMeters : settings.MaxHeightMeters;
            double relief = highest - lowest;
            if (relief > reliefLimit + ReliefToleranceMeters || lowest < minHeight - ReliefToleranceMeters || highest > maxHeight + ReliefToleranceMeters)
            {
                report.Error(
                    SettlementCodes.PadUnsuitable,
                    s.Id,
                    $"The ground under '{s.Id}' is too steep or out of range: relief {relief:F1} m (limit {reliefLimit:F1} m), lowest {lowest:F1} m, highest {highest:F1} m (allowed {minHeight:F0} to {maxHeight:F0} m).",
                    "Choose a flatter, higher or lower site, or raise MaxReliefPerRadius.");
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
