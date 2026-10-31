using System;

namespace Realm.Gen.Bake;

/// <summary>One dab of a terrain editor brush, in the edited region's own vertex grid.</summary>
/// <remarks>Thread-safety: immutable.</remarks>
public sealed record BrushStroke
{
    /// <summary>Centre, as a vertex column (can be fractional between vertices); 0 is the region's west edge.</summary>
    public required double CenterX { get; init; }

    /// <summary>Centre, as a vertex row (can be fractional between vertices); 0 is the region's north edge.</summary>
    public required double CenterZ { get; init; }

    /// <summary>Radius in vertices; a vertex at exactly this distance is not moved.</summary>
    public required double RadiusVertices { get; init; }

    /// <summary>
    /// How hard the dab pushes. For <see cref="BrushMode.Raise"/> and <see cref="BrushMode.Lower"/>, metres added or
    /// removed at the centre. For <see cref="BrushMode.Smooth"/> and <see cref="BrushMode.Flatten"/>, a blend rate
    /// from 0 (no change) to 1 (snap straight to the target) at the centre.
    /// </summary>
    public required double Strength { get; init; }

    /// <summary>What the dab does.</summary>
    public required BrushMode Mode { get; init; }

    /// <summary>The height <see cref="BrushMode.Flatten"/> moves vertices toward; ignored by every other mode.</summary>
    public double FlattenHeight { get; init; }

    /// <summary>Checks the stroke is usable.</summary>
    /// <exception cref="ArgumentException"><see cref="RadiusVertices"/> is not positive.</exception>
    public void Validate()
    {
        if (RadiusVertices <= 0)
        {
            throw new ArgumentException("A brush radius must be positive.", nameof(RadiusVertices));
        }
    }
}
