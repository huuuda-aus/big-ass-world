using System;
using System.Linq;
using Realm.Sim.Entities;
using Xunit;

namespace Realm.Sim.Tests;

public class SimWorldTests
{
    private readonly record struct Position(int X, int Y);

    private readonly record struct Health(int Value);

    [Fact]
    public void CreateEntity_GivesIncreasingValidIds()
    {
        var world = new SimWorld(1);
        EntityId a = world.CreateEntity();
        EntityId b = world.CreateEntity();

        Assert.True(a.IsValid);
        Assert.True(a < b);
        Assert.False(EntityId.None.IsValid);
        Assert.Equal(EntityId.None, default(EntityId));
        Assert.Equal(2, world.EntityCount);
    }

    [Fact]
    public void DestroyedIds_AreNeverReused()
    {
        var world = new SimWorld(1);
        EntityId first = world.CreateEntity();
        world.DestroyEntity(first);
        EntityId second = world.CreateEntity();

        Assert.NotEqual(first, second);
        Assert.False(world.IsAlive(first));
        Assert.True(world.IsAlive(second));
    }

    [Fact]
    public void DestroyEntity_ReportsWhetherItExisted()
    {
        var world = new SimWorld(1);
        EntityId e = world.CreateEntity();

        Assert.True(world.DestroyEntity(e));
        Assert.False(world.DestroyEntity(e));
        Assert.False(world.DestroyEntity(EntityId.None));
    }

    [Fact]
    public void Components_SetGetHasRemove()
    {
        var world = new SimWorld(1);
        EntityId e = world.CreateEntity();

        Assert.False(world.Has<Position>(e));
        world.Set(e, new Position(3, 4));

        Assert.True(world.Has<Position>(e));
        Assert.True(world.TryGet(e, out Position p));
        Assert.Equal(new Position(3, 4), p);

        world.Set(e, new Position(5, 6));
        world.TryGet(e, out p);
        Assert.Equal(new Position(5, 6), p);

        world.Remove<Position>(e);
        Assert.False(world.Has<Position>(e));
        Assert.False(world.TryGet(e, out Position _));
    }

    [Fact]
    public void ComponentTypes_AreIndependent()
    {
        var world = new SimWorld(1);
        EntityId e = world.CreateEntity();
        world.Set(e, new Position(1, 1));

        Assert.False(world.Has<Health>(e));
    }

    [Fact]
    public void Set_OnDeadOrUnknownEntity_Throws()
    {
        var world = new SimWorld(1);
        EntityId e = world.CreateEntity();
        world.DestroyEntity(e);

        Assert.Throws<InvalidOperationException>(() => world.Set(e, new Position(0, 0)));
        Assert.Throws<InvalidOperationException>(() => world.Set(EntityId.None, new Position(0, 0)));
    }

    [Fact]
    public void DestroyEntity_RemovesItsComponentsEverywhere()
    {
        var world = new SimWorld(1);
        EntityId e = world.CreateEntity();
        world.Set(e, new Position(1, 1));
        world.Set(e, new Health(10));

        world.DestroyEntity(e);

        Assert.Empty(world.Query<Position>());
        Assert.Empty(world.Query<Health>());
    }

    [Fact]
    public void Query_ReturnsEntityOrder_RegardlessOfInsertionOrder()
    {
        var world = new SimWorld(1);
        EntityId a = world.CreateEntity();
        EntityId b = world.CreateEntity();
        EntityId c = world.CreateEntity();

        world.Set(c, new Health(3));
        world.Set(a, new Health(1));
        world.Set(b, new Health(2));

        Assert.Equal(new[] { a, b, c }, world.Query<Health>().Select(r => r.Entity).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, world.Query<Health>().Select(r => r.Value.Value).ToArray());
    }

    [Fact]
    public void Query_IsASnapshot_SoTheWorldCanChangeWhileIterating()
    {
        var world = new SimWorld(1);
        EntityId a = world.CreateEntity();
        EntityId b = world.CreateEntity();
        world.Set(a, new Health(1));
        world.Set(b, new Health(2));

        int visited = 0;
        foreach ((EntityId entity, Health health) in world.Query<Health>())
        {
            visited++;
            world.DestroyEntity(b);
            if (world.IsAlive(entity))
            {
                world.Set(entity, new Health(health.Value * 10));
            }
        }

        // The snapshot still listed the destroyed entity, so it was visited; the live world did not keep it.
        Assert.Equal(2, visited);
        Assert.Equal(new[] { 10 }, world.Query<Health>().Select(r => r.Value.Value).ToArray());
    }

    [Fact]
    public void WritingToAnEntityDestroyedDuringIteration_Throws()
    {
        var world = new SimWorld(1);
        EntityId a = world.CreateEntity();
        EntityId b = world.CreateEntity();
        world.Set(a, new Health(1));
        world.Set(b, new Health(2));

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach ((EntityId entity, Health health) in world.Query<Health>())
            {
                world.DestroyEntity(b);
                world.Set(entity, new Health(health.Value + 1));
            }
        });
    }

    [Fact]
    public void Entities_EnumerateInIdOrder()
    {
        var world = new SimWorld(1);
        EntityId[] created = { world.CreateEntity(), world.CreateEntity(), world.CreateEntity() };
        world.DestroyEntity(created[1]);

        Assert.Equal(new[] { created[0], created[2] }, world.Entities.ToArray());
    }

    [Fact]
    public void Seed_IsKept()
    {
        Assert.Equal(99UL, new SimWorld(99).Seed);
    }
}
