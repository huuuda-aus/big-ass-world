using System;
using Realm.Gen.Bake;
using Realm.Gen.Terrain.Erosion;

namespace Realm.Gen.Settlements.Roads;

/// <summary>A fine grid that records where the rivers and lakes are, for exact crossing tests along roads.</summary>
/// <remarks>Determinism: pure. Thread-safety: immutable after construction.</remarks>
public sealed class RiverRaster
{
    /// <summary>Side of one cell in metres.</summary>
    public const double CellMeters = 8;

    private readonly bool[] _river;
    private readonly bool[] _lake;
    private readonly int _side;
    private readonly double _half;

    /// <summary>Paints the rivers (with the same width the baker paints) and the lakes.</summary>
    /// <param name="worldSizeMeters">World side length in metres, centred on the origin.</param>
    /// <param name="network">Rivers and lakes; may be null.</param>
    public RiverRaster(int worldSizeMeters, RiverNetwork? network)
    {
        _half = worldSizeMeters / 2.0;
        _side = (int)Math.Ceiling(worldSizeMeters / CellMeters);
        _river = new bool[_side * _side];
        _lake = new bool[_side * _side];
        if (network is null)
        {
            return;
        }

        foreach (River river in network.Rivers)
        {
            for (int i = 1; i < river.Points.Count; i++)
            {
                RiverPoint a = river.Points[i - 1];
                RiverPoint b = river.Points[i];
                PaintSegment(a.X, a.Z, b.X, b.Z, TerrainBaker.RiverHalfWidthMeters(b.FlowKm2));
            }
        }

        double halfCell = Math.Max(CellMeters, network.WorkingSpacingMeters) / 2.0;
        foreach (Lake lake in network.Lakes)
        {
            foreach ((double x, double z) in lake.Cells)
            {
                PaintBox(_lake, x - halfCell, z - halfCell, x + halfCell, z + halfCell);
            }
        }
    }

    /// <summary>Whether a river covers a world position.</summary>
    /// <param name="x">World X in metres.</param>
    /// <param name="z">World Z in metres.</param>
    /// <returns><c>true</c> over river water.</returns>
    public bool IsRiver(double x, double z) => Get(_river, x, z);

    /// <summary>Whether a lake covers a world position.</summary>
    /// <param name="x">World X in metres.</param>
    /// <param name="z">World Z in metres.</param>
    /// <returns><c>true</c> over lake water.</returns>
    public bool IsLake(double x, double z) => Get(_lake, x, z);

    private bool Get(bool[] grid, double x, double z)
    {
        int cx = (int)Math.Floor((x + _half) / CellMeters);
        int cz = (int)Math.Floor((z + _half) / CellMeters);
        return cx >= 0 && cz >= 0 && cx < _side && cz < _side && grid[(cz * _side) + cx];
    }

    private void PaintSegment(double ax, double az, double bx, double bz, double halfWidth)
    {
        double reach = halfWidth + (CellMeters / 2);
        int x0 = Math.Max(0, (int)Math.Floor((Math.Min(ax, bx) - reach + _half) / CellMeters));
        int x1 = Math.Min(_side - 1, (int)Math.Ceiling((Math.Max(ax, bx) + reach + _half) / CellMeters));
        int z0 = Math.Max(0, (int)Math.Floor((Math.Min(az, bz) - reach + _half) / CellMeters));
        int z1 = Math.Min(_side - 1, (int)Math.Ceiling((Math.Max(az, bz) + reach + _half) / CellMeters));
        double dx = bx - ax;
        double dz = bz - az;
        double lengthSquared = (dx * dx) + (dz * dz);
        for (int cz = z0; cz <= z1; cz++)
        {
            for (int cx = x0; cx <= x1; cx++)
            {
                double px = -_half + ((cx + 0.5) * CellMeters);
                double pz = -_half + ((cz + 0.5) * CellMeters);
                double t = lengthSquared <= 0 ? 0 : Math.Clamp((((px - ax) * dx) + ((pz - az) * dz)) / lengthSquared, 0, 1);
                double qx = ax + (dx * t);
                double qz = az + (dz * t);
                if (Math.Sqrt(((px - qx) * (px - qx)) + ((pz - qz) * (pz - qz))) <= reach)
                {
                    _river[(cz * _side) + cx] = true;
                }
            }
        }
    }

    private void PaintBox(bool[] grid, double minX, double minZ, double maxX, double maxZ)
    {
        int x0 = Math.Max(0, (int)Math.Floor((minX + _half) / CellMeters));
        int x1 = Math.Min(_side - 1, (int)Math.Floor((maxX + _half) / CellMeters));
        int z0 = Math.Max(0, (int)Math.Floor((minZ + _half) / CellMeters));
        int z1 = Math.Min(_side - 1, (int)Math.Floor((maxZ + _half) / CellMeters));
        for (int cz = z0; cz <= z1; cz++)
        {
            for (int cx = x0; cx <= x1; cx++)
            {
                grid[(cz * _side) + cx] = true;
            }
        }
    }
}
