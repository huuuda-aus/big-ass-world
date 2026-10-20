using System;
using System.Collections.Generic;
using Realm.Core.Random;

namespace Realm.Gen.Props;

/// <summary>
/// Decides which houses and trees stand beside each 100 m segment of the scale-reference strip: one house and three
/// trees per segment, never overlapping and never on the tape itself.
/// </summary>
/// <remarks>Determinism: the same seed and segment index always give the same list, independent of other segments. Thread-safety: pure.</remarks>
public static class StripLayout
{
    /// <summary>Length of one segment in metres.</summary>
    public const float SegmentMeters = 100f;

    /// <summary>Trees per segment.</summary>
    public const int TreesPerSegment = 3;

    /// <summary>Distance of a house's centre from the tape.</summary>
    public const float HouseSideMeters = 9f;

    /// <summary>Closest a tree trunk may be to the tape.</summary>
    public const float TreeMinSideMeters = 5f;

    /// <summary>Farthest a tree trunk may be from the tape.</summary>
    public const float TreeMaxSideMeters = 9f;

    /// <summary>Smallest distance between a tree and a house (centre to centre, along the strip) when on the same side.</summary>
    public const float TreeHouseClearanceMeters = 9f;

    /// <summary>Smallest distance between two trees along the strip.</summary>
    public const float TreeSpacingMeters = 6f;

    private const float EdgeMarginMeters = 6f;
    private const float HeightStepMeters = 0.5f;
    private const int Attempts = 50;

    /// <summary>Lays out one segment.</summary>
    /// <param name="seed">World seed.</param>
    /// <param name="segment">Segment index from the start of the strip (0 is the first 100 m).</param>
    /// <returns>One house followed by three trees.</returns>
    public static IReadOnlyList<StripPlacement> ForSegment(ulong seed, int segment)
    {
        Rng rng = Rng.ForKey(seed, "reference-strip", segment);
        var result = new List<StripPlacement>(1 + TreesPerSegment);

        StripPropKind house = rng.NextBool() ? StripPropKind.Cottage : StripPropKind.TownHouse;
        float houseSide = rng.NextBool() ? HouseSideMeters : -HouseSideMeters;
        float houseAlong = Range(rng, EdgeMarginMeters + 5, SegmentMeters - EdgeMarginMeters - 5);
        result.Add(new StripPlacement(house, houseAlong, houseSide, 0));

        for (int i = 0; i < TreesPerSegment; i++)
        {
            StripPlacement tree = default;
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                StripPropKind kind = (StripPropKind)((int)StripPropKind.Pine + rng.NextInt(3));
                float height = TreeModels.MinHeightMeters + (rng.NextInt((int)((TreeModels.MaxHeightMeters - TreeModels.MinHeightMeters) / HeightStepMeters) + 1) * HeightStepMeters);
                float side = Range(rng, TreeMinSideMeters, TreeMaxSideMeters) * (rng.NextBool() ? 1 : -1);
                float along = Range(rng, EdgeMarginMeters, SegmentMeters - EdgeMarginMeters);
                tree = new StripPlacement(kind, along, side, height);
                if (Fits(result, tree))
                {
                    break;
                }
            }

            result.Add(tree);
        }

        return result;
    }

    private static bool Fits(List<StripPlacement> placed, StripPlacement tree)
    {
        foreach (StripPlacement other in placed)
        {
            bool house = other.Kind is StripPropKind.Cottage or StripPropKind.TownHouse;
            bool sameSide = Math.Sign(other.SideMeters) == Math.Sign(tree.SideMeters);
            float needed = house ? TreeHouseClearanceMeters : TreeSpacingMeters;
            if ((sameSide || !house) && Math.Abs(other.AlongMeters - tree.AlongMeters) < needed)
            {
                return false;
            }
        }

        return true;
    }

    private static float Range(Rng rng, float min, float max) => min + ((float)rng.NextDouble() * (max - min));
}
