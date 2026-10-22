using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Realm.Core.Random;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>
/// Builds the centrepiece of a city's central square: fountain, pyre, execution block or market stalls. Ported from the
/// three.js viewer (landmarks.js); water jets, flames and embers are animated by the game, not baked into these meshes.
/// </summary>
/// <remarks>
/// Coordinates: the city's local frame (plan x is X, plan y is Z, height is Y), not relative to the landmark's centre.
/// Determinism: depends only on the landmark and the city seed. Thread-safety: pure.
/// </remarks>
public static class LandmarkMesher
{
    private static readonly Vector3 Stone = SettlementPalette.Hex(0x9a948a);
    private static readonly Vector3 Water = SettlementPalette.Hex(0x4a86ad);
    private static readonly Vector3 Log = SettlementPalette.Hex(0x5b3b22);
    private static readonly Vector3 Flame = SettlementPalette.Hex(0xffa030);
    private static readonly Vector3 Dark = SettlementPalette.Hex(0x3a1c14);
    private static readonly Vector3 Metal = SettlementPalette.Hex(0x6b6f75);
    private static readonly Vector3 Rope = SettlementPalette.Hex(0x8a7350);
    private static readonly Vector3 Counter = SettlementPalette.Hex(0x7a5a3a);
    private static readonly Vector3[] Awnings =
    {
        SettlementPalette.Hex(0xb23a2e), SettlementPalette.Hex(0x2e6fb2), SettlementPalette.Hex(0x3a8a46),
        SettlementPalette.Hex(0xc99a2e), SettlementPalette.Hex(0x7a3a8a), SettlementPalette.Hex(0xc9572e),
    };

    private static readonly Vector3[] Goods =
    {
        SettlementPalette.Hex(0xc0392b), SettlementPalette.Hex(0xe0a030), SettlementPalette.Hex(0x6a9a3a),
        SettlementPalette.Hex(0xd9c9a0), SettlementPalette.Hex(0x8a5a3a),
    };

    private const double PlinthMarginMeters = 0.8;
    private const int PlinthSides = 32;
    private const double PlinthBuriedMeters = 0.4;
    private const double PlinthMinDropMeters = 0.25;
    private const int BasinSides = 48;
    private const float Rim = 0.85f;
    private const float WallShare = 0.25f; // half the basin wall thickness (0.5 m)

    /// <summary>Builds a landmark.</summary>
    /// <param name="landmark">The landmark of a generated city.</param>
    /// <param name="citySeed">Seed of the city (for the layout of logs, flames and goods).</param>
    /// <param name="ground">The ground under the city; level ground when null.</param>
    /// <param name="baseY">Height of the landmark's base above the city origin. The mesh is built with its base at 0 (stalls follow the ground relative to it) and the caller places it at this height.</param>
    /// <returns>The meshes, or null when the landmark kind is <see cref="CentrepieceKind.None"/>.</returns>
    public static LandmarkMeshes? Build(Landmark landmark, int citySeed, IGroundSampler? ground = null, double baseY = 0)
    {
        ArgumentNullException.ThrowIfNull(landmark);
        ground ??= FlatGround.Instance;
        Rng rng = Rng.ForKey((ulong)(uint)citySeed, "landmark-mesh");
        Vector3 centre = new((float)landmark.Center.X, 0, (float)landmark.Center.Y);
        float radius = (float)landmark.Radius;
        LandmarkMeshes? meshes = landmark.Kind switch
        {
            CentrepieceKind.Fountain => Fountain(centre, radius),
            CentrepieceKind.Pyre => Pyre(centre, radius, rng),
            CentrepieceKind.ExecutionBlock => ExecutionBlock(centre, radius),
            CentrepieceKind.Market => Market(landmark, centre, rng, ground, baseY),
            _ => null,
        };
        return meshes is null || landmark.Kind == CentrepieceKind.Market ? meshes : meshes with { Solid = WithPlinth(meshes.Solid, landmark, citySeed, ground, baseY) };
    }

    // A stacked-stone ring under a centrepiece that stands on a slope, down to the lowest ground around it.
    private static PropMesh WithPlinth(PropMesh solid, Landmark landmark, int citySeed, IGroundSampler ground, double baseY)
    {
        double reach = landmark.Radius + PlinthMarginMeters;
        var ring = new List<Vec2>();
        double lowest = ground.HeightAt(landmark.Center.X, landmark.Center.Y);
        for (int i = 0; i < PlinthSides; i++)
        {
            double angle = 2 * Math.PI * i / PlinthSides;
            var p = new Vec2(landmark.Center.X + (reach * Math.Cos(angle)), landmark.Center.Y + (reach * Math.Sin(angle)));
            ring.Add(p);
            lowest = Math.Min(lowest, ground.HeightAt(p.X, p.Y));
        }

        if (lowest - baseY > -PlinthMinDropMeters)
        {
            return solid; // level enough: no plinth
        }

        double bottom = lowest - baseY - PlinthBuriedMeters;

        var b = new PropMeshBuilder();
        FoundationMesher.Plinth(b, ring, bottom, citySeed);
        return b.TriangleCount == 0 ? solid : new PropMesh(solid.Name, solid.Triangles.Concat(b.Build("plinth").Triangles).ToList());
    }

    // ---- fountain ----------------------------------------------------------------------------------------------

    private static LandmarkMeshes Fountain(Vector3 c, float r)
    {
        var solid = new PropMeshBuilder();
        var water = new PropMeshBuilder();

        solid.Lathe(c, BasinProfile(r, 0, Rim), BasinSides, Stone);
        Disc(water, c + (Vector3.UnitY * (Rim - 0.14f)), r - WallShare, BasinSides, Water);

        const float floor = 0.3f;
        float pillarHeight = r * 0.6f;
        solid.Tube(c + (Vector3.UnitY * floor), c + (Vector3.UnitY * (floor + pillarHeight)), 0.62f, 0.4f, 18, Stone);

        float upperRadius = r * 0.42f;
        float upperY = floor + pillarHeight;
        solid.Lathe(c + (Vector3.UnitY * upperY), BasinProfile(upperRadius, 0, 0.45f), 36, Stone);
        Disc(water, c + (Vector3.UnitY * (upperY + 0.33f)), upperRadius - WallShare, 36, Water);

        float spoutHeight = r * 0.35f;
        Vector3 spoutBase = c + (Vector3.UnitY * (upperY + 0.3f));
        solid.Tube(spoutBase, spoutBase + (Vector3.UnitY * spoutHeight), 0.22f, 0.12f, 12, Stone);
        solid.Ellipsoid(spoutBase + (Vector3.UnitY * (spoutHeight + 0.15f)), new Vector3(0.22f), 5, 12, Stone);

        return new LandmarkMeshes(solid.Build("landmark.fountain"), water.Build("landmark.fountain.water"), null, null, spoutBase + (Vector3.UnitY * spoutHeight));
    }

    // A basin: floor, outer wall, rim, inner wall and a raised inner floor, as a profile of (radius, height).
    private static (float, float)[] BasinProfile(float radius, float bottom, float rim) => new[]
    {
        (0.001f, bottom), (radius, bottom), (radius, rim), (radius - WallShare, rim), (radius - WallShare, bottom + 0.25f), (0.001f, bottom + 0.25f),
    };

    private static void Disc(PropMeshBuilder b, Vector3 centre, float radius, int sides, Vector3 colour)
    {
        var ring = new List<Vector3>();
        for (int i = 0; i < sides; i++)
        {
            float a = 2 * MathF.PI * i / sides;
            ring.Add(centre + new Vector3(radius * MathF.Cos(a), 0, radius * MathF.Sin(a)));
        }

        b.FacingPolygon(ring, Vector3.UnitY, colour);
    }

    // ---- pyre --------------------------------------------------------------------------------------------------

    private static LandmarkMeshes Pyre(Vector3 c, float r, Rng rng)
    {
        var solid = new PropMeshBuilder();
        int layers = Math.Max(5, (int)MathF.Round(r * 1.3f / 0.4f));
        for (int k = 0; k < layers; k++)
        {
            float y = 0.3f + (k * 0.4f);
            float half = r * (1 - (0.78f * k / layers));
            bool alongX = k % 2 == 0;
            int count = Math.Max(2, (int)MathF.Floor(2 * half / 0.5f) + 1);
            for (int j = 0; j < count; j++)
            {
                float offset = -half + (j * 2 * half / (count - 1));
                float length = 2 * MathF.Sqrt(MathF.Max((half * half) - (offset * offset), 0.3f));
                float jitter = ((float)rng.NextDouble() - 0.5f) * 0.12f;
                float skew = ((float)rng.NextDouble() - 0.5f) * 0.12f;
                Vector3 from = alongX ? new Vector3(-length / 2, y, offset + jitter) : new Vector3(offset + jitter, y, -length / 2);
                Vector3 to = alongX ? new Vector3(length / 2, y, offset + jitter + skew) : new Vector3(offset + jitter + skew, y, length / 2);
                solid.Tube(c + from, c + to, 0.2f, 0.24f, 7, Log);
            }
        }

        float top = 0.3f + (layers * 0.4f);
        var flames = new PropMeshBuilder();
        int tufts = Math.Max(6, (int)MathF.Round(r * 1.1f));
        float flameHeight = r * 1.15f;
        for (int i = 0; i < tufts; i++)
        {
            float w = flameHeight * (0.42f + ((float)rng.NextDouble() * 0.2f));
            float h = flameHeight * (0.65f + ((float)rng.NextDouble() * 0.4f));
            float angle = (float)rng.NextDouble() * 2 * MathF.PI;
            float dist = (float)rng.NextDouble() * r * 0.22f;
            Vector3 foot = c + new Vector3(MathF.Cos(angle) * dist, top - 0.5f, MathF.Sin(angle) * dist);
            foreach (Vector3 across in new[] { Vector3.UnitX, Vector3.UnitZ })
            {
                Vector3 normal = across == Vector3.UnitX ? Vector3.UnitZ : Vector3.UnitX;
                // A flame is a tapering diamond: wide in the middle, pointed at the top.
                Vector3 left = foot - (across * (w / 2)) + (Vector3.UnitY * (h * 0.35f));
                Vector3 right = foot + (across * (w / 2)) + (Vector3.UnitY * (h * 0.35f));
                flames.DoubleQuad(foot, right, foot + (Vector3.UnitY * h), left, normal, Flame);
            }
        }

        return new LandmarkMeshes(solid.Build("landmark.pyre"), null, flames.Build("landmark.pyre.flames"), c + (Vector3.UnitY * (top + (flameHeight * 0.5f))), null);
    }

    // ---- execution block and gallows ---------------------------------------------------------------------------

    private static LandmarkMeshes ExecutionBlock(Vector3 c, float r)
    {
        var b = new PropMeshBuilder();
        float w = MathF.Max(3.2f, r * 1.5f);
        float d = MathF.Max(2.6f, r * 1.2f);
        const float h = 0.9f;

        void Box(float x, float y, float z, float sx, float sy, float sz, Vector3 colour, float tilt = 0)
        {
            // Local plan z is mirrored relative to the viewer, which only mirrors the layout.
            Vector3 centre = c + new Vector3(x, y, -z);
            var along = new Vector3(MathF.Cos(tilt), MathF.Sin(tilt), 0);
            var up = new Vector3(-MathF.Sin(tilt), MathF.Cos(tilt), 0);
            b.OrientedBox(centre, along, up, Vector3.UnitZ, new Vector3(sx, sy, sz), colour);
        }

        Box(0, h / 2, 0, w, h, d, Log);
        for (int i = 0; i < 3; i++)
        {
            float step = h * (1 - ((i + 1) / 4f));
            Box(0, (step / 2) + 0.01f, (d / 2) + 0.3f + (i * 0.6f), MathF.Min(w * 0.4f, 2.2f), MathF.Max(step, 0.1f), 0.6f, Log);
        }

        b.Tube(c + new Vector3(0, h, -0.2f), c + new Vector3(0, h + 0.55f, -0.2f), 0.4f, 0.32f, 14, Log);
        Box(0, h + 0.56f, 0.2f, 0.62f, 0.05f, 0.28f, Dark);
        b.Tube(c + new Vector3(-w * 0.3f, h, -0.1f), c + new Vector3(-w * 0.3f, h + 0.5f, -0.1f), 0.25f, 0.22f, 10, Log);
        Box(-w * 0.3f, h + 0.95f, 0.1f, 0.07f, 0.95f, 0.07f, Log, 0.22f);
        Box((-w * 0.3f) - 0.13f, h + 1.35f, 0.1f, 0.32f, 0.22f, 0.05f, Metal, 0.22f);

        float gx = MathF.Min(w * 0.36f, 2.6f);
        float gz = -d * 0.28f;
        const float gh = 3.4f;
        Box(-gx, h + (gh / 2), gz, 0.26f, gh, 0.26f, Log);
        Box(gx, h + (gh / 2), gz, 0.26f, gh, 0.26f, Log);
        Box(0, h + gh, gz, (gx * 2) + 0.7f, 0.26f, 0.26f, Log);
        Box(-gx + 0.4f, h + gh - 0.4f, gz, 0.14f, 0.9f, 0.14f, Log, -0.75f);
        Box(gx - 0.4f, h + gh - 0.4f, gz, 0.14f, 0.9f, 0.14f, Log, 0.75f);
        Box(0, h + gh - 0.65f, gz, 0.04f, 1.3f, 0.04f, Rope);
        return new LandmarkMeshes(b.Build("landmark.block"), null, null, null, null);
    }

    // ---- market ------------------------------------------------------------------------------------------------

    private static LandmarkMeshes? Market(Landmark landmark, Vector3 c, Rng rng, IGroundSampler ground, double baseY)
    {
        var b = new PropMeshBuilder();
        foreach (ConvexPoly stall in landmark.Stalls)
        {
            Vec2 p0 = stall.Points[0];
            Vec2 p1 = stall.Points[1];
            Vec2 p3 = stall.Points[3];
            double width = Vec2.Distance(p0, p1);
            double depth = Vec2.Distance(p0, p3);
            if (width < 0.5 || depth < 0.5)
            {
                continue;
            }

            Vec2 t = (p1 - p0).Normalized(); // along the width
            Vec2 outward = (p3 - p0).Normalized(); // away from the centre of the square
            Vec2 middle = stall.Centroid;
            double floor = ground.HeightAt(middle.X, middle.Y) - baseY; // each stall stands on the ground under it
            var along = new Vector3((float)t.X, 0, (float)t.Y);
            var across = new Vector3((float)outward.X, 0, (float)outward.Y);
            Vector3 At(double u, double v, double y) => new((float)(middle.X + (t.X * u) + (outward.X * v)), (float)(y + floor), (float)(middle.Y + (t.Y * u) + (outward.Y * v)));
            void Box(double u, double v, double y, double sx, double sy, double sz, Vector3 colour) =>
                b.OrientedBox(At(u, v, y), along, Vector3.UnitY, across, new Vector3((float)sx, (float)sy, (float)sz), colour);

            foreach (int su in new[] { -1, 1 })
            {
                foreach (int sv in new[] { -1, 1 })
                {
                    Box(su * ((width / 2) - 0.08), sv * ((depth / 2) - 0.08), 1.1, 0.1, 2.2, 0.1, Log);
                }
            }

            Box(0, -depth * 0.3, 0.45, width - 0.1, 0.9, depth * 0.38, Counter);

            // The awning slopes down toward the front: tilt it about the stall's width axis.
            float tilt = -0.2f;
            Quaternion q = Quaternion.CreateFromAxisAngle(along, tilt);
            b.OrientedBox(At(0, 0, 2.3), along, Vector3.Transform(Vector3.UnitY, q), Vector3.Transform(across, q),
                new Vector3((float)width + 0.5f, 0.05f, (float)depth + 0.7f), Awnings[rng.NextInt(Awnings.Length)]);

            for (int i = 0; i < 3; i++)
            {
                Box((i - 1) * (width / 3.4), -depth * 0.3, 1.0, 0.34, 0.2, 0.3, Goods[rng.NextInt(Goods.Length)]);
            }
        }

        return b.TriangleCount == 0 ? null : new LandmarkMeshes(b.Build("landmark.market"), null, null, null, null);
    }
}
