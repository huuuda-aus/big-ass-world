using System;
using System.Collections.Generic;
using Realm.Core.Validation;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;

namespace Realm.Gen.Settlements.Roads;

/// <summary>Checks planned roads against the terrain (CLAUDE.md section 7.1: no road segment exceeds the walkable slope, bridges clear the water).</summary>
/// <remarks>Determinism: pure.</remarks>
public static class RoadValidator
{
    private const double BedToleranceMeters = 1.0;

    /// <summary>Validates roads on the carved terrain. The surface match (ROAD003) is only checked when the vertex spacing is no coarser than the road width.</summary>
    /// <param name="roads">The roads.</param>
    /// <param name="before">The terrain before carving (to measure cut and fill).</param>
    /// <param name="carved">The terrain after <see cref="RoadTerrain.Carve"/>.</param>
    /// <param name="network">Rivers and lakes, or null.</param>
    /// <param name="settings">Limits; defaults when null.</param>
    /// <param name="report">Receives ROAD003 to ROAD005 issues.</param>
    public static void Validate(
        IReadOnlyList<Road> roads,
        HeightField before,
        HeightField carved,
        RiverNetwork? network,
        RoadSettings? settings,
        ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(roads);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(carved);
        ArgumentNullException.ThrowIfNull(report);
        settings ??= new RoadSettings();
        var raster = new RiverRaster(carved.WorldSizeMeters, network);

        foreach (Road road in roads)
        {
            double worstBed = 0;
            double worstCut = 0;
            int inWater = 0;
            for (int i = 0; i < road.Points.Count; i++)
            {
                RoadPoint p = road.Points[i];
                bool onBridge = false;
                foreach (RoadBridge b in road.Bridges)
                {
                    onBridge |= i >= b.FirstPoint && i <= b.LastPoint;
                }

                if (raster.IsLake(p.X, p.Z) || carved.Sample(p.X, p.Z) < 0)
                {
                    inWater++;
                }

                if (onBridge)
                {
                    continue;
                }

                worstBed = Math.Max(worstBed, Math.Abs(carved.Sample(p.X, p.Z) - p.Height));
                worstCut = Math.Max(worstCut, Math.Abs(before.Sample(p.X, p.Z) - p.Height));
            }

            if (inWater > 0)
            {
                report.Error(RoadCodes.InWater, road.Id, $"'{road.Id}' runs through the sea or a lake at {inWater} point(s).", "Route around the water or raise MinHeightMeters.");
            }

            // A grid coarser than the road cannot hold its surface, so the match is only checked when the road is resolvable.
            bool resolvable = carved.SpacingMeters <= road.WidthMeters;
            if ((resolvable && worstBed > BedToleranceMeters) || worstCut > settings.MaxCutFillMeters)
            {
                report.Error(
                    RoadCodes.BedMismatch,
                    road.Id,
                    $"The terrain under '{road.Id}' differs from its surface by up to {worstBed:F2} m, or a cut or fill reaches {worstCut:F1} m (limit {settings.MaxCutFillMeters:F0} m).",
                    "Raise MaxCutFillMeters, lower SlopePenalty so the road avoids hills, or check the shoulder settings.");
            }

            foreach (RoadBridge bridge in road.Bridges)
            {
                if (bridge.LengthMeters > settings.MaxBridgeMeters)
                {
                    report.Error(
                        RoadCodes.BridgeTooLong,
                        road.Id,
                        $"A bridge on '{road.Id}' is {bridge.LengthMeters:F0} m long; the limit is {settings.MaxBridgeMeters:F0} m.",
                        "Raise MaxBridgeMeters or raise RiverCrossingPenaltyMeters so roads cross at narrower places.");
                }
            }
        }
    }
}
