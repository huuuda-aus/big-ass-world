using System;
using System.Linq;
using Realm.Sim.Entities;
using Xunit;

namespace Realm.Sim.Tests;

public class DenseStorageTests
{
    private readonly record struct Counter(int Value);

    private readonly record struct Marker(int Value);

    private static EntityId[] CreateMany(SimWorld world, int count)
    {
        var ids = new EntityId[count];
        for (int i = 0; i < count; i++)
        {
            ids[i] = world.CreateEntity();
        }

        return ids;
    }

    [Fact]
    public void Spans_AreParallel_AndInEntityOrder()
    {
        var world = new SimWorld(1);
        EntityId[] ids = CreateMany(world, 5);
        for (int i = 0; i < ids.Length; i++)
        {
            world.Set(ids[i], new Counter(i * 10));
        }

        ReadOnlySpan<EntityId> entities = world.EntitiesWith<Counter>();
        Span<Counter> components = world.ComponentsOf<Counter>();

        Assert.Equal(5, entities.Length);
        Assert.Equal(entities.Length, components.Length);
        for (int i = 0; i < entities.Length; i++)
        {
            Assert.Equal(ids[i], entities[i]);
            Assert.Equal(i * 10, components[i].Value);
        }
    }

    [Fact]
    public void WritingThroughTheSpan_ChangesTheComponentInPlace()
    {
        var world = new SimWorld(1);
        EntityId[] ids = CreateMany(world, 3);
        foreach (EntityId id in ids)
        {
            world.Set(id, new Counter(1));
        }

        Span<Counter> components = world.ComponentsOf<Counter>();
        for (int i = 0; i < components.Length; i++)
        {
            components[i] = new Counter(components[i].Value + 41);
        }

        foreach (EntityId id in ids)
        {
            Assert.True(world.TryGet(id, out Counter c));
            Assert.Equal(42, c.Value);
        }
    }

    [Fact]
    public void SetOnExistingComponent_DuringSpanIteration_IsSafeAndVisible()
    {
        var world = new SimWorld(1);
        EntityId[] ids = CreateMany(world, 4);
        foreach (EntityId id in ids)
        {
            world.Set(id, new Counter(0));
        }

        ReadOnlySpan<EntityId> entities = world.EntitiesWith<Counter>();
        Span<Counter> components = world.ComponentsOf<Counter>();
        for (int i = 0; i < entities.Length; i++)
        {
            world.Set(entities[i], new Counter(7));
        }

        Assert.All(components.ToArray(), c => Assert.Equal(7, c.Value));
    }

    [Fact]
    public void EmptyType_GivesEmptySpans()
    {
        var world = new SimWorld(1);

        Assert.True(world.EntitiesWith<Marker>().IsEmpty);
        Assert.True(world.ComponentsOf<Marker>().IsEmpty);
    }

    [Fact]
    public void ComponentsAddedOutOfOrder_StayInEntityOrder()
    {
        var world = new SimWorld(1);
        EntityId[] ids = CreateMany(world, 20);
        int[] order = { 15, 3, 19, 0, 8, 11, 1, 18, 5, 9, 2, 17, 7, 13, 4, 16, 6, 12, 10, 14 };
        foreach (int i in order)
        {
            world.Set(ids[i], new Counter(i));
        }

        Assert.Equal(ids, world.EntitiesWith<Counter>().ToArray());
        Assert.Equal(Enumerable.Range(0, 20), world.ComponentsOf<Counter>().ToArray().Select(c => c.Value));
        Assert.Equal(Enumerable.Range(0, 20), world.Query<Counter>().Select(r => r.Value.Value));
    }

    [Fact]
    public void GrowsPastInitialCapacity_WithoutLosingData()
    {
        var world = new SimWorld(1);
        EntityId[] ids = CreateMany(world, 1_000);
        foreach (EntityId id in ids)
        {
            world.Set(id, new Counter((int)id.Value));
        }

        Assert.Equal(1_000, world.EntitiesWith<Counter>().Length);
        foreach (EntityId id in ids)
        {
            Assert.True(world.TryGet(id, out Counter c));
            Assert.Equal((int)id.Value, c.Value);
        }
    }

    [Fact]
    public void Remove_FromFrontMiddleAndEnd_KeepsTheRestOrdered()
    {
        var world = new SimWorld(1);
        EntityId[] ids = CreateMany(world, 6);
        foreach (EntityId id in ids)
        {
            world.Set(id, new Counter((int)id.Value));
        }

        world.Remove<Counter>(ids[0]);
        world.Remove<Counter>(ids[3]);
        world.Remove<Counter>(ids[5]);
        world.Remove<Counter>(ids[3]);

        Assert.Equal(new[] { ids[1], ids[2], ids[4] }, world.EntitiesWith<Counter>().ToArray());
        Assert.Equal(
            new[] { (int)ids[1].Value, (int)ids[2].Value, (int)ids[4].Value },
            world.ComponentsOf<Counter>().ToArray().Select(c => c.Value));
        Assert.False(world.Has<Counter>(ids[3]));
    }

    [Fact]
    public void RemoveAll_LeavesEmptyStore_ThatCanBeRefilled()
    {
        var world = new SimWorld(1);
        EntityId[] ids = CreateMany(world, 3);
        foreach (EntityId id in ids)
        {
            world.Set(id, new Counter(1));
            world.Remove<Counter>(id);
        }

        Assert.True(world.EntitiesWith<Counter>().IsEmpty);
        world.Set(ids[1], new Counter(5));
        Assert.Equal(new[] { ids[1] }, world.EntitiesWith<Counter>().ToArray());
    }

    [Fact]
    public void DestroyedEntityInTheMiddle_IsGoneFromSpansAndAliveList()
    {
        var world = new SimWorld(1);
        EntityId[] ids = CreateMany(world, 5);
        foreach (EntityId id in ids)
        {
            world.Set(id, new Counter(1));
        }

        world.DestroyEntity(ids[2]);

        Assert.Equal(new[] { ids[0], ids[1], ids[3], ids[4] }, world.EntitiesWith<Counter>().ToArray());
        Assert.Equal(new[] { ids[0], ids[1], ids[3], ids[4] }, world.Entities.ToArray());
        Assert.False(world.IsAlive(ids[2]));
        Assert.True(world.IsAlive(ids[3]));
    }

    [Fact]
    public void IsAlive_IsCorrectForManyEntitiesWithGaps()
    {
        var world = new SimWorld(1);
        EntityId[] ids = CreateMany(world, 200);
        for (int i = 0; i < ids.Length; i += 3)
        {
            world.DestroyEntity(ids[i]);
        }

        for (int i = 0; i < ids.Length; i++)
        {
            Assert.Equal(i % 3 != 0, world.IsAlive(ids[i]));
        }

        Assert.False(world.IsAlive(new EntityId(ids[^1].Value + 1)));
        Assert.False(world.IsAlive(EntityId.None));
    }

    [Fact]
    public void SpanIteration_AllocatesNothing()
    {
        var world = new SimWorld(1);
        EntityId[] ids = CreateMany(world, 1_000);
        foreach (EntityId id in ids)
        {
            world.Set(id, new Counter(0));
        }

        Bump(world); // warm up the JIT so compilation allocations are not counted
        Bump(world);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < 100; round++)
        {
            Bump(world);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(102, world.ComponentsOf<Counter>()[0].Value);
    }

    private static void Bump(SimWorld world)
    {
        Span<Counter> components = world.ComponentsOf<Counter>();
        for (int i = 0; i < components.Length; i++)
        {
            components[i] = new Counter(components[i].Value + 1);
        }
    }
}
