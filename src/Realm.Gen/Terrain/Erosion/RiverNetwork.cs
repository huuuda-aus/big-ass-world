using System.Collections.Generic;
using Realm.Gen.Terrain.Water;

namespace Realm.Gen.Terrain.Erosion;

/// <summary>The rivers and lakes of a world.</summary>
/// <remarks>Thread-safety: immutable.</remarks>
public sealed class RiverNetwork
{
    internal RiverNetwork(IReadOnlyList<River> rivers, IReadOnlyList<Lake> lakes, int workingSpacingMeters = 0, IReadOnlyList<RiverChannel>? channels = null)
    {
        Rivers = rivers;
        Lakes = lakes;
        WorkingSpacingMeters = workingSpacingMeters;
        Channels = channels ?? new List<RiverChannel>();
    }

    /// <summary>The water profile of each river (level, width and flow along a smoothed centre line); empty when channels were not cut.</summary>
    public IReadOnlyList<RiverChannel> Channels { get; }

    /// <summary>Spacing in metres of the grid the rivers and lakes were found on; each lake cell is a square of this width. 0 when unknown.</summary>
    public int WorkingSpacingMeters { get; }

    /// <summary>A network with nothing in it.</summary>
    public static RiverNetwork Empty { get; } = new(new List<River>(), new List<Lake>());

    /// <summary>Rivers ordered by the length of their flow path to the sea or world edge, longest first (ties by source position).</summary>
    public IReadOnlyList<River> Rivers { get; }

    /// <summary>Lakes in scan order.</summary>
    public IReadOnlyList<Lake> Lakes { get; }
}
