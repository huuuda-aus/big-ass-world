using System;
using System.Collections.Generic;
using Realm.Core.Random;
using Realm.Sim.Navigation;

namespace Realm.Sim.Npcs;

/// <summary>
/// The characters of one settlement and their day: they follow their profession's timetable, standing about, walking along the graph to
/// buildings, going inside, coming out, taking shelter from rain and going home to sleep. A walking character's position is a function of
/// time (its route and when it set out), so nothing is stepped per frame; the crowd only wakes a character when its next event is due,
/// and catches up if time jumped.
/// </summary>
/// <remarks>
/// Time is in game minutes (see <c>WorldClock</c>); speed is metres per game minute. A plan without a schedule follows the simple
/// behaviour of its role. Determinism: each character draws from its own <see cref="Rng"/> seeded by its plan; characters are processed in
/// order. Thread-safety: not thread-safe; one owner.
/// </remarks>
public sealed class Crowd
{
    private const int MaxEventsPerCall = 12;
    private const double MinutesPerDay = 1440;
    private const double RetryMinutes = 20;
    private const double ShuffleMeters = 4; // a move shorter than this goes straight
    private const double CornerMeters = 1.6; // corners of a walk are rounded this far either side
    private const double EaseStrength = 0.3; // how much slower than the average a walk is at its very start and end
    private const double EaseMinMeters = 6;
    private const double LaneTaperMeters = 2.5; // the sideways offset fades out over this distance at either end of a walk

    // How long a character dwells, in game minutes (a game minute is about a second of real time at the default day length).
    private const double LoiterMinMinutes = 25;
    private const double LoiterMaxMinutes = 120;
    private const double ErrandMinMinutes = 30;
    private const double ErrandMaxMinutes = 120;
    private const double CallChance = 0.3; // chance that someone out and about calls on a house nearby instead of moving on
    private const double StartStayMinMinutes = 20;
    private const double StartStayMaxMinutes = 200;
    private const double InsideAtStart = 0.5; // share of the people who start the game indoors
    private const double VisitMinMinutes = 30;
    private const double VisitMaxMinutes = 180;
    private const double DoorstepMinMinutes = 2;
    private const double DoorstepMaxMinutes = 10;
    private const double WorkSpellMinMinutes = 70;
    private const double WorkSpellMaxMinutes = 150;
    private const double InnMinMinutes = 60;
    private const double InnMaxMinutes = 150;
    private const double PatrolPauseMinMinutes = 2;
    private const double PatrolPauseMaxMinutes = 6;
    private const double ErrandChance = 0.15;
    private const double HomeChance = 0.25;

    // The world and the player.
    private const double ShelterRain = 0.35;
    private const double ClearRain = 0.15;
    private const double ShelterStaggerMinutes = 6;
    private const double ShelterStayMinutes = 20;
    private const double LingerMinMinutes = 10;
    private const double LingerMaxMinutes = 50;
    private const double HurryPerRain = 0.3;
    private const double NoticeMeters = 7;
    private const double ChallengeMeters = 5;
    private const double StepAsideMeters = 2.5;
    private const double StepAsideDistance = 0.9;

    // Keeping people apart (presentation only: the simulation does not change).
    private const double PersonalSpaceMeters = 0.8;
    private const double SeparationRate = 14; // per second: how fast people ease out of each other's way
    private const double MaxOffsetMeters = 2.0;
    private const double HeadOnCosine = -0.3; // headings further apart than about 107 degrees are walking toward each other
    private const double HeadOnPush = 0.8;

    private readonly NavGraph _graph;
    private readonly NavPathFinder _finder;
    private readonly double _metresPerMinute;
    private readonly Func<double, double, double, double, bool>? _lineClear;
    private readonly Func<double, double, double, double, (double X, double Z)>? _constrain;
    private readonly Agent[] _agents;
    private readonly List<int> _path = new();
    private readonly NpcSnapshot[] _raw;
    private readonly int[] _rawIndex;
    private readonly double[] _pushX;
    private readonly double[] _pushZ;
    private CrowdContext _context;

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
        public int Door = -1; // the node the character is in or at
        public double NextEvent;
        public NavRoute? Route;
        public double RouteStart;
        public double SpeedMul = 1;
        public int Goal = -1; // node it walks to (enter it on arrival); -1 to stand at the end
        public (double X, double Z) GoalSpot;
        public bool OnNode; // standing on a graph node (a doorstep), not out in the open
        public NpcPurpose Purpose;
        public double PurposeEnd; // game minutes at which the current block of the day ends
        public int PatrolIndex;
        public bool Sheltering;
        public double ClearedAt = double.NaN; // when the rain stopped for someone sheltering (they wait a little longer before coming out)
        public double OffsetX; // how far the character is currently pushed aside by others (presentation only)
        public double OffsetZ;
    }

    /// <summary>Creates the crowd.</summary>
    /// <param name="graph">The settlement's walking graph.</param>
    /// <param name="plans">One plan per character.</param>
    /// <param name="nowMinutes">The current game time in minutes; the characters start already going about their day.</param>
    /// <param name="metresPerMinute">Walking speed in metres per game minute.</param>
    /// <param name="lineClear">Whether a straight walk between two points (x1, z1, x2, z2, in the graph's frame) stays on open ground; lets
    /// strollers cross a square directly instead of following the graph. When null only short shuffles go straight.</param>
    /// <param name="constrain">Keeps a body out of buildings and the like: given a position known to be fine (x, z) and the position wanted
    /// (x, z), returns the nearest position that is clear; null leaves positions as they are.</param>
    public Crowd(
        NavGraph graph,
        IReadOnlyList<NpcPlan> plans,
        double nowMinutes,
        double metresPerMinute,
        Func<double, double, double, double, bool>? lineClear = null,
        Func<double, double, double, double, (double X, double Z)>? constrain = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(metresPerMinute);
        _graph = graph;
        _finder = new NavPathFinder(graph);
        _metresPerMinute = metresPerMinute;
        _lineClear = lineClear;
        _constrain = constrain;
        _agents = new Agent[plans.Count];
        _raw = new NpcSnapshot[plans.Count];
        _rawIndex = new int[plans.Count];
        _pushX = new double[plans.Count];
        _pushZ = new double[plans.Count];
        for (int i = 0; i < plans.Count; i++)
        {
            _agents[i] = Start(plans[i], nowMinutes);
        }
    }

    /// <summary>Number of characters.</summary>
    public int Count => _agents.Length;

    /// <summary>Lets every character whose next event is due act, catching up if time jumped.</summary>
    /// <param name="nowMinutes">The current game time in minutes.</param>
    public void Update(double nowMinutes) => Update(nowMinutes, default);

    /// <summary>Lets every character whose next event is due act, with what they can sense of the world this frame.</summary>
    /// <param name="nowMinutes">The current game time in minutes.</param>
    /// <param name="context">The rain and the player.</param>
    public void Update(double nowMinutes, in CrowdContext context)
    {
        _context = context;
        bool raining = context.Rain >= ShelterRain;
        for (int i = 0; i < _agents.Length; i++)
        {
            Agent a = _agents[i];

            // When the rain sets in, those standing in the open who mind it start looking for a roof, a few at a time.
            if (raining && a.Mode == Mode.Standing && !a.Sheltering && NeedsShelter(a))
            {
                a.Sheltering = true;
                a.NextEvent = Math.Min(a.NextEvent, nowMinutes + (a.Rng.NextDouble() * ShelterStaggerMinutes));
            }

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
        if (a.Mode == Mode.Inside)
        {
            snapshot = default;
            return false;
        }

        double x = a.X;
        double z = a.Z;
        double lineX = x; // the walk's own line, before any sideways offset: always a place where the way is open
        double lineZ = z;
        double heading = a.Plan.Role == NpcRole.Guard && a.Plan.Spots.Length > 0 ? FacingFor(a) : StandingHeading(a);
        bool walking = false;
        if (a.Mode == Mode.Walking && a.Route is not null)
        {
            double length = a.Route.LengthMeters;
            double progress = Math.Clamp((nowMinutes - a.RouteStart) * _metresPerMinute * a.SpeedMul * a.Plan.SpeedFactor / Math.Max(length, 1e-6), 0, 1);
            walking = progress < 1;
            double distance = Eased(progress, length) * length;
            a.Route.PositionAt(distance, out x, out z);
            heading = a.Route.HeadingAt(distance);
            lineX = x;
            lineZ = z;

            // Keep to the right-hand side of the way, except at the very ends of a walk (the door, the spot).
            double taper = Math.Min(1, Math.Min(distance, length - distance) / LaneTaperMeters);
            double offset = a.Plan.LaneOffsetMeters * Math.Max(0, taper);
            double fx = -Math.Sin(heading);
            double fz = -Math.Cos(heading);
            x += -fz * offset;
            z += fx * offset;
        }

        bool noticing = false;
        bool alert = false;
        if (_context.PlayerPresent)
        {
            double dx = _context.PlayerX - x;
            double dz = _context.PlayerZ - z;
            double d = Math.Sqrt((dx * dx) + (dz * dz));
            if (a.Plan.Challenges && d <= ChallengeMeters)
            {
                alert = true;
                noticing = true;
            }
            else if (d <= NoticeMeters && (!walking || a.Plan.Id % 3 == 0))
            {
                noticing = true; // looks at the player (a walker only glances, and not everybody does)
            }

            if (noticing)
            {
                heading = Math.Atan2(-dx, -dz);
            }

            // Passing close, a walker steps aside, away from the player.
            if (walking && d < StepAsideMeters && d > 1e-6)
            {
                double push = StepAsideDistance * (1 - (d / StepAsideMeters));
                x -= dx / d * push;
                z -= dz / d * push;
            }
        }

        // The body never overlaps a house, a stall or the fountain, whatever pushed it there (lane, cut corner, a step aside).
        if (_constrain is not null)
        {
            (x, z) = _constrain(lineX, lineZ, x, z);
        }

        snapshot = new NpcSnapshot(a.Plan.Id, a.Plan.Role, x, z, walking, heading, noticing, alert);
        return true;
    }

    /// <summary>
    /// Collects the characters that can be seen and keeps them apart: people closer than personal space ease away from each other, so
    /// nobody walks through or stands inside anybody. The push is a smoothed offset on what is drawn; the simulation itself does not change,
    /// and nobody is pushed off open ground.
    /// </summary>
    /// <param name="nowMinutes">The game time in minutes.</param>
    /// <param name="deltaSeconds">Real seconds since the last call, for the pace of the easing.</param>
    /// <param name="output">Receives the visible characters; cleared first.</param>
    public void CollectVisible(double nowMinutes, double deltaSeconds, List<NpcSnapshot> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();
        int n = 0;
        for (int i = 0; i < _agents.Length; i++)
        {
            if (TryGetSnapshot(i, nowMinutes, out NpcSnapshot snap))
            {
                _raw[n] = snap;
                _rawIndex[n] = i;
                _pushX[n] = 0;
                _pushZ[n] = 0;
                n++;
            }
            else
            {
                _agents[i].OffsetX = 0;
                _agents[i].OffsetZ = 0;
            }
        }

        // Pairs closer than personal space push each other apart, measured with the offsets they already have.
        double minSquared = PersonalSpaceMeters * PersonalSpaceMeters;
        for (int a = 0; a < n; a++)
        {
            Agent agentA = _agents[_rawIndex[a]];
            double ax = _raw[a].X + agentA.OffsetX;
            double az = _raw[a].Z + agentA.OffsetZ;
            for (int b = a + 1; b < n; b++)
            {
                Agent agentB = _agents[_rawIndex[b]];
                double dx = ax - (_raw[b].X + agentB.OffsetX);
                double dz = az - (_raw[b].Z + agentB.OffsetZ);
                double d2 = (dx * dx) + (dz * dz);
                if (d2 >= minSquared)
                {
                    continue;
                }

                double d = Math.Sqrt(d2);
                double norm = d; // dividing by it turns (dx, dz) into a unit direction
                if (d < 1e-6)
                {
                    // On the same spot: part along a direction fixed by the two characters.
                    double angle = (((_raw[a].Id * 73856093) ^ (_raw[b].Id * 19349663)) & 0x3ff) / 1024.0 * 2 * Math.PI;
                    dx = Math.Cos(angle);
                    dz = Math.Sin(angle);
                    d = 0;
                    norm = 1;
                }

                // Two people walking toward each other each step to their own right, as people do; any other pair eases straight apart.
                double headingA = _raw[a].Heading;
                double headingB = _raw[b].Heading;
                bool headOn = _raw[a].Walking && _raw[b].Walking && Math.Cos(headingA - headingB) < HeadOnCosine;
                if (headOn)
                {
                    double press = (PersonalSpaceMeters - d) * HeadOnPush;
                    _pushX[a] += Math.Cos(headingA) * press;
                    _pushZ[a] += -Math.Sin(headingA) * press;
                    _pushX[b] += Math.Cos(headingB) * press;
                    _pushZ[b] += -Math.Sin(headingB) * press;
                    continue;
                }

                double push = (PersonalSpaceMeters - d) / 2 / norm;
                _pushX[a] += dx * push;
                _pushZ[a] += dz * push;
                _pushX[b] -= dx * push;
                _pushZ[b] -= dz * push;
            }
        }

        double relax = Math.Clamp(1 - Math.Exp(-SeparationRate * Math.Max(0, deltaSeconds)), 0, 1);
        for (int a = 0; a < n; a++)
        {
            Agent agent = _agents[_rawIndex[a]];
            bool crowded = _pushX[a] != 0 || _pushZ[a] != 0;
            // Pushed while crowded; when there is room again the offset eases back to nothing.
            double ox = crowded ? agent.OffsetX + (_pushX[a] * Math.Max(relax, 0.2)) : agent.OffsetX * (1 - (relax * 0.5));
            double oz = crowded ? agent.OffsetZ + (_pushZ[a] * Math.Max(relax, 0.2)) : agent.OffsetZ * (1 - (relax * 0.5));
            double length = Math.Sqrt((ox * ox) + (oz * oz));
            if (length > MaxOffsetMeters)
            {
                ox *= MaxOffsetMeters / length;
                oz *= MaxOffsetMeters / length;
            }

            // Never push anybody into a house, a stall or the fountain.
            if (length > 0.02 && _constrain is not null)
            {
                (double cx, double cz) = _constrain(_raw[a].X, _raw[a].Z, _raw[a].X + ox, _raw[a].Z + oz);
                ox = cx - _raw[a].X;
                oz = cz - _raw[a].Z;
            }

            agent.OffsetX = ox;
            agent.OffsetZ = oz;
            NpcSnapshot s = _raw[a];
            output.Add(s with { X = s.X + ox, Z = s.Z + oz });
        }
    }

    /// <summary>What a character is doing and why.</summary>
    /// <param name="index">The character's index.</param>
    /// <returns>The status.</returns>
    public NpcStatus Describe(int index)
    {
        Agent a = _agents[index];
        NpcState state = a.Mode switch
        {
            Mode.Inside => NpcState.Inside,
            Mode.Walking => NpcState.Walking,
            _ => NpcState.Standing,
        };
        int target = a.Mode == Mode.Walking ? a.Goal : a.Mode == Mode.Inside ? a.Door : -1;
        return new NpcStatus(state, a.Sheltering && a.Mode != Mode.Standing ? NpcPurpose.Shelter : a.Purpose, target, a.NextEvent);
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

    /// <summary>Counts the characters that are inside a building, by the node of its door.</summary>
    /// <param name="perNode">Receives the count for every graph node (cleared first); must have one entry per node of the graph.</param>
    public void CountInside(int[] perNode)
    {
        ArgumentNullException.ThrowIfNull(perNode);
        Array.Clear(perNode);
        foreach (Agent a in _agents)
        {
            if (a.Mode == Mode.Inside && a.Door >= 0)
            {
                perNode[a.Door]++;
            }
        }
    }

    // ---- start -------------------------------------------------------------------------------------------------

    private Agent Start(NpcPlan plan, double now)
    {
        var a = new Agent { Plan = plan, Rng = new Rng(plan.Seed) };
        double minute = Mod(now, MinutesPerDay);
        if (plan.Schedule is not null)
        {
            ScheduleActivity scheduled = plan.ScheduledAt(minute, out _);
            a.Door = scheduled == ScheduleActivity.Work && plan.WorkNode >= 0 ? plan.WorkNode : plan.HomeNode >= 0 ? plan.HomeNode : plan.WorkNode;
            SetStandingAtDoor(a);
            if (a.Door >= 0 && a.Rng.NextDouble() < InsideAtStart)
            {
                // Half the people start indoors, for a good while yet.
                a.Mode = Mode.Inside;
                a.Purpose = scheduled == ScheduleActivity.Work ? NpcPurpose.Work : NpcPurpose.Home;
                a.NextEvent = now + Pick(a, StartStayMinMinutes, StartStayMaxMinutes);
            }
            else
            {
                // The rest are on their doorsteps, about to set off: the day is already under way.
                a.Mode = Mode.Standing;
                a.NextEvent = now - (a.Rng.NextDouble() * 20);
            }

            return a;
        }

        switch (plan.Role)
        {
            case NpcRole.Guard:
                SetStanding(a, plan.Spots[0].X, plan.Spots[0].Z);
                a.NextEvent = double.PositiveInfinity;
                break;
            case NpcRole.Loiterer when !plan.GoesHomeAt(minute) && plan.Spots.Length > 0 && plan.HomeNode >= 0 && new Rng(plan.Seed ^ 0x5bd1e995UL).NextDouble() >= InsideAtStart:
                (double X, double Z) spot = plan.Spots[a.Rng.NextInt(plan.Spots.Length)];
                SetStanding(a, spot.X, spot.Z);
                a.NextEvent = now - (a.Rng.NextDouble() * LoiterMaxMinutes); // already part-way through its stay
                a.Purpose = NpcPurpose.About;
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

    private void SetStandingAtDoor(Agent a)
    {
        NavNode door = _graph.Nodes[Math.Max(0, a.Door)];
        a.X = door.X;
        a.Z = door.Z;
        a.OnNode = true;
        a.Route = null;
        a.Goal = -1;
    }

    // ---- events ------------------------------------------------------------------------------------------------

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
        a.Route = null;
        if (a.Goal >= 0)
        {
            a.Mode = Mode.Inside;
            a.Door = a.Goal;
            NavNode n = _graph.Nodes[a.Goal];
            a.X = n.X;
            a.Z = n.Z;
            a.OnNode = true;
            a.NextEvent = InsideUntil(a, now);
            return;
        }

        SetStanding(a, a.GoalSpot.X, a.GoalSpot.Z);
        if (a.Purpose == NpcPurpose.Shelter || a.Purpose == NpcPurpose.Patrol)
        {
            a.NextEvent = now; // the next thing at once (a roof when it rains, the next stop of the patrol; the pause was part of the walk)
            return;
        }

        a.NextEvent = a.Plan.Schedule is null
            ? BeforeBedtime(a, now, now + Pick(a, LoiterMinMinutes, LoiterMaxMinutes))
            : StandUntil(a, now);
    }

    // How long a character stays inside the building it has just reached, by what it came for.
    private double InsideUntil(Agent a, double now)
    {
        NpcPlan plan = a.Plan;
        if (a.Purpose == NpcPurpose.Shelter)
        {
            return now + ShelterStayMinutes;
        }

        if (plan.Schedule is null)
        {
            double stay = Pick(a, plan.Role == NpcRole.Loiterer ? ErrandMinMinutes : VisitMinMinutes, plan.Role == NpcRole.Loiterer ? ErrandMaxMinutes : VisitMaxMinutes);
            double until = BeforeBedtime(a, now, now + stay);
            if (plan.GoesHomeAt(Mod(now, MinutesPerDay)) && a.Goal == plan.HomeNode)
            {
                until = now + MinutesUntilWake(plan, Mod(now, MinutesPerDay));
            }

            return until;
        }

        double end = Math.Max(a.PurposeEnd, now + 1);
        return a.Purpose switch
        {
            NpcPurpose.Work => Math.Min(end, now + Pick(a, WorkSpellMinMinutes, WorkSpellMaxMinutes)),
            NpcPurpose.Inn => Math.Min(end, now + Pick(a, InnMinMinutes, InnMaxMinutes)),
            NpcPurpose.Errand => Math.Min(end, now + Pick(a, ErrandMinMinutes, ErrandMaxMinutes)),
            NpcPurpose.Fields => end,
            _ => end, // at home: until the block ends
        };
    }

    // A character comes out of a building and decides what to do next.
    private void Leave(Agent a, double now)
    {
        NavNode door = _graph.Nodes[a.Door];
        SetStanding(a, door.X, door.Z);
        a.OnNode = true;
        double minute = Mod(now, MinutesPerDay);

        if (a.Sheltering)
        {
            if (_context.Rain >= ClearRain)
            {
                // Still raining: stay under the roof a good while longer.
                a.ClearedAt = double.NaN;
                a.Mode = Mode.Inside;
                a.NextEvent = now + ShelterStayMinutes;
                return;
            }

            if (double.IsNaN(a.ClearedAt))
            {
                // It has stopped: wait a little longer to be sure before coming out.
                a.ClearedAt = now;
                a.Mode = Mode.Inside;
                a.NextEvent = now + Pick(a, LingerMinMinutes, LingerMaxMinutes);
                return;
            }
        }

        a.Sheltering = false;
        a.ClearedAt = double.NaN;
        if (a.Plan.Schedule is null && a.Plan.GoesHomeAt(minute) && a.Door == a.Plan.HomeNode)
        {
            // Still night at home: sleep on.
            a.Mode = Mode.Inside;
            a.NextEvent = now + Math.Max(1, MinutesUntilWake(a.Plan, minute));
            return;
        }

        // A short pause on the doorstep, then on to the next thing.
        a.NextEvent = a.Plan.Schedule is null
            ? BeforeBedtime(a, now, now + Pick(a, DoorstepMinMinutes, DoorstepMaxMinutes))
            : now + Pick(a, DoorstepMinMinutes, DoorstepMaxMinutes);
    }

    private void Decide(Agent a, double now)
    {
        if (a.Sheltering && _context.Rain >= ClearRain)
        {
            if (!WalkToNearestDoor(a, now))
            {
                a.NextEvent = now + RetryMinutes;
            }

            return;
        }

        a.Sheltering = false;
        if (a.Plan.Schedule is not null)
        {
            DecideScheduled(a, now);
        }
        else
        {
            DecideSimple(a, now);
        }
    }

    // ---- the day by timetable ----------------------------------------------------------------------------------

    private void DecideScheduled(Agent a, double now)
    {
        NpcPlan plan = a.Plan;
        ScheduleActivity activity = plan.ScheduledAt(Mod(now, MinutesPerDay), out double untilChange);
        a.PurposeEnd = now + untilChange;
        switch (activity)
        {
            case ScheduleActivity.Work when plan.WorkNode >= 0:
                GoInside(a, plan.WorkNode, NpcPurpose.Work, now);
                break;
            case ScheduleActivity.Work when plan.WorkSpot is { } spot:
                GoStand(a, spot, NpcPurpose.Work, now);
                break;
            case ScheduleActivity.Inn when plan.InnNode >= 0:
                GoInside(a, plan.InnNode, NpcPurpose.Inn, now);
                break;
            case ScheduleActivity.Errand when plan.Destinations.Length > 0:
                GoInside(a, plan.Destinations[a.Rng.NextInt(plan.Destinations.Length)], NpcPurpose.Errand, now);
                break;
            case ScheduleActivity.Patrol when plan.PatrolNodes.Length > 0:
                Patrol(a, now);
                break;
            case ScheduleActivity.Fields when plan.FieldsNode >= 0:
                GoInside(a, plan.FieldsNode, NpcPurpose.Fields, now);
                break;
            case ScheduleActivity.Home when plan.HomeNode >= 0:
                GoInside(a, plan.HomeNode, NpcPurpose.Home, now);
                break;
            default:
                Loiter(a, now); // out and about, or nothing else to do
                break;
        }
    }

    // Walk to a door and go in (or go straight in when already at it).
    private void GoInside(Agent a, int doorNode, NpcPurpose purpose, double now)
    {
        a.Purpose = purpose;
        if (a.OnNode && a.Door == doorNode && a.Mode == Mode.Standing)
        {
            a.Mode = Mode.Inside;
            a.Goal = doorNode;
            a.NextEvent = InsideUntil(a, now);
            return;
        }

        if (!WalkToDoor(a, doorNode, now))
        {
            a.NextEvent = now + RetryMinutes;
        }
    }

    // Walk to a place in the open and stand there.
    private void GoStand(Agent a, (double X, double Z) spot, NpcPurpose purpose, double now)
    {
        a.Purpose = purpose;
        double distance = Math.Sqrt(((spot.X - a.X) * (spot.X - a.X)) + ((spot.Z - a.Z) * (spot.Z - a.Z)));
        if (distance < 0.5 && !a.OnNode)
        {
            a.NextEvent = StandUntil(a, now);
            return;
        }

        WalkToSpot(a, spot, now);
    }

    private double StandUntil(Agent a, double now)
    {
        double end = Math.Max(a.PurposeEnd, now + 1);
        return a.Purpose == NpcPurpose.Work || a.Purpose == NpcPurpose.Patrol ? end : Math.Min(end, now + Pick(a, LoiterMinMinutes, LoiterMaxMinutes));
    }

    private void Loiter(Agent a, double now)
    {
        a.Purpose = NpcPurpose.About;
        if (a.Plan.Destinations.Length > 0 && a.Rng.NextDouble() < CallChance)
        {
            // Calls on a house nearby and stays a good while.
            GoInside(a, a.Plan.Destinations[a.Rng.NextInt(a.Plan.Destinations.Length)], NpcPurpose.Errand, now);
            return;
        }

        if (a.Plan.Spots.Length == 0)
        {
            a.NextEvent = Math.Max(a.PurposeEnd, now + 1);
            return;
        }

        (double X, double Z) spot = a.Plan.Spots[a.Rng.NextInt(a.Plan.Spots.Length)];
        WalkToSpot(a, spot, now);
    }

    // A round of the patrol nodes, pausing at each.
    private void Patrol(Agent a, double now)
    {
        a.Purpose = NpcPurpose.Patrol;
        int node = a.Plan.PatrolNodes[a.PatrolIndex % a.Plan.PatrolNodes.Length];
        a.PatrolIndex++;
        NavNode n = _graph.Nodes[node];
        if (!a.OnNode && Math.Abs(a.X - n.X) < 0.5 && Math.Abs(a.Z - n.Z) < 0.5)
        {
            a.NextEvent = now + Pick(a, PatrolPauseMinMinutes, PatrolPauseMaxMinutes);
            return;
        }

        int start = _graph.Nearest(a.X, a.Z);
        if (start < 0 || !_finder.TryFindPath(start, node, _path, out _))
        {
            a.NextEvent = now + RetryMinutes;
            return;
        }

        var points = new List<(double X, double Z)>(_path.Count + 1) { (a.X, a.Z) };
        foreach (int p in _path)
        {
            points.Add((_graph.Nodes[p].X, _graph.Nodes[p].Z));
        }

        // Stand where the patrol stops (outside, on the node), then move on.
        BeginWalk(a, NavRoute.Smooth(points, CornerMeters), now, -1, (n.X, n.Z));
        a.NextEvent += Pick(a, PatrolPauseMinMinutes, PatrolPauseMaxMinutes);
        a.Goal = -1;
    }

    // ---- the simple behaviour of a role (plans without a timetable) --------------------------------------------

    private void DecideSimple(Agent a, double now)
    {
        double minute = Mod(now, MinutesPerDay);
        NpcPlan plan = a.Plan;
        if (plan.GoesHomeAt(minute) && plan.HomeNode >= 0)
        {
            a.Purpose = NpcPurpose.Home;
            if (a.OnNode && a.Door == plan.HomeNode)
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
                    a.Purpose = NpcPurpose.Errand;
                    if (WalkToDoor(a, plan.Destinations[a.Rng.NextInt(plan.Destinations.Length)], now))
                    {
                        return;
                    }
                }

                a.Purpose = NpcPurpose.About;
                (double X, double Z) spot = plan.Spots[a.Rng.NextInt(plan.Spots.Length)];
                WalkToSpot(a, spot, now);
                break;
            case NpcRole.Walker:
                int target = a.Rng.NextDouble() < HomeChance || plan.Destinations.Length == 0
                    ? plan.HomeNode
                    : plan.Destinations[a.Rng.NextInt(plan.Destinations.Length)];
                a.Purpose = target == plan.HomeNode ? NpcPurpose.Home : NpcPurpose.Errand;
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

    // ---- walking -----------------------------------------------------------------------------------------------

    private bool WalkToNearestDoor(Agent a, double now)
    {
        int door = _graph.Nearest(a.X, a.Z, NavNodeKind.Door);
        if (door < 0)
        {
            return false;
        }

        a.Purpose = NpcPurpose.Shelter;
        return WalkToDoor(a, door, now);
    }

    private bool WalkToDoor(Agent a, int doorNode, double now)
    {
        int start = _graph.Nearest(a.X, a.Z);
        if (start < 0 || !_finder.TryFindPath(start, doorNode, _path, out _))
        {
            return false;
        }

        var points = new List<(double X, double Z)>(_path.Count + 1) { (a.X, a.Z) };
        foreach (int node in _path)
        {
            points.Add((_graph.Nodes[node].X, _graph.Nodes[node].Z));
        }

        BeginWalk(a, NavRoute.Smooth(points, CornerMeters), now, doorNode, default);
        return true;
    }

    // From a spot to another: a short shuffle, or a walk the open-ground test allows, goes straight; anything else follows the graph
    // (round the fountain, along the ring) from the nearest street node to the one nearest the spot, so nobody walks through the
    // fountain, a stall or a house.
    private void WalkToSpot(Agent a, (double X, double Z) spot, double now)
    {
        var points = new List<(double X, double Z)> { (a.X, a.Z) };
        double straight = Math.Sqrt(((spot.X - a.X) * (spot.X - a.X)) + ((spot.Z - a.Z) * (spot.Z - a.Z)));
        bool direct = !a.OnNode && (straight <= ShuffleMeters || (_lineClear is not null && _lineClear(a.X, a.Z, spot.X, spot.Z)));
        if (!direct)
        {
            int start = _graph.Nearest(a.X, a.Z);
            int end = _graph.Nearest(spot.X, spot.Z);
            if (start < 0 || end < 0 || !_finder.TryFindPath(start, end, _path, out _))
            {
                a.NextEvent = now + RetryMinutes;
                return;
            }

            foreach (int node in _path)
            {
                points.Add((_graph.Nodes[node].X, _graph.Nodes[node].Z));
            }

            // The last leg, from the end of the path to the spot, must be open ground too; if not, stand at the end of the path.
            (double X, double Z) last = points[^1];
            if (_lineClear is not null && !_lineClear(last.X, last.Z, spot.X, spot.Z))
            {
                spot = last;
            }
        }

        points.Add(spot);
        BeginWalk(a, NavRoute.Smooth(points, CornerMeters), now, -1, spot);
    }

    private void BeginWalk(Agent a, NavRoute route, double now, int goal, (double X, double Z) goalSpot)
    {
        a.Mode = Mode.Walking;
        a.OnNode = false;
        a.Route = route;
        a.RouteStart = now;
        a.Goal = goal;
        a.GoalSpot = goalSpot;
        a.SpeedMul = 1 + (HurryPerRain * Math.Clamp(_context.Rain, 0, 1));
        a.NextEvent = now + (Math.Max(route.LengthMeters, 0.01) / (_metresPerMinute * a.SpeedMul * a.Plan.SpeedFactor));
    }

    private static void SetStanding(Agent a, double x, double z)
    {
        a.Mode = Mode.Standing;
        a.X = x;
        a.Z = z;
        a.Route = null;
        a.Goal = -1;
        a.OnNode = false;
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    // Whether a character standing in the open minds the rain: not the watch at their posts, not those with nothing outdoors to do.
    private static bool NeedsShelter(Agent a) =>
        a.Plan.ShelterFromRain && a.Plan.Role != NpcRole.Guard && a.Plan.HomeNode >= 0 && a.Purpose != NpcPurpose.Patrol;

    // A walk starts and ends a little slower than its average, so people do not set off and stop like machines.
    private static double Eased(double progress, double length) =>
        length < EaseMinMeters ? progress : progress - (EaseStrength / (2 * Math.PI) * Math.Sin(2 * Math.PI * progress));

    private double StandingHeading(Agent a)
    {
        // A character standing in the open faces a fixed direction of its own.
        return ((a.Plan.Seed % 628) / 100.0) - Math.PI;
    }

    private double FacingFor(Agent a)
    {
        // A guard faces away from the middle of the settlement (outward), toward whatever comes.
        return Math.Atan2(-a.X, -a.Z);
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
