using System;
using System.Collections.Generic;
using System.Numerics;

namespace Realm.Gen.Props;

// Faces wound to match a given normal, plus the oriented solids that the city buildings need.
internal sealed partial class PropMeshBuilder
{
    private const float DegenerateArea = 1e-9f;

    /// <summary>Number of triangles added so far.</summary>
    public int TriangleCount => _triangles.Count;

    /// <summary>A triangle wound so that its front faces the given normal.</summary>
    public void Facing(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector3 colour)
    {
        Vector3 cross = Vector3.Cross(b - a, c - a);
        if (cross.LengthSquared() < DegenerateArea)
        {
            return;
        }

        if (Vector3.Dot(cross, normal) < 0)
        {
            (b, c) = (c, b);
        }

        _triangles.Add(new PropTriangle(a, b, c, colour));
    }

    /// <summary>A quad (two triangles) facing the given normal.</summary>
    public void FacingQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector3 colour)
    {
        Facing(a, b, c, normal, colour);
        Facing(a, c, d, normal, colour);
    }

    /// <summary>A quad visible from both sides.</summary>
    public void DoubleQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector3 colour)
    {
        FacingQuad(a, b, c, d, normal, colour);
        FacingQuad(a, b, c, d, -normal, colour);
    }

    /// <summary>A convex polygon as a triangle fan, facing the given normal.</summary>
    public void FacingPolygon(IReadOnlyList<Vector3> points, Vector3 normal, Vector3 colour)
    {
        for (int i = 1; i + 1 < points.Count; i++)
        {
            Facing(points[0], points[i], points[i + 1], normal, colour);
        }
    }

    /// <summary>A box with arbitrary axes (they need not be unit length, only mutually perpendicular).</summary>
    /// <param name="centre">Centre of the box.</param>
    /// <param name="along">Local X axis (unit).</param>
    /// <param name="up">Local Y axis (unit).</param>
    /// <param name="across">Local Z axis (unit).</param>
    /// <param name="size">Extent along each axis.</param>
    /// <param name="colour">Colour.</param>
    public void OrientedBox(Vector3 centre, Vector3 along, Vector3 up, Vector3 across, Vector3 size, Vector3 colour)
    {
        Vector3 hx = along * (size.X / 2);
        Vector3 hy = up * (size.Y / 2);
        Vector3 hz = across * (size.Z / 2);
        var c = new[]
        {
            centre - hx - hy - hz, centre + hx - hy - hz, centre + hx - hy + hz, centre - hx - hy + hz,
            centre - hx + hy - hz, centre + hx + hy - hz, centre + hx + hy + hz, centre - hx + hy + hz,
        };
        Quad(c[0], c[1], c[2], c[3], centre, colour);
        Quad(c[4], c[5], c[6], c[7], centre, colour);
        Quad(c[0], c[1], c[5], c[4], centre, colour);
        Quad(c[1], c[2], c[6], c[5], centre, colour);
        Quad(c[2], c[3], c[7], c[6], centre, colour);
        Quad(c[3], c[0], c[4], c[7], centre, colour);
    }

    /// <summary>A box lying along the ground direction <paramref name="yaw"/> (plan angle) with its base at the given height.</summary>
    public void YawBox(Vector3 baseCentre, float yaw, Vector3 size, Vector3 colour)
    {
        (Vector3 along, Vector3 across) = Axes(yaw);
        OrientedBox(baseCentre + (Vector3.UnitY * (size.Y / 2)), along, Vector3.UnitY, across, size, colour);
    }

    /// <summary>The horizontal unit vectors along and across a plan direction.</summary>
    public static (Vector3 Along, Vector3 Across) Axes(float yaw) =>
        (new Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw)), new Vector3(-MathF.Sin(yaw), 0, MathF.Cos(yaw)));

    /// <summary>A convex polygon extruded upward from a base height: walls, floor and top.</summary>
    /// <param name="loop">Base corners (any winding), all at the same height.</param>
    /// <param name="height">Extrusion height.</param>
    /// <param name="colour">Colour of the walls and top.</param>
    public void Prism(IReadOnlyList<Vector3> loop, float height, Vector3 colour)
    {
        Vector3 centroid = Vector3.Zero;
        foreach (Vector3 p in loop)
        {
            centroid += p;
        }

        centroid /= loop.Count;
        Vector3 inside = centroid + (Vector3.UnitY * (height / 2));
        var top = new List<Vector3>(loop.Count);
        foreach (Vector3 p in loop)
        {
            top.Add(p + (Vector3.UnitY * height));
        }

        for (int i = 0; i < loop.Count; i++)
        {
            int j = (i + 1) % loop.Count;
            Quad(loop[i], loop[j], top[j], top[i], inside, colour);
        }

        FacingPolygon(top, Vector3.UnitY, colour);
        FacingPolygon(loop, -Vector3.UnitY, colour);
    }

    /// <summary>A tube (cylinder or frustum) between two points, with end caps.</summary>
    public void Tube(Vector3 from, Vector3 to, float radiusFrom, float radiusTo, int sides, Vector3 colour)
    {
        Vector3 axis = Vector3.Normalize(to - from);
        Vector3 helper = MathF.Abs(axis.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 e1 = Vector3.Normalize(Vector3.Cross(axis, helper));
        Vector3 e2 = Vector3.Cross(axis, e1);
        Vector3 inside = (from + to) / 2;
        for (int i = 0; i < sides; i++)
        {
            float a0 = 2 * MathF.PI * i / sides;
            float a1 = 2 * MathF.PI * (i + 1) / sides;
            Vector3 d0 = (e1 * MathF.Cos(a0)) + (e2 * MathF.Sin(a0));
            Vector3 d1 = (e1 * MathF.Cos(a1)) + (e2 * MathF.Sin(a1));
            Quad(from + (d0 * radiusFrom), from + (d1 * radiusFrom), to + (d1 * radiusTo), to + (d0 * radiusTo), inside, colour);
            Triangle(from, from + (d0 * radiusFrom), from + (d1 * radiusFrom), inside, colour);
            Triangle(to, to + (d0 * radiusTo), to + (d1 * radiusTo), inside, colour);
        }
    }

    /// <summary>
    /// A solid of revolution about the vertical axis through a point. The profile is a polyline of (radius, height) pairs
    /// traversed with the solid on its left.
    /// </summary>
    public void Lathe(Vector3 axisBase, IReadOnlyList<(float Radius, float Height)> profile, int sides, Vector3 colour)
    {
        for (int k = 0; k + 1 < profile.Count; k++)
        {
            (float r0, float y0) = profile[k];
            (float r1, float y1) = profile[k + 1];
            float tr = r1 - r0;
            float ty = y1 - y0;
            for (int i = 0; i < sides; i++)
            {
                float a0 = 2 * MathF.PI * i / sides;
                float a1 = 2 * MathF.PI * (i + 1) / sides;
                float am = (a0 + a1) / 2;
                // Right-hand normal of the profile direction in the (radius, height) plane points away from the solid.
                var normal = new Vector3(ty * MathF.Cos(am), -tr, ty * MathF.Sin(am));
                Vector3 p00 = axisBase + new Vector3(r0 * MathF.Cos(a0), y0, r0 * MathF.Sin(a0));
                Vector3 p10 = axisBase + new Vector3(r0 * MathF.Cos(a1), y0, r0 * MathF.Sin(a1));
                Vector3 p11 = axisBase + new Vector3(r1 * MathF.Cos(a1), y1, r1 * MathF.Sin(a1));
                Vector3 p01 = axisBase + new Vector3(r1 * MathF.Cos(a0), y1, r1 * MathF.Sin(a0));
                FacingQuad(p00, p10, p11, p01, normal, colour);
            }
        }
    }

    /// <summary>All triangles added so far (for combining builders).</summary>
    public IReadOnlyList<PropTriangle> Triangles => _triangles;

    /// <summary>Adds triangles made elsewhere, optionally moved.</summary>
    public void AddAll(IEnumerable<PropTriangle> triangles) => _triangles.AddRange(triangles);
}
