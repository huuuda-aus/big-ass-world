using System;
using System.Collections.Generic;

namespace Realm.Gen.Terrain.Erosion;

/// <summary>Where a river ends.</summary>
public enum RiverMouth
{
    /// <summary>Flows into the sea.</summary>
    Sea = 0,

    /// <summary>Leaves the world at its edge.</summary>
    WorldEdge = 1,

    /// <summary>Joins a longer river (see <see cref="River.JoinsRiver"/>).</summary>
    Confluence = 2,

    /// <summary>Fades out: its flow fell below the river threshold, for example through water loss in a dry biome.</summary>
    Dried = 3,
}

/// <summary>One point along a river.</summary>
/// <param name="X">East coordinate in metres from the world centre.</param>
/// <param name="Z">South coordinate in metres from the world centre.</param>
/// <param name="Height">Water surface height in metres above sea level.</param>
/// <param name="FlowKm2">Flow in effective square kilometres of catchment.</param>
/// <param name="HalfWidthMeters">Half the width of the water here, set by <see cref="RiverWidth.Apply"/>; 0 means "not set" (see <see cref="HalfWidth"/>).</param>
public readonly record struct RiverPoint(double X, double Z, double Height, double FlowKm2, double HalfWidthMeters = 0)
{
    /// <summary>Half the width of the water at this point, in metres: the stored width, or the base width for its flow when none was set.</summary>
    public double HalfWidth => HalfWidthMeters > 0 ? HalfWidthMeters : RiverWidth.BaseHalfWidthMeters(FlowKm2);
}

/// <summary>A river as a polyline from its source to its mouth.</summary>
/// <remarks>Thread-safety: immutable.</remarks>
public sealed class River
{
    internal River(int index, IReadOnlyList<RiverPoint> points, RiverMouth mouth, int? joinsRiver, double lengthMeters)
    {
        Index = index;
        Points = points;
        Mouth = mouth;
        JoinsRiver = joinsRiver;
        LengthMeters = lengthMeters;

        double max = 0;
        foreach (RiverPoint point in points)
        {
            max = Math.Max(max, point.FlowKm2);
        }

        MaxFlowKm2 = max;
    }

    /// <summary>
    /// Position in <see cref="RiverNetwork.Rivers"/>. Rivers are ordered by the length of their whole flow path from
    /// the source to the sea or world edge, longest first, so the main stems come before their tributaries.
    /// A river that dries up early can therefore have a shorter <see cref="LengthMeters"/> than a later one.
    /// </summary>
    public int Index { get; }

    /// <summary>Points from the source downstream to the mouth; at least two.</summary>
    public IReadOnlyList<RiverPoint> Points { get; }

    /// <summary>How the river ends.</summary>
    public RiverMouth Mouth { get; }

    /// <summary>Index of the river this one joins when <see cref="Mouth"/> is <see cref="RiverMouth.Confluence"/>; otherwise null.</summary>
    public int? JoinsRiver { get; }

    /// <summary>Length along the polyline in metres.</summary>
    public double LengthMeters { get; }

    /// <summary>The largest flow along the river in effective km2.</summary>
    public double MaxFlowKm2 { get; }
}
