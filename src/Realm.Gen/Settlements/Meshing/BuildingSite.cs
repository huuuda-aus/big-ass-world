using System;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>
/// Where a building's floor sits on uneven ground, and what holds it up. The floor is set just above the highest ground under the
/// footprint, so the terrain never rises into a room; a foundation fills the gap on the downhill side.
/// </summary>
/// <remarks>Determinism: pure; the style depends on the ward, the service, the drop and the building ID. Thread-safety: immutable.</remarks>
public sealed class BuildingSite
{
    /// <summary>Spacing of the ground samples taken along the footprint's edges, in metres.</summary>
    private const double SampleSpacingMeters = 2;

    /// <summary>Ground below the floor that is worth a foundation at all, in metres. Less than this the house just stands on the ground.</summary>
    private const double MinDropMeters = 0.2;

    /// <summary>The floor stands this far above the highest ground when the ground is uneven, in metres.</summary>
    private const double SillLiftMeters = 0.12;

    /// <summary>Ground flatter than this is treated as level, in metres.</summary>
    private const double LevelToleranceMeters = 0.05;

    /// <summary>How far in front of the door the ground is sampled, in metres.</summary>
    private const double FrontSampleMeters = 1.5;

    /// <summary>A drop smaller than this is always a stone plinth, in metres.</summary>
    private const double PilingMinDropMeters = 0.9;

    /// <summary>A wooden foundation higher than this becomes a pier with a deck, in metres.</summary>
    private const double PierMinDropMeters = 3.5;

    private BuildingSite(IGroundSampler ground, double baseY, double lowestY, FoundationStyle? style)
    {
        Ground = ground;
        BaseY = baseY;
        LowestGroundY = lowestY;
        Style = style;
    }

    /// <summary>The ground the site was planned on.</summary>
    public IGroundSampler Ground { get; }

    /// <summary>Height of the ground floor above the city origin, in metres. A building's mesh is built with its floor at 0 and placed here.</summary>
    public double BaseY { get; }

    /// <summary>Height of the lowest ground under the footprint, in metres above the city origin.</summary>
    public double LowestGroundY { get; }

    /// <summary>How far the lowest ground is below the floor, in metres.</summary>
    public double Drop => BaseY - LowestGroundY;

    /// <summary>The foundation style, or null when the building stands on the ground without one.</summary>
    public FoundationStyle? Style { get; }

    /// <summary>Plans the site of a building.</summary>
    /// <param name="building">The building.</param>
    /// <param name="ground">The ground under the city.</param>
    /// <returns>The site.</returns>
    public static BuildingSite Plan(Building building, IGroundSampler ground)
    {
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(ground);
        ConvexPoly fp = building.Footprint;
        Vec2 centre = fp.Centroid;
        double max = ground.HeightAt(centre.X, centre.Y);
        double min = max;
        for (int i = 0; i < fp.Count; i++)
        {
            Vec2 a = fp.Points[i];
            int steps = Math.Max(1, (int)Math.Ceiling(fp.EdgeLength(i) / SampleSpacingMeters));
            for (int s = 0; s < steps; s++)
            {
                Vec2 p = Vec2.Lerp(a, fp.EdgeEnd(i), s / (double)steps);
                double h = ground.HeightAt(p.X, p.Y);
                max = Math.Max(max, h);
                min = Math.Min(min, h);
            }
        }

        // The ground just outside the front door counts too, so a door never opens onto ground higher than its sill.
        Vec2 front = fp.Points[building.DoorEdge] + (fp.Edge(building.DoorEdge) * 0.5);
        Vec2 outward = fp.OutwardNormal(building.DoorEdge);
        max = Math.Max(max, ground.HeightAt(front.X + (outward.X * FrontSampleMeters), front.Y + (outward.Y * FrontSampleMeters)));

        bool level = max - min < LevelToleranceMeters;
        double baseY = level ? max : max + SillLiftMeters;
        if (baseY - min < MinDropMeters)
        {
            return new BuildingSite(ground, baseY, min, null);
        }

        return new BuildingSite(ground, baseY, min, ChooseStyle(building, baseY - min));
    }

    /// <summary>A site on level ground at the city origin: no foundation.</summary>
    /// <returns>The site.</returns>
    public static BuildingSite Level() => new(FlatGround.Instance, 0, 0, null);

    private static FoundationStyle ChooseStyle(Building building, double drop)
    {
        FoundationStyle style = building.Service?.Foundation ?? (building.Ward switch
        {
            WardType.Market or WardType.Merchant or WardType.Cathedral => FoundationStyle.Stone,
            WardType.Craftsmen => (Hash(building.Id) & 1) == 0 ? FoundationStyle.Stone : FoundationStyle.Wood,
            _ => FoundationStyle.Wood,
        });

        if (style == FoundationStyle.Wood && drop < PilingMinDropMeters)
        {
            return FoundationStyle.Stone;
        }

        return style == FoundationStyle.Wood && drop > PierMinDropMeters ? FoundationStyle.Pier : style;
    }

    internal static uint Hash(int id) => unchecked((uint)(id + 1) * 2654435761u) >> 8;
}
