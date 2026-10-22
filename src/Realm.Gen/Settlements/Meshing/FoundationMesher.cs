using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>
/// Builds what holds a building up on sloping ground: a stacked-stone plinth, wooden pilings, or a pier with a deck, plus steps
/// down from the front door. All heights are relative to the building's floor (<see cref="BuildingSite.BaseY"/>), so the mesh is
/// placed with the floor at 0 like the rest of the building.
/// </summary>
/// <remarks>Determinism: pure; variety comes from the building ID. Thread-safety: pure.</remarks>
internal static class FoundationMesher
{
    private const double CourseMeters = 0.45;
    private const double StoneLipMeters = 0.12;
    private const double BuriedMeters = 0.4;
    private const double PostSpacingMeters = 2.4;
    private const double PostRadius = 0.17;
    private const int PostSides = 6;
    private const double PostEmbedMeters = 0.7;
    private const double BeamDrop = 0.3;
    private const double BeamSize = 0.3;
    private const double InsetForPosts = 0.35;
    private const double DeckWidthMeters = 1.2;
    private const double DeckThickness = 0.18;
    private const double DeckDrop = 0.2;
    private const double RailHeight = 1.0;
    private const double RailSpacing = 2.0;
    private const double RailPost = 0.1;
    private const double StepDepth = 0.4;
    private const double StepWidth = 1.8;
    private const double StepRise = 0.26;
    private const double StepBuried = 0.3;
    private const double MinStepDrop = 0.05;
    private const int MaxSteps = 16;
    private const double StepRunSample = 1.2;

    private static readonly Vector3 StoneColour = SettlementPalette.Hex(0x8c887e);
    private static readonly float[] StoneShades = { 0.82f, 0.9f, 0.96f, 1.0f, 0.88f };
    private static readonly Vector3 PostColour = SettlementPalette.Hex(0x5b3b22);
    private static readonly Vector3 DeckColour = SettlementPalette.Hex(0x7a5a3a);

    /// <summary>Adds the foundation and the front steps of a building to a mesh builder.</summary>
    /// <param name="b">The builder (the building's visual or collision mesh).</param>
    /// <param name="building">The building.</param>
    /// <param name="site">Where its floor sits.</param>
    /// <param name="doorEdge">Index of the footprint edge the front door is on.</param>
    /// <param name="doorU">Distance of the front door's centre from the start of that edge, in metres.</param>
    public static void Add(PropMeshBuilder b, Building building, BuildingSite site, int doorEdge, double doorU)
    {
        if (site.Style is not { } style)
        {
            return;
        }

        ConvexPoly fp = building.Footprint;
        Vec2 doorAt = fp.Points[doorEdge] + (fp.Edge(doorEdge).Normalized() * doorU);
        double floor = site.LowestGroundY - site.BaseY;
        double stepStart;
        switch (style)
        {
            case FoundationStyle.Stone:
                StoneCourses(b, Loop(fp, StoneLipMeters), floor - BuriedMeters, 0, building.Id);
                stepStart = StoneLipMeters;
                break;
            case FoundationStyle.Wood:
                Pilings(b, site, fp, doorEdge, 0);
                stepStart = 0.1;
                break;
            default:
                Pilings(b, site, fp, doorEdge, DeckWidthMeters);
                Deck(b, fp, doorEdge);
                stepStart = DeckWidthMeters;
                break;
        }

        Steps(b, fp, site, doorEdge, doorAt, stepStart, style == FoundationStyle.Stone);
    }

    /// <summary>Adds a stacked-stone plinth under a round or polygonal object, such as a fountain.</summary>
    /// <param name="b">The builder.</param>
    /// <param name="loop">Outline of the plinth in plan coordinates.</param>
    /// <param name="bottom">Lowest height, relative to the object's base.</param>
    /// <param name="seed">Varies the stone shades.</param>
    public static void Plinth(PropMeshBuilder b, IReadOnlyList<Vec2> loop, double bottom, int seed) =>
        StoneCourses(b, loop, bottom, 0, seed);

    // ---- stone -------------------------------------------------------------------------------------------------

    private static void StoneCourses(PropMeshBuilder b, IReadOnlyList<Vec2> loop, double bottom, double top, int seed)
    {
        double height = top - bottom;
        if (height <= 0.01)
        {
            return;
        }

        int courses = Math.Max(1, (int)Math.Ceiling(height / CourseMeters));
        double thickness = height / courses;
        Vec2 centre = Average(loop);
        for (int k = 0; k < courses; k++)
        {
            uint h = BuildingSite.Hash(seed + (k * 31));
            // Each course sticks out or in a few centimetres so the wall reads as stacked stones.
            double wobble = ((int)(h % 5) - 2) * 0.02;
            var ring = loop.Select(p => P(p + ((p - centre).Normalized() * wobble), bottom + (k * thickness))).ToList();
            b.Prism(ring, (float)thickness, StoneColour * StoneShades[(h >> 3) % (uint)StoneShades.Length]);
        }
    }

    private static List<Vec2> Loop(ConvexPoly fp, double outset)
    {
        Vec2 centre = fp.Centroid;
        return fp.Points.Select(p => p + ((p - centre).Normalized() * outset)).ToList();
    }

    // ---- wood --------------------------------------------------------------------------------------------------

    private static void Pilings(PropMeshBuilder b, BuildingSite site, ConvexPoly fp, int doorEdge, double outset)
    {
        List<Vec2> outline = outset > 0 ? Loop(fp, outset) : fp.Points.ToList();
        Vec2 centre = fp.Centroid;

        // Posts at every corner...
        foreach (Vec2 corner in outline)
        {
            Post(b, site, corner + ((centre - corner).Normalized() * 0.25), outset > 0 ? DeckDrop : BeamDrop + (BeamSize / 2));
        }

        // ...and on a grid under the floor.
        Vec2 u = fp.Edge(doorEdge).Normalized();
        var v = new Vec2(-u.Y, u.X);
        ConvexPoly inner = fp.Inset(Enumerable.Repeat(InsetForPosts, fp.Count).ToArray()) ?? fp;
        double uMin = double.MaxValue, uMax = double.MinValue, vMin = double.MaxValue, vMax = double.MinValue;
        foreach (Vec2 p in fp.Points)
        {
            double pu = Vec2.Dot(p, u), pv = Vec2.Dot(p, v);
            uMin = Math.Min(uMin, pu);
            uMax = Math.Max(uMax, pu);
            vMin = Math.Min(vMin, pv);
            vMax = Math.Max(vMax, pv);
        }

        for (double pu = uMin + PostSpacingMeters; pu < uMax - 0.5; pu += PostSpacingMeters)
        {
            for (double pv = vMin + PostSpacingMeters; pv < vMax - 0.5; pv += PostSpacingMeters)
            {
                Vec2 p = (u * pu) + (v * pv);
                if (Contains(inner, p))
                {
                    Post(b, site, p, BeamDrop + (BeamSize / 2));
                }
            }
        }

        // A beam under each wall carries the floor.
        for (int i = 0; i < fp.Count; i++)
        {
            Vec2 a = fp.Points[i];
            Vec2 e = fp.Edge(i);
            Vec2 mid = a + (e * 0.5);
            Vec2 dir = e.Normalized();
            var along = new Vector3((float)dir.X, 0, (float)dir.Y);
            var across = new Vector3((float)-dir.Y, 0, (float)dir.X);
            b.OrientedBox(P(mid, -BeamDrop), along, Vector3.UnitY, across, new Vector3((float)e.Length, (float)BeamSize, (float)BeamSize), PostColour);
        }
    }

    private static void Post(PropMeshBuilder b, BuildingSite site, Vec2 at, double topBelowFloor)
    {
        double bottom = site.Ground.HeightAt(at.X, at.Y) - site.BaseY - PostEmbedMeters;
        double top = -topBelowFloor;
        if (top - bottom < 0.1)
        {
            return;
        }

        b.Tube(P(at, bottom), P(at, top), (float)PostRadius, (float)PostRadius, PostSides, PostColour);
    }

    // ---- pier deck ---------------------------------------------------------------------------------------------

    private static void Deck(PropMeshBuilder b, ConvexPoly fp, int doorEdge)
    {
        List<Vec2> outer = Loop(fp, DeckWidthMeters);
        b.Prism(outer.Select(p => P(p, -DeckDrop)).ToList(), (float)DeckThickness, DeckColour);

        // A rail round the deck except along the front, where the steps start.
        for (int i = 0; i < outer.Count; i++)
        {
            if (i == doorEdge)
            {
                continue;
            }

            Vec2 a = outer[i];
            Vec2 c = outer[(i + 1) % outer.Count];
            double length = Vec2.Distance(a, c);
            int posts = Math.Max(2, (int)Math.Ceiling(length / RailSpacing) + 1);
            for (int k = 0; k < posts; k++)
            {
                Vec2 p = Vec2.Lerp(a, c, k / (double)(posts - 1));
                b.OrientedBox(P(p, (RailHeight / 2) - DeckDrop + DeckThickness), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3((float)RailPost, (float)RailHeight, (float)RailPost), PostColour);
            }

            Vec2 dir = (c - a).Normalized();
            b.OrientedBox(P(Vec2.Lerp(a, c, 0.5), RailHeight - DeckDrop + DeckThickness - 0.05), new Vector3((float)dir.X, 0, (float)dir.Y), Vector3.UnitY, new Vector3((float)-dir.Y, 0, (float)dir.X), new Vector3((float)length, 0.1f, 0.12f), PostColour);
        }
    }

    // ---- steps -------------------------------------------------------------------------------------------------

    private static void Steps(PropMeshBuilder b, ConvexPoly fp, BuildingSite site, int doorEdge, Vec2 doorAt, double startMeters, bool stone)
    {
        Vec2 outward = fp.OutwardNormal(doorEdge);
        Vec2 along = fp.Edge(doorEdge).Normalized();
        double groundAhead = site.Ground.HeightAt(doorAt.X + (outward.X * (startMeters + StepRunSample)), doorAt.Y + (outward.Y * (startMeters + StepRunSample))) - site.BaseY;
        double drop = -groundAhead;
        if (drop < MinStepDrop)
        {
            return;
        }

        int count = Math.Min(MaxSteps, Math.Max(1, (int)Math.Ceiling(drop / StepRise)));
        double rise = drop / count;
        Vector3 colour = stone ? StoneColour : DeckColour;
        var up = Vector3.UnitY;
        var alongV = new Vector3((float)outward.X, 0, (float)outward.Y);
        var acrossV = new Vector3((float)along.X, 0, (float)along.Y);
        for (int k = 1; k <= count; k++)
        {
            double top = -k * rise;
            Vec2 centre = doorAt + (outward * (startMeters + ((k - 0.5) * StepDepth)));
            double bottom = Math.Min(site.Ground.HeightAt(centre.X, centre.Y) - site.BaseY, top - StepBuried) - StepBuried;
            double height = top - bottom;
            b.OrientedBox(P(centre, bottom + (height / 2)), alongV, up, acrossV, new Vector3((float)StepDepth, (float)height, (float)StepWidth), colour);
        }
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    private static Vec2 Average(IReadOnlyList<Vec2> points)
    {
        double x = 0, y = 0;
        foreach (Vec2 p in points)
        {
            x += p.X;
            y += p.Y;
        }

        return new Vec2(x / points.Count, y / points.Count);
    }

    private static bool Contains(ConvexPoly poly, Vec2 p)
    {
        for (int i = 0; i < poly.Count; i++)
        {
            if (Vec2.Cross(poly.Edge(i), p - poly.Points[i]) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static Vector3 P(Vec2 p, double y) => new((float)p.X, (float)y, (float)p.Y);
}
