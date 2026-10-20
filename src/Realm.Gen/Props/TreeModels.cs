using System;
using System.Numerics;

namespace Realm.Gen.Props;

/// <summary>Builds simple low-poly trees for judging scale and as placeholder vegetation (CLAUDE.md P6).</summary>
/// <remarks>Determinism: pure functions of the arguments. Thread-safety: all methods are pure.</remarks>
public static class TreeModels
{
    /// <summary>Shortest tree the models are meant for, in metres.</summary>
    public const float MinHeightMeters = 4f;

    /// <summary>Tallest tree the models are meant for, in metres.</summary>
    public const float MaxHeightMeters = 10f;

    private const float RootDepthMeters = 0.3f;
    private const int Sides = 8;
    private const int Rings = 5;
    private static readonly Vector3 Bark = new(0.35f, 0.24f, 0.15f);
    private static readonly Vector3 PineLeaf = new(0.13f, 0.36f, 0.20f);
    private static readonly Vector3 OakLeaf = new(0.30f, 0.50f, 0.17f);
    private static readonly Vector3 PoplarLeaf = new(0.46f, 0.58f, 0.18f);

    /// <summary>Builds a tree whose tip is exactly <paramref name="heightMeters"/> above the ground.</summary>
    /// <param name="kind">Silhouette.</param>
    /// <param name="heightMeters">Total height from <see cref="MinHeightMeters"/> to <see cref="MaxHeightMeters"/>.</param>
    /// <returns>The model, origin at the foot of the trunk.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Height outside the supported range, or unknown kind.</exception>
    public static PropMesh Build(TreeKind kind, float heightMeters)
    {
        if (heightMeters < MinHeightMeters || heightMeters > MaxHeightMeters)
        {
            throw new ArgumentOutOfRangeException(nameof(heightMeters), heightMeters, $"Height must be {MinHeightMeters} to {MaxHeightMeters} m.");
        }

        float h = heightMeters;
        var b = new PropMeshBuilder();
        switch (kind)
        {
            case TreeKind.Pine:
                b.Cylinder(0, 0, -RootDepthMeters, 0.025f * h, (0.3f * h) + RootDepthMeters, Sides, Bark);
                b.Cone(0, 0, 0.18f * h, 0.20f * h, 0.38f * h, Sides, PineLeaf);
                b.Cone(0, 0, 0.42f * h, 0.15f * h, 0.34f * h, Sides, PineLeaf);
                b.Cone(0, 0, 0.64f * h, 0.10f * h, 0.36f * h, Sides, PineLeaf);
                break;
            case TreeKind.Oak:
                b.Cylinder(0, 0, -RootDepthMeters, 0.05f * h, (0.5f * h) + RootDepthMeters, Sides, Bark);
                b.Ellipsoid(new Vector3(0, 0.68f * h, 0), new Vector3(0.36f * h, 0.32f * h, 0.36f * h), Rings, Sides, OakLeaf);
                break;
            case TreeKind.Poplar:
                b.Cylinder(0, 0, -RootDepthMeters, 0.025f * h, (0.3f * h) + RootDepthMeters, Sides, Bark);
                b.Ellipsoid(new Vector3(0, 0.63f * h, 0), new Vector3(0.12f * h, 0.37f * h, 0.12f * h), Rings, Sides, PoplarLeaf);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown tree kind.");
        }

        return b.Build($"tree.{kind.ToString().ToLowerInvariant()}");
    }
}
