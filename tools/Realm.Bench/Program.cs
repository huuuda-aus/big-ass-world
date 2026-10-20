using System;
using System.Diagnostics;
using Realm.Sim;
using Realm.Sim.Entities;
using Realm.Sim.Time;

// Simulation benchmark. Run in Release:
//   dotnet run -c Release --project tools/Realm.Bench
//   dotnet run -c Release --project tools/Realm.Bench -- --check
// --check exits 1 when the span-based scenario is over the sim tick budget or allocates per tick.

const double TickBudgetMs = 4.0; // CLAUDE.md section 4.8: sim tick <= 4 ms
const int CheckEntities = 10_000;
const int CheckSystems = 2;

bool check = args.Length > 0 && args[0] == "--check";

#if DEBUG
Console.Error.WriteLine("Warning: Debug build. Numbers are not meaningful; use -c Release.");
#endif

Console.WriteLine($"{"entities",9} {"systems",8} {"style",-18} {"ms/tick",9} {"B/tick",10} {"gen0/1000t",11}");

(double Ms, long Bytes) spanCheckResult = default;
foreach (int entities in new[] { 100, 1_000, 10_000 })
{
    foreach (int systems in new[] { 1, 2 })
    {
        Measure(entities, systems, "query+set (copy)", legacy: true);
        (double, long) span = Measure(entities, systems, "span in place", legacy: false);
        if (entities == CheckEntities && systems == CheckSystems)
        {
            spanCheckResult = span;
        }
    }
}

if (!check)
{
    return 0;
}

bool tooSlow = spanCheckResult.Ms > TickBudgetMs;
bool allocates = spanCheckResult.Bytes > 0;
Console.WriteLine();
Console.WriteLine(
    $"check: span, {CheckEntities} entities x {CheckSystems} systems = {spanCheckResult.Ms:F3} ms/tick " +
    $"(budget {TickBudgetMs} ms), {spanCheckResult.Bytes} B/tick (must be 0)");
if (tooSlow || allocates)
{
    Console.Error.WriteLine("FAILED: sim tick budget or allocation rule broken.");
    return 1;
}

Console.WriteLine("OK");
return 0;

static (double Ms, long Bytes) Measure(int entities, int systems, string style, bool legacy)
{
    var sim = new Simulation(1, new TimeSettings(20));
    for (int s = 0; s < systems; s++)
    {
        sim.AddSystem(s == 0
            ? legacy ? new QueryMove() : new SpanMove()
            : legacy ? new QueryRegen() : new SpanRegen());
    }

    for (int i = 0; i < entities; i++)
    {
        EntityId e = sim.WorldState.CreateEntity();
        sim.WorldState.Set(e, new Position(0, 0));
        sim.WorldState.Set(e, new Health(10));
    }

    sim.Run(50); // JIT warm-up
    int ticks = entities >= 10_000 ? 300 : 1_000;
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
    int gen0Before = GC.CollectionCount(0);
    var watch = Stopwatch.StartNew();
    sim.Run(ticks);
    watch.Stop();
    long bytes = (GC.GetAllocatedBytesForCurrentThread() - bytesBefore) / ticks;
    double gen0 = (GC.CollectionCount(0) - gen0Before) * 1000.0 / ticks;
    double ms = watch.Elapsed.TotalMilliseconds / ticks;

    Console.WriteLine($"{entities,9} {systems,8} {style,-18} {ms,9:F3} {bytes,10} {gen0,11:F1}");
    return (ms, bytes);
}

internal readonly record struct Position(int X, int Y);

internal readonly record struct Health(int Value);

// Old style: copy with Query, write back with Set (one tree/binary-search lookup per entity).
internal sealed class QueryMove : ISystem
{
    public void Update(SimWorld world, SimClock clock)
    {
        foreach ((EntityId entity, Position p) in world.Query<Position>())
        {
            world.Set(entity, new Position(p.X + 1, p.Y));
        }
    }
}

internal sealed class QueryRegen : ISystem
{
    public void Update(SimWorld world, SimClock clock)
    {
        foreach ((EntityId entity, Health h) in world.Query<Health>())
        {
            world.Set(entity, new Health(h.Value + 1));
        }
    }
}

// New style: write straight into the component storage.
internal sealed class SpanMove : ISystem
{
    public void Update(SimWorld world, SimClock clock)
    {
        Span<Position> positions = world.ComponentsOf<Position>();
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i] = new Position(positions[i].X + 1, positions[i].Y);
        }
    }
}

internal sealed class SpanRegen : ISystem
{
    public void Update(SimWorld world, SimClock clock)
    {
        Span<Health> health = world.ComponentsOf<Health>();
        for (int i = 0; i < health.Length; i++)
        {
            health[i] = new Health(health[i].Value + 1);
        }
    }
}
