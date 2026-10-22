using System;
using System.Linq;
using System.Collections.Generic;
using Realm.Core.Validation;
using Realm.Gen.Settlements.Placement;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;

namespace Realm.Gen.Settlements.Roads;

/// <summary>Checks planned roads against the terrain (CLAUDE.md section 7.1: no road segment exceeds the walkable slope, bridges clear the water).</summary>
/// <remarks>Determinism: pure.</remarks>
public static class RoadValidator
{
    private const double BedToleranceMeters = 1.0;
    private const double SharedCellMeters = 16;
    private const int HairpinPoints = 3; // points of one road this close in index (24 m) are the same stretch; further apart and within a width of each other is a hairpin crossing itself

    /// <summary>Validates roads on the carved terrain. The surface match (ROAD003) is only checked when the vertex spacing is no coarser than the road width.</summary>
    /// <param name="roads">The roads.</param>
    /// <param name="before">The terrain before carving (to measure cut and fill).</param>
    /// <param name="carved">The terrain after <see cref="RoadTerrain.Carve"/>.</param>
    /// <param name="network">Rivers and lakes, or null.</param>
    /// <param name="settings">Limits; defaults when null.</param>
    /// <param name="report">Receives ROAD003 to ROAD005 issues.</param>
    /// <param name="settlements">The settlements the roads join; roads that end at a mountain city get the looser highland limits. Optional.</param>
    public static void Validate(
        IReadOnlyList<Road> roads,
        HeightField before,
        HeightField carved,
        RiverNetwork? network,
        RoadSettings? settings,
        ValidationReport report,
        IReadOnlyList<Settlement>? settlements = null)
    {
        ArgumentNullException.ThrowIfNull(roads);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(carved);
        ArgumentNullException.ThrowIfNull(report);
        settings ??= new RoadSettings();
        var raster = new RiverRaster(carved.WorldSizeMeters, network);

        // Where two roads run together or cross, or one road crosses itself in a hairpin, the later stretch's bed wins and the junction has
        // no design yet (known issue 51), so those places are left out of the bed check; the cut and fill check still covers them.
        var shared = new Dictionary<(int, int), List<(string Road, int Index, RoadPoint Point)>>();
        foreach (Road r in roads)
        {
            for (int k = 0; k < r.Points.Count; k++)
            {
                RoadPoint p = r.Points[k];
                var cell = ((int)Math.Floor(p.X / SharedCellMeters), (int)Math.Floor(p.Z / SharedCellMeters));
                if (!shared.TryGetValue(cell, out List<(string, int, RoadPoint)>? list))
                {
                    shared[cell] = list = new List<(string, int, RoadPoint)>();
                }

                list.Add((r.Id, k, p));
            }
        }

        bool SharedWithAnother(Road own, int index, RoadPoint p)
        {
            int cx = (int)Math.Floor(p.X / SharedCellMeters), cz = (int)Math.Floor(p.Z / SharedCellMeters);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (shared.TryGetValue((cx + dx, cz + dz), out List<(string Road, int Index, RoadPoint Point)>? list)
                        && list.Any(o => (o.Road != own.Id || Math.Abs(o.Index - index) > HairpinPoints)
                            && Math.Abs(o.Point.X - p.X) < own.WidthMeters && Math.Abs(o.Point.Z - p.Z) < own.WidthMeters))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        foreach (Road road in roads)
        {
            bool highland = settlements is not null && settlements.Any(s => s.Highland && (s.Id == road.From || s.Id == road.To));
            double bedTolerance = highland ? settings.HighlandBedToleranceMeters : BedToleranceMeters;
            double cutLimit = highland ? settings.HighlandMaxCutFillMeters : settings.MaxCutFillMeters;
            double worstBed = 0;
            double worstCut = 0;
            RoadPoint worstAt = road.Points[0];
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

                double bed = SharedWithAnother(road, i, p) ? 0 : Math.Abs(carved.Sample(p.X, p.Z) - p.Height);
                if (bed > worstBed)
                {
                    worstBed = bed;
                    worstAt = p;
                }

                worstCut = Math.Max(worstCut, Math.Abs(before.Sample(p.X, p.Z) - p.Height));
            }

            if (inWater > 0)
            {
                report.Error(RoadCodes.InWater, road.Id, $"'{road.Id}' runs through the sea or a lake at {inWater} point(s).", "Route around the water or raise MinHeightMeters.");
            }

            // A grid coarser than the road cannot hold its surface, so the match is only checked when the road is resolvable.
            bool resolvable = carved.SpacingMeters <= road.WidthMeters;
            if ((resolvable && worstBed > bedTolerance) || worstCut > cutLimit)
            {
                report.Error(
                    RoadCodes.BedMismatch,
                    road.Id,
                    $"The terrain under '{road.Id}' differs from its surface by up to {worstBed:F2} m (worst near {worstAt.X:F0}, {worstAt.Z:F0}), or a cut or fill reaches {worstCut:F1} m (limit {cutLimit:F0} m).",
                    "Raise MaxCutFillMeters, lower SlopePenalty so the road avoids hills, or check the shoulder settings.");
            }

            foreach (RoadBridge bridge in road.Bridges)
            {
                double grade = Math.Abs(bridge.EndHeight - bridge.StartHeight) / Math.Max(bridge.LengthMeters, 1e-6);
                if (grade > settings.MaxBridgeGrade)
                {
                    report.Error(
                        RoadCodes.BridgeTooSteep,
                        road.Id,
                        $"A bridge on '{road.Id}' at ({bridge.X:F0}, {bridge.Z:F0}) climbs {grade:P0} between its ends (from {bridge.StartHeight:F1} m to {bridge.EndHeight:F1} m); the limit is {settings.MaxBridgeGrade:P0}.",
                        "Raise MaxBridgeGrade, or raise RiverCrossingPenaltyMeters and SlopePenalty so roads cross where the banks are level.");
                }

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
