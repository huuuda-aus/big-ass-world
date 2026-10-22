using System;
using System.Collections.Generic;
using Realm.Core.Random;
using Realm.Sim.Navigation;

namespace Realm.Sim.Npcs;

/// <summary>
/// The characters of one settlement and their day: they stand about, walk along the graph to buildings, go inside, come out, and go home
/// to sleep. A walking character's position is a function of time (its route and when it set out), so nothing is stepped per frame; the
/// crowd only wakes a character when its next event is due, and catches up if time jumped.
/// </summary>
/// <remarks>
/// Time is in game minutes (see <c>WorldClock</c>); speed is metres per game minute. Determinism: each character draws from its own
/// <see cref="Rng"/> seeded by its plan; characters are processed in order. Thread-safety: not thread-safe; one owner.
/// </remarks>
public sealed class Crowd
{
    private const int MaxEventsPerCall = 12;
    private const double MinutesPerDay = 1440;
    private const double RetryMinutes = 20;
    private const double ShuffleMeters = 4; // a move shorter than this goes straight

    // How long a character dwells, in game minutes (a game minute is about a second of real time at the default day length).
    private const double LoiterMinMinutes = 25;
    private const double LoiterMaxMinutes = 120;
    private const double ErrandMinMinutes = 6;
    private const double ErrandMaxMinutes = 45;
    private const double VisitMinMinutes = 30;
    private const double VisitMaxMinutes = 180;
    private const double DoorstepMinMinutes = 2;
    private const double DoorstepMaxMinutes = 10;
    private const double ErrandChance = 0.15;
    private const double HomeChance = 0.25;

    private readonly NavGraph _graph;
    private readonly NavPathFinder _finder;
    private readonly double _metresPerMinute;
    private readonly Func<double, double, double, double, bool>? _lineClear;
    private readonly Agent[] _agents;
    private readonly List<int> _path = new();

    private enum Mode
    {
        Standing,
        Inside,
        Walking,
    }

    private sealed class Agent
    {
        public NpcPlan Plan = null!; // Safe: set at construction.
        public Rng Rng = null!; // Safe: set at construction.
        public Mode Mode;
        public double X;
        public double Z;
        public int Door = -1; // the door node the character is in or at
        public double NextEvent;
        public NavRoute? Route;
        public double RouteStart;
        public int Goal = -1; // door node it walks to (enter it on arrival); -1 to stand at the end
        public (double X, double Z) GoalSpot;
        public bool OnDoorstep; // standing in front of a door, not out in the open
    }

    /// <summary>Creates the crowd.</summary>
    /// <param name="graph">The settlement's walking graph.</param>
    /// <param name="plans">One plan per character.</param>
    /// <param name="nowMinutes">The current game time in minutes; the characters start already going about their day.</param>
    /// <param name="metresPerMinute">Walking speed in metres per game minute.</param>
    /// <param name="lineClear">Whether a straight walk between two points (x1, z1, x2, z2, in the graph's frame) stays on open ground; lets
    /// strollers cross a square directly instead of following the graph. When null only short shuffles go straight.</param>
    public Crowd(NavGraph graph, IReadOnlyList<NpcPlan> plans, double nowMinutes, double metresPerMinute, Func<double, double, double, double, bool>? lineClear = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(metresPerMinute);
        _graph = graph;
        _finder = new NavPathFinder(graph);
        _metresPerMinute = metresPerMinute;
        _lineClear = lineClear;
        _agents = new Agent[plans.Count];
        for (int i = 0; i < plans.Count; i++)
        {
            _agents[i] = Start(plans[i], nowMinutes);
        }
    }

    /// <summary>Number of characters.</summary>
    public int Count => _agents.Length;

    /// <summary>Lets every character whose next event is due act, catching up if time jumped.</summary>
    /// <param name="nowMinutes">The current game time in minutes.</param>
    public void Update(double nowMinutes)
    {
        foreach (Agent a in _agents)
        {
            for (int events = 0; events < MaxEventsPerCall && a.NextEvent <= nowMinutes; events++)
            {
                Act(a);
            }

            if (a.NextEvent <= nowMinutes)
            {
                a.NextEvent = nowMinutes + 1; // far behind (a long jump in time): carry on from here
            }
        }
    }

    /// <summary>Where a character is, and whether it can be seen (a character inside a building cannot).</summary>
    /// <param name="index">The character's index.</param>
    /// <param name="nowMinutes">The game time in minutes.</param>
    /// <param name="snapshot">Receives the position when the character is outside.</param>
    /// <returns><c>true</c> when the character is outside and visible.</returns>
    public bool TryGetSnapshot(int index, double nowMinutes, out NpcSnapshot snapshot)
    {
        Agent a = _agents[index];
        switch (a.Mode)
        {
            case Mode.Inside:
                snapshot = default;
                return false;
            case Mode.Walking when a.Route is not null:
                a.Route.PositionAt((nowMinutes - a.RouteStart) * _metresPerMinute, out double x, out double z);
                snapshot = new NpcSnapshot(a.Plan.Id, a.Plan.Role, x, z, true);
                return true;
            default:
                snapshot = new NpcSnapshot(a.Plan.Id, a.Plan.Role, a.X, a.Z, false);
                return true;
        }
    }

    /// <summary>The plan of a character.</summary>
    /// <param name="index">The character's index.</param>
    /// <returns>The plan.</returns>
    public NpcPlan PlanOf(int index) => _agents[index].Plan;

    /// <summary>Number of characters that are outside right now.</summary>
    /// <returns>The count of characters standing or walking in the open.</returns>
    public int CountOutside()
    {
        int n = 0;
        foreach (Agent a in _agents)
        {
            n += a.Mode == Mode.Inside ? 0 : 1;
        }

        return n;
    }

    // ---- behaviour ---------------------------------------------------------------------------------------------

    private Agent Start(NpcPlan plan, double now)
    {
        var a = new Agent { Plan = plan, Rng = new Rng(plan.Seed) };
        double minute = Mod(now, MinutesPerDay);
        switch (plan.Role)
        {
            case NpcRole.Guard:
                SetStanding(a, plan.Spots[0].X, plan.Spots[0].Z);
                a.NextEvent = double.PositiveInfinity;
                break;
            case NpcRole.Loiterer when !plan.GoesHomeAt(minute) && plan.Spots.Length > 0:
                (double X, double Z) spot = plan.Spots[a.Rng.NextInt(plan.Spots.Length)];
                SetStanding(a, spot.X, spot.Z);
                a.NextEvent = now - (a.Rng.NextDouble() * LoiterMaxMinutes); // already part-way through its stay
                break;
            default:
                // Starts inside a building (home when asleep), due to come out at a time in the recent past so the day is under way.
                a.Mode = Mode.Inside;
                a.Door = plan.GoesHomeAt(minute) || plan.Destinations.Length == 0 ? plan.HomeNode : plan.Destinations[a.Rng.NextInt(plan.Destinations.Length)];
                if (a.Door < 0)
                {
                    a.Door = plan.HomeNode;
                }

                a.NextEvent = now - (a.Rng.NextDouble() * VisitMaxMinutes * 0.5);
                break;
        }

        return a;
    }

    private void Act(Agent a)
    {
        double now = a.NextEvent;
        switch (a.Mode)
        {
            case Mode.Walking:
                Arrive(a, now);
                break;
            case Mode.Inside:
                Leave(a, now);
                break;
            default:
                Decide(a, now);
                break;
        }
    }

    // A walk has ended at its time: either enter the building at the goal or stand there.
    private void Arrive(Agent a, double now)
    {
        if (a.Goal >= 0)
        {
            a.Mode = Mode.Inside;
            a.Door = a.Goal;
            NavNode n = _graph.Nodes[a.Goal];
            a.X = n.X;
            a.Z = n.Z;
            a.NextEvent = BeforeBedtime(a, now, now + Pick(a, a.Plan.Role == NpcRole.Loiterer ? ErrandMinMinutes : VisitMinMinutes, a.Plan.Role == NpcRole.Loiterer ? ErrandMaxMinutes : VisitMaxMinutes));
            if (a.Plan.GoesHomeAt(Mod(now, MinutesPerDay)) && a.Goal == a.Plan.HomeNode)
            {
                a.NextEvent = now + MinutesUntilWake(a.Plan, Mod(now, MinutesPerDay));
            }
        }
        else
        {
            SetStanding(a, a.GoalSpot.X, a.GoalSpot.Z);
            a.NextEvent = BeforeBedtime(a, now, now + Pick(a, LoiterMinMinutes, LoiterMaxMinutes));
        }

        a.Route = null;
    }

    // A character comes out of a building and decides what to do next.
    private void Leave(Agent a, double now)
    {
        NavNode door = _graph.Nodes[a.Door];
        SetStanding(a, door.X, door.Z);
        a.OnDoorstep = true;
        double minute = Mod(now, MinutesPerDay);
        if (a.Plan.GoesHomeAt(minute) && a.Door == a.Plan.HomeNode)
        {
            // Still night at home: sleep on.
            a.Mode = Mode.Inside;
            a.NextEvent = now + Math.Max(1, MinutesUntilWake(a.Plan, minute));
            return;
        }

        // A short pause on the doorstep, then on to the next thing.
        a.NextEvent = BeforeBedtime(a, now, now + Pick(a, DoorstepMinMinutes, DoorstepMaxMinutes));
    }

    private void Decide(Agent a, double now)
    {
        double minute = Mod(now, MinutesPerDay);
        NpcPlan plan = a.Plan;
        if (plan.GoesHomeAt(minute) && plan.HomeNode >= 0)
        {
            if (a.OnDoorstep && a.Door == plan.HomeNode)
            {
                // Already at its own door: in for the night.
                a.Mode = Mode.Inside;
                a.NextEvent = now + Math.Max(1, MinutesUntilWake(plan, minute));
                return;
            }

            if (!WalkToDoor(a, plan.HomeNode, now))
            {
                a.NextEvent = now + RetryMinutes;
            }

            return;
        }

        switch (plan.Role)
        {
            case NpcRole.Loiterer:
                if (plan.Destinations.Length > 0 && a.Rng.NextDouble() < ErrandChance)
                {
                    if (WalkToDoor(a, plan.Destinations[a.Rng.NextInt(plan.Destinations.Length)], now))
                    {
                        return;
                    }
                }

                (double X, double Z) spot = plan.Spots[a.Rng.NextInt(plan.Spots.Length)];
                WalkToSpot(a, spot, now);
                break;
            case NpcRole.Walker:
                int target = a.Rng.NextDouble() < HomeChance || plan.Destinations.Length == 0
                    ? plan.HomeNode
                    : plan.Destinations[a.Rng.NextInt(plan.Destinations.Length)];
                if (target == a.Door || !WalkToDoor(a, target, now))
                {
                    a.NextEvent = now + Pick(a, DoorstepMinMinutes, DoorstepMaxMinutes);
                }

                break;
            default:
                a.NextEvent = double.PositiveInfinity;
                break;
        }
    }

    private bool WalkToDoor(Agent a, int doorNode, double now)
    {
        int start = a.OnDoorstep ? _graph.Nearest(a.X, a.Z) : _graph.Nearest(a.X, a.Z, NavNodeKind.Street);
        if (start < 0 || !_finder.TryFindPath(start, doorNode, _path, out _))
        {
            return false;
        }

        var points = new List<(double X, double Z)>(_path.Count + 1) { (a.X, a.Z) };
        foreach (int node in _path)
        {
            points.Add((_graph.Nodes[node].X, _graph.Nodes[node].Z));
        }

        BeginWalk(a, new NavRoute(points), now, doorNode, default);
        return true;
    }

    // From a spot to another: a short shuffle, or a walk the open-ground test allows, goes straight; anything else follows the graph (round the fountain, along the ring) from
    // the nearest street node to the one nearest the spot, so nobody walks through the fountain, a stall or a house.
    private void WalkToSpot(Agent a, (double X, double Z) spot, double now)
    {
        var points = new List<(double X, double Z)> { (a.X, a.Z) };
        double straight = Math.Sqrt(((spot.X - a.X) * (spot.X - a.X)) + ((spot.Z - a.Z) * (spot.Z - a.Z)));
        bool direct = !a.OnDoorstep && (straight <= ShuffleMeters || (_lineClear is not null && _lineClear(a.X, a.Z, spot.X, spot.Z)));
        if (!direct)
        {
            int start = a.OnDoorstep ? _graph.Nearest(a.X, a.Z) : _graph.Nearest(a.X, a.Z, NavNodeKind.Street);
            int end = _graph.Nearest(spot.X, spot.Z, NavNodeKind.Street);
            if (start < 0 || end < 0 || !_finder.TryFindPath(start, end, _path, out _))
            {
                a.NextEvent = now + RetryMinutes;
                return;
            }

            foreach (int node in _path)
            {
                points.Add((_graph.Nodes[node].X, _graph.Nodes[node].Z));
            }
        }

        points.Add(spot);
        BeginWalk(a, new NavRoute(points), now, -1, spot);
    }

    private void BeginWalk(Agent a, NavRoute route, double now, int goal, (double X, double Z) goalSpot)
    {
        a.Mode = Mode.Walking;
        a.Route = route;
        a.RouteStart = now;
        a.Goal = goal;
        a.GoalSpot = goalSpot;
        a.NextEvent = now + (Math.Max(route.LengthMeters, 0.01) / _metresPerMinute);
    }

    private static void SetStanding(Agent a, double x, double z)
    {
        a.Mode = Mode.Standing;
        a.X = x;
        a.Z = z;
        a.Route = null;
        a.Goal = -1;
        a.OnDoorstep = false;
    }

    // A stay never runs past the start of the evening: it is cut short so the character can start for home in time.
    private static double BeforeBedtime(Agent a, double now, double next)
    {
        double minute = Mod(now, MinutesPerDay);
        if (a.Plan.HomeNode < 0 || a.Plan.GoesHomeAt(minute))
        {
            return next;
        }

        double untilSleep = Mod(a.Plan.SleepMinute - NpcPlan.EveningMinutes - minute, MinutesPerDay);
        return untilSleep < next - now ? now + Math.Max(1, untilSleep) : next;
    }

    private static double Pick(Agent a, double min, double max) => min + (a.Rng.NextDouble() * (max - min));

    private static double MinutesUntilWake(NpcPlan plan, double minuteOfDay)
    {
        double wait = plan.WakeMinute - minuteOfDay;
        return wait > 0 ? wait : wait + MinutesPerDay;
    }

    private static double Mod(double value, double modulus)
    {
        double r = value % modulus;
        return r < 0 ? r + modulus : r;
    }
}
