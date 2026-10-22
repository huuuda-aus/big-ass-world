using System.Collections.Generic;

namespace Realm.Gen.Terrain.Water;

/// <summary>The water profile of one river: a smoothed centre line with a level, falling from the source to the mouth.</summary>
/// <remarks>Determinism: pure data. Thread-safety: immutable.</remarks>
public sealed class RiverChannel
{
    /// <summary>Creates a channel.</summary>
    /// <param name="riverIndex">Index of the river in <see cref="Realm.Gen.Terrain.Erosion.RiverNetwork.Rivers"/>.</param>
    /// <param name="points">Profile points from the source to the mouth.</param>
    public RiverChannel(int riverIndex, IReadOnlyList<ChannelPoint> points)
    {
        RiverIndex = riverIndex;
        Points = points;
    }

    /// <summary>Index of the river this channel belongs to.</summary>
    public int RiverIndex { get; }

    /// <summary>Profile points from the source to the mouth.</summary>
    public IReadOnlyList<ChannelPoint> Points { get; }
}
