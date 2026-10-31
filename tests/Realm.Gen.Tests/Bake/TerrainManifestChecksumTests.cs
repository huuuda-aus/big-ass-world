using System.Collections.Generic;
using Realm.Gen.Bake;
using Xunit;

namespace Realm.Gen.Tests.Bake;

public class TerrainManifestChecksumTests
{
    // Independent re-implementation of the FNV-1a fold, so this test does not just call the production code back.
    private static string ReferenceCombine(IReadOnlyList<ulong> checksums)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong combined = offset;
        foreach (ulong checksum in checksums)
        {
            for (int shift = 0; shift < 64; shift += 8)
            {
                combined = unchecked((combined ^ ((checksum >> shift) & 0xFFUL)) * prime);
            }
        }

        return combined.ToString("x16");
    }

    [Fact]
    public void Combine_MatchesAnIndependentImplementation()
    {
        var checksums = new ulong[] { 1, 2, 0xDEADBEEFUL, ulong.MaxValue, 0 };

        Assert.Equal(ReferenceCombine(checksums), TerrainManifestChecksum.Combine(checksums));
    }

    [Fact]
    public void Combine_IsDeterministic()
    {
        var checksums = new ulong[] { 7, 42, 1337 };

        Assert.Equal(TerrainManifestChecksum.Combine(checksums), TerrainManifestChecksum.Combine(checksums));
    }

    [Fact]
    public void Combine_OrderMatters()
    {
        Assert.NotEqual(
            TerrainManifestChecksum.Combine(new ulong[] { 1, 2 }),
            TerrainManifestChecksum.Combine(new ulong[] { 2, 1 }));
    }

    [Fact]
    public void Combine_ReturnsSixteenHexDigits()
    {
        string result = TerrainManifestChecksum.Combine(new ulong[] { 123 });

        Assert.Equal(16, result.Length);
    }
}
