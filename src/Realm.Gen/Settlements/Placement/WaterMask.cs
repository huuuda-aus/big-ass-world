using System;
using Realm.Gen.Terrain.Erosion;

namespace Realm.Gen.Settlements.Placement;

/// <summary>A coarse grid that records which squares of the world hold a river or a lake, for fast distance checks.</summary>
/// <remarks>Determinism: pure. Thread-safety: immutable after construction.</remarks>
public sealed class WaterMask
{
    /// <summary>Side of one grid cell in metres.</summary>
    public const double CellMeters = 64;

    private readonly bool[] _water;
    private readonly int _side;
    private readonly double _half;

    /// <summary>Marks every cell touched by a river point or a lake cell.</summary>
    /// <param name="worldSizeMeters">World side length in metres, centred on the origin.</param>
    /// <param name="network">Rivers and lakes; may be null for a world without water.</param>
    public WaterMask(int worldSizeMeters, RiverNetwork? network)
    {
        _half = worldSizeMeters / 2.0;
        _side = (int)Math.Ceiling(worldSizeMeters / CellMeters);
        _water = new bool[_side * _side];
        if (network is null)
        {
            return;
        }

        foreach (River river in network.Rivers)
        {
            // Walk each segment in cell-sized steps so no cell between two far-apart points is missed.
            for (int i = 0; i < river.Points.Count; i++)
            {
                RiverPoint a = river.Points[i];
                MarkDisc(a.X, a.Z, a.HalfWidth);
                if (i + 1 < river.Points.Count)
                {
                    RiverPoint b = river.Points[i + 1];
                    double length = Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Z - a.Z) * (b.Z - a.Z)));
                    int steps = (int)Math.Ceiling(length / (CellMeters / 2));
                    for (int s = 1; s < steps; s++)
                    {
                        double t = s / (double)steps;
                        MarkDisc(a.X + ((b.X - a.X) * t), a.Z + ((b.Z - a.Z) * t), a.HalfWidth + ((b.HalfWidth - a.HalfWidth) * t));
                    }
                }
            }
        }

        foreach (Lake lake in network.Lakes)
        {
            foreach ((double x, double z) in lake.Cells)
            {
                Mark(x, z);
            }
        }
    }

    /// <summary>Whether any water lies within a distance of a point (to the accuracy of one cell).</summary>
    /// <param name="x">World X in metres.</param>
    /// <param name="z">World Z in metres.</param>
    /// <param name="radiusMeters">Search radius in metres.</param>
    /// <returns><c>true</c> when a marked cell lies within the radius plus half a cell.</returns>
    public bool AnyWithin(double x, double z, double radiusMeters)
    {
        double reach = radiusMeters + (CellMeters / 2);
        int x0 = Math.Max(0, CellOf(x - reach));
        int x1 = Math.Min(_side - 1, CellOf(x + reach));
        int z0 = Math.Max(0, CellOf(z - reach));
        int z1 = Math.Min(_side - 1, CellOf(z + reach));
        for (int cz = z0; cz <= z1; cz++)
        {
            for (int cx = x0; cx <= x1; cx++)
            {
                if (!_water[(cz * _side) + cx])
                {
                    continue;
                }

                double dx = (-_half + ((cx + 0.5) * CellMeters)) - x;
                double dz = (-_half + ((cz + 0.5) * CellMeters)) - z;
                if ((dx * dx) + (dz * dz) <= reach * reach)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Marks every cell whose centre lies within the river's half width of a point on its line.
    private void MarkDisc(double x, double z, double radius)
    {
        int x0 = CellOf(x - radius), x1 = CellOf(x + radius), z0 = CellOf(z - radius), z1 = CellOf(z + radius);
        for (int cz = z0; cz <= z1; cz++)
        {
            for (int cx = x0; cx <= x1; cx++)
            {
                double dx = (-_half + ((cx + 0.5) * CellMeters)) - x;
                double dz = (-_half + ((cz + 0.5) * CellMeters)) - z;
                if (((dx * dx) + (dz * dz) <= radius * radius || (cx == CellOf(x) && cz == CellOf(z))) && cx >= 0 && cz >= 0 && cx < _side && cz < _side)
                {
                    _water[(cz * _side) + cx] = true;
                }
            }
        }
    }

    private int CellOf(double world) => (int)Math.Floor((world + _half) / CellMeters);

    private void Mark(double x, double z)
    {
        int cx = CellOf(x);
        int cz = CellOf(z);
        if (cx >= 0 && cz >= 0 && cx < _side && cz < _side)
        {
            _water[(cz * _side) + cx] = true;
        }
    }
}
