using System;
using System.Collections.Generic;
using Realm.Gen.Settlements.Placement;
using Realm.Gen.Settlements.Roads;
using Realm.Gen.Terrain.Erosion;

namespace Realm.Gen.Foliage;

/// <summary>A raster of the places plants must keep clear: water, roads and settlements, each with a margin.</summary>
/// <remarks>Determinism: pure. Thread-safety: immutable after construction.</remarks>
public sealed class FoliageExclusion
{
    private readonly bool[] _blocked;
    private readonly int _side;
    private readonly double _cell;
    private readonly double _half;

    /// <summary>Paints the exclusion raster.</summary>
    /// <param name="worldSizeMeters">World side length in metres.</param>
    /// <param name="network">Rivers and lakes, or null.</param>
    /// <param name="roads">Roads.</param>
    /// <param name="settlements">Settlements.</param>
    /// <param name="settings">Margins and cell size.</param>
    public FoliageExclusion(int worldSizeMeters, RiverNetwork? network, IReadOnlyList<Road> roads, IReadOnlyList<Settlement> settlements, FoliageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(roads);
        ArgumentNullException.ThrowIfNull(settlements);
        ArgumentNullException.ThrowIfNull(settings);
        _cell = settings.ExclusionCellMeters;
        _half = worldSizeMeters / 2.0;
        _side = (int)Math.Ceiling(worldSizeMeters / _cell);
        _blocked = new bool[_side * _side];

        if (network is not null)
        {
            foreach (River river in network.Rivers)
            {
                for (int i = 1; i < river.Points.Count; i++)
                {
                    RiverPoint a = river.Points[i - 1];
                    RiverPoint b = river.Points[i];
                    PaintCapsule(a.X, a.Z, b.X, b.Z, Math.Max(a.HalfWidth, b.HalfWidth) + settings.WaterMarginMeters);
                }
            }

            double halfCell = Math.Max(_cell, network.WorkingSpacingMeters) / 2.0 + settings.WaterMarginMeters;
            foreach (Lake lake in network.Lakes)
            {
                foreach ((double x, double z) in lake.Cells)
                {
                    PaintBox(x - halfCell, z - halfCell, x + halfCell, z + halfCell);
                }
            }
        }

        foreach (Road road in roads)
        {
            for (int i = 1; i < road.Points.Count; i++)
            {
                RoadPoint a = road.Points[i - 1];
                RoadPoint b = road.Points[i];
                PaintCapsule(a.X, a.Z, b.X, b.Z, (road.WidthMeters / 2) + settings.RoadMarginMeters);
            }
        }

        foreach (Settlement s in settlements)
        {
            double radius = s.PadRadiusMeters + settings.SettlementMarginMeters;
            PaintBox(s.X - radius, s.Z - radius, s.X + radius, s.Z + radius, s.X, s.Z, radius);
        }
    }

    /// <summary>Whether a world position must stay clear.</summary>
    /// <param name="x">World east in metres.</param>
    /// <param name="z">World south in metres.</param>
    /// <returns><c>true</c> when blocked; positions outside the world are blocked.</returns>
    public bool IsBlocked(double x, double z)
    {
        int cx = (int)Math.Floor((x + _half) / _cell);
        int cz = (int)Math.Floor((z + _half) / _cell);
        return (uint)cx >= (uint)_side || (uint)cz >= (uint)_side || _blocked[(cz * _side) + cx];
    }

    private void PaintCapsule(double ax, double az, double bx, double bz, double radius)
    {
        int x0 = Cell(Math.Min(ax, bx) - radius);
        int x1 = Cell(Math.Max(ax, bx) + radius);
        int z0 = Cell(Math.Min(az, bz) - radius);
        int z1 = Cell(Math.Max(az, bz) + radius);
        double sx = bx - ax;
        double sz = bz - az;
        double lengthSquared = (sx * sx) + (sz * sz);
        for (int cz = z0; cz <= z1; cz++)
        {
            for (int cx = x0; cx <= x1; cx++)
            {
                double x = -_half + ((cx + 0.5) * _cell);
                double z = -_half + ((cz + 0.5) * _cell);
                double t = lengthSquared < 1e-9 ? 0 : Math.Clamp((((x - ax) * sx) + ((z - az) * sz)) / lengthSquared, 0, 1);
                double dx = x - (ax + (sx * t));
                double dz = z - (az + (sz * t));
                if ((dx * dx) + (dz * dz) <= radius * radius)
                {
                    _blocked[(cz * _side) + cx] = true;
                }
            }
        }
    }

    private void PaintBox(double minX, double minZ, double maxX, double maxZ, double discX = 0, double discZ = 0, double discRadius = -1)
    {
        int x0 = Cell(minX);
        int x1 = Cell(maxX);
        int z0 = Cell(minZ);
        int z1 = Cell(maxZ);
        for (int cz = z0; cz <= z1; cz++)
        {
            for (int cx = x0; cx <= x1; cx++)
            {
                if (discRadius >= 0)
                {
                    double dx = -_half + ((cx + 0.5) * _cell) - discX;
                    double dz = -_half + ((cz + 0.5) * _cell) - discZ;
                    if ((dx * dx) + (dz * dz) > discRadius * discRadius)
                    {
                        continue;
                    }
                }

                _blocked[(cz * _side) + cx] = true;
            }
        }
    }

    private int Cell(double world) => Math.Clamp((int)Math.Floor((world + _half) / _cell), 0, _side - 1);
}
