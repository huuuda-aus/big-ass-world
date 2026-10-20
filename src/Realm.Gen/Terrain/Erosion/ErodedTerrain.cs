using System;

namespace Realm.Gen.Terrain.Erosion;

/// <summary>The result of the erosion step: carved heights plus the rivers and lakes found in them.</summary>
/// <remarks>Determinism: see <see cref="Checksum"/>. Thread-safety: immutable.</remarks>
public sealed class ErodedTerrain
{
    private const ulong Fnv64Offset = 14695981039346656037UL;
    private const ulong Fnv64Prime = 1099511628211UL;

    internal ErodedTerrain(HeightField field, RiverNetwork network, double maxCarveDepthMeters)
    {
        Field = field;
        Network = network;
        MaxCarveDepthMeters = maxCarveDepthMeters;
    }

    /// <summary>The terrain after hydraulic and thermal erosion.</summary>
    public HeightField Field { get; }

    /// <summary>The rivers and lakes.</summary>
    public RiverNetwork Network { get; }

    /// <summary>The deepest hydraulic carve, in metres, on the working grid (before thermal relaxation).</summary>
    public double MaxCarveDepthMeters { get; }

    /// <summary>
    /// A 64-bit fingerprint of the erosion generator version, the height field, every river point and every
    /// lake, for golden tests. Equal results give equal checksums on every machine.
    /// </summary>
    /// <returns>The FNV-1a checksum.</returns>
    public ulong Checksum()
    {
        ulong hash = Fnv64Offset;
        hash = Mix(hash, (ulong)ErosionGenerator.GeneratorVersion);
        hash = Mix(hash, Field.Checksum());
        foreach (River river in Network.Rivers)
        {
            hash = Mix(hash, (ulong)river.Index);
            hash = Mix(hash, (ulong)river.Mouth);
            hash = Mix(hash, (ulong)(river.JoinsRiver ?? -1));
            foreach (RiverPoint p in river.Points)
            {
                hash = Mix(hash, Bits(p.X));
                hash = Mix(hash, Bits(p.Z));
                hash = Mix(hash, Bits(p.Height));
                hash = Mix(hash, Bits(p.FlowKm2));
            }
        }

        foreach (Lake lake in Network.Lakes)
        {
            hash = Mix(hash, Bits(lake.SurfaceHeight));
            hash = Mix(hash, (ulong)lake.Cells.Count);
            hash = Mix(hash, Bits(lake.CentreX));
            hash = Mix(hash, Bits(lake.CentreZ));
        }

        return hash;
    }

    private static ulong Bits(double value) => unchecked((ulong)BitConverter.DoubleToInt64Bits(value));

    private static ulong Mix(ulong hash, ulong value)
    {
        for (int shift = 0; shift < 64; shift += 8)
        {
            hash = unchecked((hash ^ ((value >> shift) & 0xFFUL)) * Fnv64Prime);
        }

        return hash;
    }
}
