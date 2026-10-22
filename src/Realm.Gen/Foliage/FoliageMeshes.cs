using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Realm.Gen.Props;

namespace Realm.Gen.Foliage;

/// <summary>Placeholder low-poly models of the foliage styles, until hand-made art exists. Each style has a near and a far version.</summary>
/// <remarks>
/// Models stand with their base at height 0, 1 unit = 1 metre: an oak is about 11 m, a pine 11.5 m and a dead tree 8 m tall at scale 1, so a species' scale range sets how tall its trees get. Colours are per vertex; the game tints each
/// instance a little. Determinism: pure. Thread-safety: pure.
/// </remarks>
public static class FoliageMeshes
{
    /// <summary>The model styles.</summary>
    public static readonly IReadOnlyList<string> Styles = Array.AsReadOnly(new[] { "oak", "pine", "dead", "shrub", "tuft" });

    private static readonly Vector3 Bark = new(0.36f, 0.25f, 0.16f);
    private static readonly Vector3 DeadBark = new(0.45f, 0.40f, 0.34f);
    private static readonly Vector3 OakLeaf = new(0.30f, 0.50f, 0.18f);
    private static readonly Vector3 OakLeafLight = new(0.42f, 0.60f, 0.22f);
    private static readonly Vector3 PineLeaf = new(0.13f, 0.34f, 0.20f);
    private static readonly Vector3 PineLeafLight = new(0.18f, 0.42f, 0.24f);
    private static readonly Vector3 ShrubLeaf = new(0.36f, 0.46f, 0.20f);
    private static readonly Vector3 GrassBase = new(0.30f, 0.48f, 0.16f);
    private static readonly Vector3 GrassTip = new(0.66f, 0.80f, 0.34f);

    /// <summary>Picks the style for a species from its kind and tags.</summary>
    /// <param name="kind"><c>tree</c>, <c>bush</c> or <c>grass</c>.</param>
    /// <param name="tags">The species' tags: <c>conifer</c> gives a pine, <c>dead</c> a bare tree.</param>
    /// <returns>One of <see cref="Styles"/>.</returns>
    public static string StyleFor(string kind, IReadOnlyCollection<string> tags)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(tags);
        return kind switch
        {
            "grass" => "tuft",
            "bush" => "shrub",
            _ when tags.Contains("dead") => "dead",
            _ when tags.Contains("conifer") => "pine",
            _ => "oak",
        };
    }

    /// <summary>Builds a model.</summary>
    /// <param name="style">One of <see cref="Styles"/>.</param>
    /// <param name="far">Whether to build the simpler version for distance.</param>
    /// <returns>The model.</returns>
    /// <exception cref="ArgumentException">The style is unknown.</exception>
    public static PropMesh Build(string style, bool far)
    {
        var b = new PropMeshBuilder();
        int sides = far ? 4 : 7;
        switch (style)
        {
            case "oak":
                b.Cylinder(0, 0, 0, 0.36f, 5.4f, far ? 4 : 6, Bark);
                b.Ellipsoid(new Vector3(0, 7.8f, 0), new Vector3(2.8f, 3.0f, 2.8f), far ? 2 : 4, sides, OakLeaf);
                if (!far)
                {
                    b.Ellipsoid(new Vector3(1.3f, 9.4f, 0.5f), new Vector3(1.9f, 1.6f, 1.9f), 3, 6, OakLeafLight);
                }

                break;
            case "pine":
                b.Cylinder(0, 0, 0, 0.30f, 3.8f, far ? 4 : 6, Bark);
                b.Cone(0, 0, 2.6f, 2.4f, 4.6f, sides, PineLeaf);
                b.Cone(0, 0, 5.3f, 1.9f, 4.2f, sides, PineLeafLight);
                if (!far)
                {
                    b.Cone(0, 0, 7.9f, 1.3f, 3.6f, sides, PineLeaf);
                }

                break;
            case "dead":
                b.Tube(new Vector3(0, 0, 0), new Vector3(0, 8.0f, 0), 0.42f, 0.14f, far ? 4 : 6, DeadBark);
                if (!far)
                {
                    b.Tube(new Vector3(0, 4.8f, 0), new Vector3(2.4f, 7.0f, 0.5f), 0.16f, 0.06f, 4, DeadBark);
                    b.Tube(new Vector3(0, 6.0f, 0), new Vector3(-1.8f, 8.2f, -0.9f), 0.13f, 0.05f, 4, DeadBark);
                }

                break;
            case "shrub":
                b.Ellipsoid(new Vector3(0, 0.55f, 0), new Vector3(0.9f, 0.6f, 0.9f), far ? 2 : 3, far ? 4 : 6, ShrubLeaf);
                break;
            case "tuft":
                Tuft(b, far);
                break;
            default:
                throw new ArgumentException($"Unknown foliage style '{style}'.", nameof(style));
        }

        return b.Build($"foliage.{style}{(far ? ".far" : string.Empty)}");
    }

    // Three crossed blades-of-grass quads, dark at the root and light at the tip; both faces are emitted so it shows from any side.
    private static void Tuft(PropMeshBuilder b, bool far)
    {
        int blades = far ? 2 : 3;
        for (int i = 0; i < blades; i++)
        {
            double angle = Math.PI * i / blades;
            float dx = (float)Math.Cos(angle) * 0.28f;
            float dz = (float)Math.Sin(angle) * 0.28f;
            var left = new Vector3(-dx, 0, -dz);
            var right = new Vector3(dx, 0, dz);
            var tipLeft = new Vector3(-dx * 0.2f, 0.55f, -dz * 0.2f);
            var tipRight = new Vector3(dx * 0.2f, 0.55f, dz * 0.2f);
            Vector3 mid = (left + right + tipLeft + tipRight) / 4;
            // Two triangles per face; the builder winds each away from a point inside, so a point on each side gives both faces.
            Vector3 side = new(-dz, 0, dx);
            foreach (Vector3 inside in new[] { mid + side, mid - side })
            {
                b.Triangle(left, right, tipRight, inside, GrassBase);
                b.Triangle(left, tipRight, tipLeft, inside, GrassTip);
            }
        }
    }
}
