using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Random;
using Realm.Sim.Entities;
using Realm.Sim.Time;
using Xunit;

namespace Realm.Sim.Tests;

public class SimulationTests
{
    private readonly record struct Position(int X, int Y);

    private sealed class RecordingSystem : ISystem
    {
        private readonly string _name;
        private readonly List<string> _log;

        public RecordingSystem(string name, List<string> log)
        {
            _name = name;
            _log = log;
        }

        public void Update(SimWorld world, SimClock clock) => _log.Add($"{_name}@{clock.Tick}");
    }

    // Moves every entity by a random step drawn from a per-tick, per-system Rng.
    private sealed class WanderSystem : ISystem
    {
        public void Update(SimWorld world, SimClock clock)
        {
            Rng rng = Rng.ForKey(world.Seed, "system", "wander", clock.Tick);
            foreach ((EntityId entity, Position p) in world.Query<Position>())
            {
                world.Set(entity, new Position(p.X + rng.NextInt(-1, 2), p.Y + rng.NextInt(-1, 2)));
            }
        }
    }

    private static Simulation Wandering(ulong seed)
    {
        var sim = new Simulation(seed);
        sim.AddSystem(new WanderSystem());
        for (int i = 0; i < 5; i++)
        {
            sim.WorldState.Set(sim.WorldState.CreateEntity(), new Position(0, 0));
        }

        return sim;
    }

    private static Position[] Positions(Simulation sim) =>
        sim.WorldState.Query<Position>().Select(r => r.Value).ToArray();

    [Fact]
    public void Tick_RunsSystemsInRegistrationOrder_ThenAdvancesClock()
    {
        var log = new List<string>();
        var sim = new Simulation(1);
        sim.AddSystem(new RecordingSystem("a", log));
        sim.AddSystem(new RecordingSystem("b", log));

        sim.Tick();
        sim.Tick();

        Assert.Equal(new[] { "a@0", "b@0", "a@1", "b@1" }, log);
        Assert.Equal(2, sim.Clock.Tick);
    }

    [Fact]
    public void Run_TicksTheGivenCount()
    {
        var sim = new Simulation(1, new TimeSettings(2));
        sim.Run(6);

        Assert.Equal(6, sim.Clock.Tick);
        Assert.Equal(3, sim.Clock.Time.TotalMinutes);
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Run(-1));
    }

    [Fact]
    public void AddSystem_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new Simulation(1).AddSystem(null!));
    }

    [Fact]
    public void SameSeed_SameTicks_GiveIdenticalWorlds()
    {
        Simulation a = Wandering(42);
        Simulation b = Wandering(42);

        a.Run(200);
        b.Run(200);

        Assert.Equal(Positions(a), Positions(b));
    }

    [Fact]
    public void RunInDifferentChunks_GivesTheSameResult()
    {
        Simulation oneGo = Wandering(7);
        Simulation chunks = Wandering(7);

        oneGo.Run(100);
        for (int i = 0; i < 10; i++)
        {
            chunks.Run(10);
        }

        Assert.Equal(Positions(oneGo), Positions(chunks));
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentWorlds()
    {
        Simulation a = Wandering(1);
        Simulation b = Wandering(2);

        a.Run(200);
        b.Run(200);

        Assert.NotEqual(Positions(a), Positions(b));
    }

    [Fact]
    public void WanderSystem_ActuallyMovesThings()
    {
        Simulation sim = Wandering(3);
        sim.Run(50);

        Assert.Contains(Positions(sim), p => p != new Position(0, 0));
    }
}
