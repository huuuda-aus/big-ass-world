using System;
using System.Collections.Generic;
using System.Numerics;

namespace Realm.Gen.Props;

/// <summary>Collects convex solids into a <see cref="PropMesh"/>; every face is wound outward automatically.</summary>
internal sealed partial class PropMeshBuilder
{
    private readonly List<PropTriangle> _triangles = new();

    public PropMesh Build(string name) => new(name, _triangles);

    /// <summary>Axis-aligned box.</summary>
    public void Box(Vector3 min, Vector3 max, Vector3 colour)
    {
        Vector3 inside = (min + max) / 2;
        var c = new[]
        {
            new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, min.Y, min.Z),
            new Vector3(max.X, min.Y, max.Z), new Vector3(min.X, min.Y, max.Z),
            new Vector3(min.X, max.Y, min.Z), new Vector3(max.X, max.Y, min.Z),
            new Vector3(max.X, max.Y, max.Z), new Vector3(min.X, max.Y, max.Z),
        };
        Quad(c[0], c[1], c[2], c[3], inside, colour);
        Quad(c[4], c[5], c[6], c[7], inside, colour);
        Quad(c[0], c[1], c[5], c[4], inside, colour);
        Quad(c[1], c[2], c[6], c[5], inside, colour);
        Quad(c[2], c[3], c[7], c[6], inside, colour);
        Quad(c[3], c[0], c[4], c[7], inside, colour);
    }

    /// <summary>Gable roof (triangular prism) with the ridge along X, centred on the footprint's Z middle.</summary>
    public void GableRoofAlongX(float x0, float x1, float z0, float z1, float eaveY, float ridgeY, Vector3 colour)
    {
        float zm = (z0 + z1) / 2;
        var a = new Vector3(x0, eaveY, z0);
        var b = new Vector3(x1, eaveY, z0);
        var c = new Vector3(x1, eaveY, z1);
        var d = new Vector3(x0, eaveY, z1);
        var r0 = new Vector3(x0, ridgeY, zm);
        var r1 = new Vector3(x1, ridgeY, zm);
        var inside = new Vector3((x0 + x1) / 2, eaveY + ((ridgeY - eaveY) / 3), zm);
        Quad(a, b, c, d, inside, colour);
        Quad(a, b, r1, r0, inside, colour);
        Quad(d, c, r1, r0, inside, colour);
        Triangle(a, d, r0, inside, colour);
        Triangle(b, c, r1, inside, colour);
    }

    /// <summary>Upright cylinder.</summary>
    public void Cylinder(float cx, float cz, float baseY, float radius, float height, int sides, Vector3 colour)
    {
        var inside = new Vector3(cx, baseY + (height / 2), cz);
        for (int i = 0; i < sides; i++)
        {
            (float x0, float z0) = Ring(cx, cz, radius, i, sides);
            (float x1, float z1) = Ring(cx, cz, radius, i + 1, sides);
            var b0 = new Vector3(x0, baseY, z0);
            var b1 = new Vector3(x1, baseY, z1);
            var t0 = new Vector3(x0, baseY + height, z0);
            var t1 = new Vector3(x1, baseY + height, z1);
            Quad(b0, b1, t1, t0, inside, colour);
            Triangle(new Vector3(cx, baseY, cz), b0, b1, inside, colour);
            Triangle(new Vector3(cx, baseY + height, cz), t0, t1, inside, colour);
        }
    }

    /// <summary>Upright cone with a flat base.</summary>
    public void Cone(float cx, float cz, float baseY, float radius, float height, int sides, Vector3 colour)
    {
        var inside = new Vector3(cx, baseY + (height / 4), cz);
        var apex = new Vector3(cx, baseY + height, cz);
        for (int i = 0; i < sides; i++)
        {
            (float x0, float z0) = Ring(cx, cz, radius, i, sides);
            (float x1, float z1) = Ring(cx, cz, radius, i + 1, sides);
            var b0 = new Vector3(x0, baseY, z0);
            var b1 = new Vector3(x1, baseY, z1);
            Triangle(b0, b1, apex, inside, colour);
            Triangle(new Vector3(cx, baseY, cz), b0, b1, inside, colour);
        }
    }

    /// <summary>Low-poly ellipsoid.</summary>
    public void Ellipsoid(Vector3 centre, Vector3 radii, int rings, int sides, Vector3 colour)
    {
        Vector3 Point(int ring, int side)
        {
            double polar = Math.PI * ring / rings;
            double azimuth = 2 * Math.PI * side / sides;
            return centre + new Vector3(
                radii.X * (float)(Math.Sin(polar) * Math.Cos(azimuth)),
                radii.Y * (float)Math.Cos(polar),
                radii.Z * (float)(Math.Sin(polar) * Math.Sin(azimuth)));
        }

        for (int ring = 0; ring < rings; ring++)
        {
            for (int side = 0; side < sides; side++)
            {
                Vector3 a = Point(ring, side);
                Vector3 b = Point(ring, side + 1);
                Vector3 c = Point(ring + 1, side + 1);
                Vector3 d = Point(ring + 1, side);
                if (ring == 0)
                {
                    Triangle(a, c, d, centre, colour);
                }
                else if (ring == rings - 1)
                {
                    Triangle(a, b, c, centre, colour);
                }
                else
                {
                    Quad(a, b, c, d, centre, colour);
                }
            }
        }
    }

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside, Vector3 colour)
    {
        Triangle(a, b, c, inside, colour);
        Triangle(a, c, d, inside, colour);
    }

    // Wind the triangle so its normal points away from a point inside the (convex) solid.
    internal void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 inside, Vector3 colour)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a);
        if (Vector3.Dot(normal, a - inside) < 0)
        {
            (b, c) = (c, b);
        }

        _triangles.Add(new PropTriangle(a, b, c, colour));
    }

    private static (float X, float Z) Ring(float cx, float cz, float radius, int i, int sides)
    {
        double angle = 2 * Math.PI * i / sides;
        return (cx + (radius * (float)Math.Cos(angle)), cz + (radius * (float)Math.Sin(angle)));
    }
}
