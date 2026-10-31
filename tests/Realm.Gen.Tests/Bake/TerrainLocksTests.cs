using System;
using System.IO;
using Realm.Gen.Bake;
using Xunit;

namespace Realm.Gen.Tests.Bake;

public class TerrainLocksTests
{
    [Fact]
    public void Empty_ContainsNothing()
    {
        Assert.False(TerrainLocks.Empty.Contains(0, 0));
    }

    [Fact]
    public void WithLock_MakesItContained()
    {
        TerrainLocks locks = TerrainLocks.Empty.WithLock(-3, 2, "hand-edited");

        Assert.True(locks.Contains(-3, 2));
        Assert.False(locks.Contains(-3, 3));
        Assert.Equal("hand-edited", locks.Regions[0].Note);
    }

    [Fact]
    public void WithLock_OnAnAlreadyLockedRegion_ReplacesTheNoteRatherThanDuplicating()
    {
        TerrainLocks locks = TerrainLocks.Empty.WithLock(1, 1, "first").WithLock(1, 1, "second");

        Assert.Equal(1, locks.Regions.Count);
        Assert.Equal("second", locks.Regions[0].Note);
    }

    [Fact]
    public void WithoutLock_RemovesIt_AndIsANoOpWhenNotLocked()
    {
        TerrainLocks locks = TerrainLocks.Empty.WithLock(1, 1).WithoutLock(1, 1);

        Assert.False(locks.Contains(1, 1));
        Assert.Empty(locks.WithoutLock(5, 5).Regions);
    }

    [Fact]
    public void Json_RoundTrips()
    {
        TerrainLocks locks = TerrainLocks.Empty.WithLock(-1, 4, "edited 2026-10-30");

        TerrainLocks back = TerrainLocks.FromJson(locks.ToJson());

        Assert.True(back.Contains(-1, 4));
        Assert.Equal("edited 2026-10-30", back.Regions[0].Note);
    }

    [Fact]
    public void Load_WithNoFile_ReturnsEmpty()
    {
        string dir = Path.Combine(Path.GetTempPath(), "realm-locks-" + Guid.NewGuid().ToString("N"));

        Assert.Equal(TerrainLocks.Empty, TerrainLocks.Load(dir));
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsThroughTheFileSystem()
    {
        string dir = Path.Combine(Path.GetTempPath(), "realm-locks-" + Guid.NewGuid().ToString("N"));
        try
        {
            TerrainLocks.Empty.WithLock(2, -2, "note").Save(dir);

            TerrainLocks loaded = TerrainLocks.Load(dir);

            Assert.True(loaded.Contains(2, -2));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
