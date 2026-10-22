using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Gen.Terrain.Erosion;

namespace Realm.Gen.Terrain.Water;

/// <summary>
/// Builds the water surfaces (river ribbons that follow the profile and flat lake sheets) in square tiles, so the game can cull them
/// by distance. Vertex colours carry what the water shader needs: how fast the water runs, how high it is and how rough.
/// </summary>
/// <remarks>Determinism: pure; tiles and triangles come out in a fixed order. Thread-safety: pure.</remarks>
public static class WaterMesher
{
    /// <summary>Default tile side in metres.</summary>
    public const int DefaultTileMeters = 2048;

    private const double MarginMeters = 0.5; // the ribbon is a little wider than the cut bed so its edge tucks under the bank
    private const double SurfaceLiftMeters = 0.05;
    private const double SteepSlope = 0.3; // slope at which the water is fully rough
    private const double HighAltitudeMeters = 800;
    private const int Across = 3; // left edge, centre, right edge

    /// <summary>Builds the water tiles.</summary>
    /// <param name="network">Rivers (with their <see cref="RiverNetwork.Channels"/>) and lakes.</param>
    /// <param name="worldSizeMeters">World side length in metres, centred on the origin.</param>
    /// <param name="tileMeters">Tile side in metres, at least 256.</param>
    /// <returns>The non-empty tiles, ordered by row then column.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tileMeters"/> is below 256.</exception>
    public static IReadOnlyList<WaterTile> Build(RiverNetwork network, int worldSizeMeters, int tileMeters = DefaultTileMeters)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentOutOfRangeException.ThrowIfLessThan(tileMeters, 256);
        var builders = new SortedDictionary<(int Z, int X), Builder>();
        double half = worldSizeMeters / 2.0;

        Builder For(double x, double z)
        {
            var key = ((int)Math.Floor((z + half) / tileMeters), (int)Math.Floor((x + half) / tileMeters));
            if (!builders.TryGetValue(key, out Builder? b))
            {
                builders[key] = b = new Builder();
            }

            return b;
        }

        foreach (RiverChannel channel in network.Channels)
        {
            IReadOnlyList<ChannelPoint> pts = channel.Points;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                if (pts[i].InLake && pts[i + 1].InLake)
                {
                    continue;
                }

                AddRibbon(For((pts[i].X + pts[i + 1].X) / 2, (pts[i].Z + pts[i + 1].Z) / 2), pts, i);
            }
        }

        double cell = Math.Max(1, network.WorkingSpacingMeters);
        foreach (Lake lake in network.Lakes)
        {
            foreach ((double cx, double cz) in lake.Cells)
            {
                AddCell(For(cx, cz), cx, cz, cell / 2, lake.SurfaceHeight);
            }
        }

        return builders.Select(b => b.Value.Build(b.Key.X, b.Key.Z)).ToList();
    }

    private static void AddRibbon(Builder b, IReadOnlyList<ChannelPoint> pts, int i)
    {
        int first = b.VertexCount;
        for (int k = 0; k < 2; k++)
        {
            int index = i + k;
            ChannelPoint p = pts[index];
            ChannelPoint before = pts[Math.Max(0, index - 1)];
            ChannelPoint after = pts[Math.Min(pts.Count - 1, index + 1)];
            double dx = after.X - before.X;
            double dz = after.Z - before.Z;
            double length = Math.Sqrt((dx * dx) + (dz * dz));
            double nx = length < 1e-6 ? 0 : -dz / length;
            double nz = length < 1e-6 ? 1 : dx / length;
            double width = p.HalfWidth + MarginMeters;
            ChannelPoint next = pts[Math.Min(pts.Count - 1, i + 1)];
            ChannelPoint now = pts[i];
            double run = Math.Sqrt(((next.X - now.X) * (next.X - now.X)) + ((next.Z - now.Z) * (next.Z - now.Z)));
            double slope = run < 1e-6 ? 0 : Math.Max(0, (now.Surface - next.Surface) / run);
            double speed = RiverFlow.Speed(slope, p.FlowKm2);
            float r = (float)((speed - RiverFlow.MinimumSpeed) / (RiverFlow.MaximumSpeed - RiverFlow.MinimumSpeed));
            float g = (float)Math.Clamp(p.Surface / HighAltitudeMeters, 0, 1);
            float turbulence = (float)Math.Clamp(slope / SteepSlope, 0, 1);
            double y = p.Surface + SurfaceLiftMeters;
            for (int side = 0; side < Across; side++)
            {
                double offset = (side - 1) * width; // -width, 0, +width
                float edge = side == 1 ? 1f : 0f;
                b.Vertex(p.X + (nx * offset), y, p.Z + (nz * offset), (float)offset, (float)p.DistanceMeters, r, g, turbulence, edge);
            }
        }

        for (int side = 0; side < Across - 1; side++)
        {
            int a0 = first + side;
            int a1 = first + side + 1;
            int b0 = first + Across + side;
            int b1 = first + Across + side + 1;
            // Seen from above (+Y), the river runs toward the second ring and the first column is on its left; wound to face up.
            b.Triangle(a0, b0, a1);
            b.Triangle(a1, b0, b1);
        }
    }

    private static void AddCell(Builder b, double cx, double cz, double halfCell, double level)
    {
        int first = b.VertexCount;
        float g = (float)Math.Clamp(level / HighAltitudeMeters, 0, 1);
        double y = level + SurfaceLiftMeters;
        (double X, double Z)[] corners = { (cx - halfCell, cz - halfCell), (cx + halfCell, cz - halfCell), (cx - halfCell, cz + halfCell), (cx + halfCell, cz + halfCell) };
        foreach ((double x, double z) in corners)
        {
            b.Vertex(x, y, z, (float)x, (float)z, 0f, g, 0f, 1f);
        }

        b.Triangle(first, first + 2, first + 1);
        b.Triangle(first + 1, first + 2, first + 3);
    }

    private sealed class Builder
    {
        private readonly List<float> _positions = new();
        private readonly List<float> _uvs = new();
        private readonly List<float> _colours = new();
        private readonly List<int> _indices = new();

        public int VertexCount => _positions.Count / 3;

        public void Vertex(double x, double y, double z, float u, float v, float r, float g, float b, float a)
        {
            _positions.Add((float)x);
            _positions.Add((float)y);
            _positions.Add((float)z);
            _uvs.Add(u);
            _uvs.Add(v);
            _colours.Add(r);
            _colours.Add(g);
            _colours.Add(b);
            _colours.Add(a);
        }

        public void Triangle(int a, int b, int c)
        {
            _indices.Add(a);
            _indices.Add(b);
            _indices.Add(c);
        }

        public WaterTile Build(int tileX, int tileZ) => new(tileX, tileZ, _positions.ToArray(), _uvs.ToArray(), _colours.ToArray(), _indices.ToArray());
    }
}
