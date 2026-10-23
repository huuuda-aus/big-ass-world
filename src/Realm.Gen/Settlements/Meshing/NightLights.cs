using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>
/// The fake light of lit windows at night: glowing panes painted on the facade of a far house, and the soft glow a lit window throws
/// on the street and on the walls around it. Ported from the CityGen viewer (<c>facadeMesh</c>, <c>windowAura</c>); the game shows them
/// only while a house is occupied, so one house is one draw instead of a light per window.
/// </summary>
/// <remarks>Determinism: pure functions. Thread-safety: pure.</remarks>
public static class NightLights
{
    /// <summary>Colour of a lit window pane.</summary>
    public static Vector3 WindowGlow { get; } = new(1.0f, 0.78f, 0.42f);

    /// <summary>Colour of the light a lit window throws.</summary>
    public static Vector3 AuraColour { get; } = new(1.0f, 0.7f, 0.36f);

    /// <summary>How far a lit window lights the street and the walls round it, in metres.</summary>
    public const double AuraRadiusMeters = 5;

    /// <summary>Brightness of the glow at its centre for a ground-floor window.</summary>
    public const double AuraStrength = 0.275;

    /// <summary>Each floor up lights the street this much less.</summary>
    public const double AuraPerLevel = 0.72;

    /// <summary>How far neighbouring houses are searched for walls to light, in metres.</summary>
    public const double NeighbourSearchMeters = 40;

    private const double GlowOffset = 0.075;
    private const int PoolSegments = 16;
    private const double PoolLift = 0.085;
    private const double HaloRadius = 1.8;
    private const double HaloOffset = 0.06;
    private const double HaloShare = 0.7;
    private const double LampOffset = 0.06;
    private const double PatchOffset = 0.07;
    private const double PatchShare = 0.9;
    private const double MinFacing = 0.05;
    private const double MinPatchGain = 0.03;
    private const double MinPatchMeters = 0.1;
    private const double MinHeight = 0.02;

    /// <summary>The lit windows of a house, as openings on its outer walls.</summary>
    /// <param name="building">The house.</param>
    /// <param name="interior">Its interior.</param>
    /// <returns>One opening per window whose room keeps a lamp.</returns>
    public static IReadOnlyList<WallOpening> LitWindows(Building building, Interior interior)
    {
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(interior);
        var result = new List<WallOpening>();
        ConvexPoly fp = building.Footprint;
        for (int level = 0; level < interior.Floors.Count; level++)
        {
            foreach (Window w in interior.Floors[level].Windows.Where(w => RoomLighting.HasLamp(interior, level, w.Room)))
            {
                int edge = BuildingMesher.ExteriorEdge(interior, w.Room, w.Position);
                if (edge < 0)
                {
                    continue;
                }

                Vec2 a = fp.Points[edge];
                double u = Vec2.Dot(w.Position - a, fp.Edge(edge).Normalized());
                double bottom = (level * interior.FloorHeight) + w.Sill;
                result.Add(new WallOpening(edge, u, w.Width, bottom, bottom + w.Height, false));
            }
        }

        return result;
    }

    /// <summary>Glowing panes over the painted windows of a far house.</summary>
    /// <param name="building">The house.</param>
    /// <param name="interior">Its interior.</param>
    /// <returns>A quad per lit window just in front of the far mesh's painted window, in the same frame as <see cref="BuildingMesher.Far"/>; null when no room keeps a lamp.</returns>
    public static PropMesh? Glow(Building building, Interior interior)
    {
        var b = new PropMeshBuilder();
        ConvexPoly fp = building.Footprint;
        foreach (WallOpening o in LitWindows(building, interior))
        {
            Vec2 a = fp.Points[o.Edge];
            Vec2 dir = fp.Edge(o.Edge).Normalized();
            Vector3 outward = new((float)dir.Y, 0, (float)-dir.X);
            Vector3 At(double u, double v) => new Vector3((float)(a.X + (dir.X * u)), (float)v, (float)(a.Y + (dir.Y * u))) + (outward * (float)GlowOffset);
            double half = o.Width / 2;
            b.FacingQuad(At(o.U - half, o.Bottom), At(o.U + half, o.Bottom), At(o.U + half, o.Top), At(o.U - half, o.Top), outward, WindowGlow);
        }

        return b.BuildOrNull($"building.{building.Id}.glow");
    }

    /// <summary>The glow the lit windows of a house throw on the street and the walls around them.</summary>
    /// <param name="building">The house.</param>
    /// <param name="interior">Its interior.</param>
    /// <param name="baseY">Height of the house's floor in the city's frame, in metres.</param>
    /// <param name="ground">The ground, for the pool of light on the street.</param>
    /// <param name="neighbours">Every house of the city (this one is skipped for its own wall's patch).</param>
    /// <returns>Triangles in the city's frame, three vertices each: a half disc on the street in front of each window, a halo on its wall and a patch on every other wall in range that faces it.</returns>
    public static IReadOnlyList<AuraVertex> Aura(Building building, Interior interior, double baseY, IGroundSampler ground, IReadOnlyList<AuraNeighbour> neighbours)
    {
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(interior);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(neighbours);
        var vertices = new List<AuraVertex>();
        ConvexPoly fp = building.Footprint;
        double storey = interior.FloorHeight;
        Vec2 centre = fp.Centroid;
        AuraNeighbour[] near = neighbours.Where(n => Vec2.Distance(n.Footprint.Centroid, centre) < NeighbourSearchMeters + 20).ToArray();

        void Triangle(Vector3 p, Vector3 q, Vector3 r, Vector2 up, Vector2 uq, Vector2 ur, Vector3 colour)
        {
            vertices.Add(new AuraVertex(p, up, colour));
            vertices.Add(new AuraVertex(q, uq, colour));
            vertices.Add(new AuraVertex(r, ur, colour));
        }

        foreach (WallOpening o in LitWindows(building, interior))
        {
            Vec2 a = fp.Points[o.Edge];
            Vec2 dir = fp.Edge(o.Edge).Normalized();
            var normal = new Vec2(dir.Y, -dir.X); // outwards
            int level = (int)Math.Floor((o.Bottom - 0.001) / storey);
            double strength = AuraStrength * Math.Pow(AuraPerLevel, Math.Max(0, level));
            Vector3 colour = AuraColour * (float)strength;
            Vec2 window = a + (dir * o.U);
            Vector3 At(double s, double t, double h) => new((float)(window.X + (dir.X * s) + (normal.X * t)), (float)h, (float)(window.Y + (dir.Y * s) + (normal.Y * t)));
            double GroundAt(double s, double t) => ground.HeightAt(window.X + (dir.X * s) + (normal.X * t), window.Y + (dir.Y * s) + (normal.Y * t)) + PoolLift;

            // The pool on the street: a half disc, brightest at the foot of the wall.
            double radius = AuraRadiusMeters;
            (Vector3 P, Vector2 Uv) Rim(int i)
            {
                double angle = (-Math.PI / 2) + (Math.PI * i / PoolSegments);
                double s = Math.Sin(angle) * radius, t = Math.Cos(angle) * radius;
                return (At(s, t, GroundAt(s, t)), new Vector2((float)(0.5 + (s / (2 * radius))), (float)(0.5 + (t / (2 * radius)))));
            }

            Vector3 foot = At(0, 0, GroundAt(0, 0));
            for (int i = 0; i < PoolSegments; i++)
            {
                (Vector3 p, Vector2 pu) = Rim(i);
                (Vector3 q, Vector2 qu) = Rim(i + 1);
                Triangle(foot, p, q, new Vector2(0.5f, 0.5f), pu, qu, colour);
            }

            // The halo on the wall round the window.
            double centreY = baseY + ((o.Bottom + o.Top) / 2);
            double low = Math.Max(baseY + MinHeight, centreY - HaloRadius), high = centreY + HaloRadius;
            float vLow = (float)((low - (centreY - HaloRadius)) / (2 * HaloRadius));
            Vector3 halo = colour * (float)HaloShare;
            Vector3 c00 = At(-HaloRadius, HaloOffset, low), c10 = At(HaloRadius, HaloOffset, low), c11 = At(HaloRadius, HaloOffset, high), c01 = At(-HaloRadius, HaloOffset, high);
            Triangle(c00, c10, c11, new Vector2(0, vLow), new Vector2(1, vLow), new Vector2(1, 1), halo);
            Triangle(c00, c11, c01, new Vector2(0, vLow), new Vector2(1, 1), new Vector2(0, 1), halo);

            // The same light falling on other walls in range: the house across the alley, the sides of the houses next door.
            var lamp = new Vec2(window.X + (normal.X * LampOffset), window.Y + (normal.Y * LampOffset));
            foreach (AuraNeighbour other in near)
            {
                ConvexPoly ofp = other.Footprint;
                for (int k = 0; k < ofp.Count; k++)
                {
                    if (other.Id == building.Id && k == o.Edge)
                    {
                        continue; // the window's own wall already has its halo
                    }

                    Vec2 wa = ofp.Points[k];
                    Vec2 e = ofp.Edge(k).Normalized();
                    double length = ofp.EdgeLength(k);
                    var m = new Vec2(e.Y, -e.X); // outwards from that wall
                    double sNear = Math.Clamp(Vec2.Dot(lamp - wa, e), 0, length);
                    Vec2 q = wa + (e * sNear);
                    Vec2 g = lamp - q;
                    double dist = g.Length;
                    if (dist >= radius || dist < 0.05)
                    {
                        continue;
                    }

                    double facing = Vec2.Dot(g, m) / dist; // 1: lamp straight in front; at most 0: the wall faces away
                    if (facing <= MinFacing)
                    {
                        continue;
                    }

                    double gain = Math.Pow(1 - (dist / radius), 2) * facing;
                    if (gain < MinPatchGain)
                    {
                        continue;
                    }

                    double rr = Math.Sqrt((radius * radius) - (dist * dist));
                    double s0 = Math.Max(0, sNear - rr), s1 = Math.Min(length, sNear + rr);
                    double h0 = Math.Max(other.BaseY + MinHeight, centreY - rr), h1 = Math.Min(other.BaseY + other.Height, centreY + rr);
                    if (s1 - s0 < MinPatchMeters || h1 - h0 < MinPatchMeters)
                    {
                        continue;
                    }

                    Vector3 OnWall(double sv, double hv) => new((float)(wa.X + (e.X * sv) + (m.X * PatchOffset)), (float)hv, (float)(wa.Y + (e.Y * sv) + (m.Y * PatchOffset)));
                    Vector2 UvOf(double sv, double hv) => new((float)(0.5 + ((sv - sNear) / (2 * rr))), (float)(0.5 + ((hv - centreY) / (2 * rr))));
                    Vector3 patch = colour * (float)(gain * PatchShare);
                    Triangle(OnWall(s0, h0), OnWall(s1, h0), OnWall(s1, h1), UvOf(s0, h0), UvOf(s1, h0), UvOf(s1, h1), patch);
                    Triangle(OnWall(s0, h0), OnWall(s1, h1), OnWall(s0, h1), UvOf(s0, h0), UvOf(s1, h1), UvOf(s0, h1), patch);
                }
            }
        }

        return vertices;
    }
}
