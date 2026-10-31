using System;
using System.Collections.Generic;

namespace Realm.Gen.Bake;

/// <summary>
/// Combines per-tile checksums into the manifest's whole-world fingerprint (<see cref="TerrainManifest.Checksum"/>).
/// Shared by <see cref="TerrainBaker"/> (bakes every tile) and the terrain editor (rewrites one tile and must fold
/// its new checksum back in without re-reading every tile's full contents).
/// </summary>
/// <remarks>Determinism: a pure function of the checksums and their order. Thread-safety: stateless.</remarks>
public static class TerrainManifestChecksum
{
    private const ulong Fnv64Offset = 14695981039346656037UL;
    private const ulong Fnv64Prime = 1099511628211UL;

    /// <summary>Combines tile checksums, taken in region order (south to north, west to east, matching <see cref="TerrainBaker"/>).</summary>
    /// <param name="checksumsInRegionOrder">One checksum per region, in the same order every time the world is baked.</param>
    /// <returns>The combined fingerprint as 16 lowercase hexadecimal digits.</returns>
    public static string Combine(IReadOnlyList<ulong> checksumsInRegionOrder)
    {
        ArgumentNullException.ThrowIfNull(checksumsInRegionOrder);
        ulong combined = Fnv64Offset;
        foreach (ulong checksum in checksumsInRegionOrder)
        {
            for (int shift = 0; shift < 64; shift += 8)
            {
                combined = unchecked((combined ^ ((checksum >> shift) & 0xFFUL)) * Fnv64Prime);
            }
        }

        return combined.ToString("x16");
    }
}
