using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Random;
using Realm.Data.Content;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Settlements.Navigation;
using Realm.Sim.Navigation;
using Realm.Sim.Npcs;

namespace Realm.Gen.Population;

/// <summary>
/// Makes the characters of one settlement as plans: guards at the gates, loiterers who crowd the square around the fountain (the
/// closer to it, the thicker), and walkers who live in a house and visit a few other buildings, favouring the service buildings. The
/// characters are simple placeholders; the same plans will later drive real people.
/// </summary>
/// <remarks>Determinism: every random choice comes from <see cref="Rng"/> keyed by the world seed, the settlement and the character. Thread-safety: pure.</remarks>
public static class CrowdGenerator
{
    /// <summary>Generator identifier recorded with derived data.</summary>
    public const string GeneratorId = "gen.population.crowd";

    /// <summary>Generator version; bump when the plans change.</summary>
    public const int GeneratorVersion = 1;

    private const double AboutJitterMeters = 0.5;
    private const double AboutClearOfFountainMeters = 4;
    private const int NearHouses = 24;
    private const int NearServices = 6;
    private const double ServiceChance = 0.4;
    private const double MinutesPerDay = 1440;
    private const double GuardSideMeters = 1.2;
    private const double GuardRowMeters = 1.5;

    /// <summary>The number of characters a settlement of a tier gets.</summary>
    /// <param name="tier">The tier.</param>
    /// <param name="settings">The counts.</param>
    /// <returns>The count.</returns>
    public static int CountFor(Settlements.Placement.SettlementTier tier, CrowdSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return tier switch
        {
            Settlements.Placement.SettlementTier.City => settings.CityCount,
            Settlements.Placement.SettlementTier.Town => settings.TownCount,
            _ => settings.HamletCount,
        };
    }

    /// <summary>Generates the plans of a settlement's characters.</summary>
    /// <param name="city">The generated city.</param>
    /// <param name="nav">Its walking graph.</param>
    /// <param name="ground">Its open ground (spots are chosen on it).</param>
    /// <param name="settlementId">The settlement ID, part of every seed.</param>
    /// <param name="count">How many characters.</param>
    /// <param name="seed">World seed.</param>
    /// <param name="settings">Tuning; defaults when null.</param>
    /// <returns>The plans, indexed by character ID.</returns>
    public static IReadOnlyList<NpcPlan> Generate(City city, NavGraphResult nav, OpenGround ground, string settlementId, int count, ulong seed, CrowdSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(settlementId);
        settings ??= new CrowdSettings();
        var plans = new List<NpcPlan>(Math.Max(0, count));
        if (count <= 0)
        {
            return plans;
        }

        var unreachable = nav.UnreachableBuildings.ToHashSet();
        var doors = new List<(int Node, Building Building)>();
        foreach (Building b in city.AllBuildings)
        {
            int node = nav.Graph.DoorOf(b.Id);
            if (node >= 0 && !unreachable.Contains(b.Id))
            {
                doors.Add((node, b));
            }
        }

        List<(double X, double Z)> posts = GuardPosts(city, ground, settings);
        int guards = Math.Min(Math.Min(posts.Count, (int)Math.Floor(count * settings.GuardShare)), count);
        int loiterers = (int)Math.Round((count - guards) * settings.LoiterShare);
        if (doors.Count == 0)
        {
            loiterers = count - guards; // nowhere to live: everyone stands about the square
        }

        var master = Rng.ForKey(seed, "crowd", settlementId);
        NavGraph graph = nav.Graph;
        int[] aboutNodes = AboutCandidates(graph, ground);
        List<(int Node, Building Building)> homes = Shuffle(doors, master);
        var services = doors.Where(d => d.Building.Service is not null).Select(d => d.Node).ToList();

        for (int i = 0; i < count; i++)
        {
            var rng = Rng.ForKey(seed, "crowd", settlementId, i);
            ulong own = rng.NextUInt64();
            double sleep = Wrap(settings.BedtimeMinute + ((rng.NextDouble() * 2 - 1) * settings.SleepSpreadMinutes));
            double wake = Wrap(settings.WakeMinute + ((rng.NextDouble() * 2 - 1) * settings.SleepSpreadMinutes));
            if (i < guards)
            {
                plans.Add(new NpcPlan(i, NpcRole.Guard, -1, Array.Empty<int>(), new[] { posts[i] }, sleep, wake, own));
            }
            else if (i < guards + loiterers)
            {
                int home = doors.Count == 0 ? -1 : homes[i % homes.Count].Node;
                plans.Add(new NpcPlan(i, NpcRole.Loiterer, home, Destinations(nav.Graph, doors, services, home, new Vec2(0, 0), rng, settings), AboutSpots(graph, ground, aboutNodes, rng, settings), sleep, wake, own));
            }
            else
            {
                int home = homes[i % homes.Count].Node;
                plans.Add(new NpcPlan(i, NpcRole.Walker, home, Destinations(nav.Graph, doors, services, home, home >= 0 ? new Vec2(nav.Graph.Nodes[home].X, nav.Graph.Nodes[home].Z) : new Vec2(0, 0), rng, settings), Array.Empty<(double, double)>(), sleep, wake, own));
            }
        }

        return plans;
    }

    // Guards flank the opening of every gate, one on each side of the road, standing in the gate itself (the middle of the opening is
    // the only ground there that is certainly free); further guards stand a little way inside.
    private static List<(double X, double Z)> GuardPosts(City city, OpenGround ground, CrowdSettings settings)
    {
        var posts = new List<(double X, double Z)>();
        foreach (Vec2 gate in city.Gates)
        {
            Vec2 inward = -OpenGround.GateOutward(city, gate);
            Vec2 side = new(-inward.Y, inward.X);
            for (int k = 0; k < settings.GuardsPerGate; k++)
            {
                double lateral = (k % 2 == 0 ? 1 : -1) * GuardSideMeters;
                double depth = (k / 2) * GuardRowMeters;
                Vec2 p = gate + (inward * depth) + (side * lateral);
                if (!ground.Blocked(p))
                {
                    posts.Add((p.X, p.Y));
                }
            }
        }

        return posts;
    }

    // Places to stand about, spread evenly over the whole settlement: street and door-front nodes of the walking graph (so every place
    // can be walked to), a little off the node, away from the fountain's own ring.
    private static (double X, double Z)[] AboutSpots(NavGraph graph, OpenGround ground, int[] candidates, Rng rng, CrowdSettings settings)
    {
        if (candidates.Length == 0)
        {
            return new[] { (ground.LandmarkSolidRadius + 3, 0.0) };
        }

        var spots = new (double X, double Z)[settings.SpotsPerLoiterer];
        for (int s = 0; s < spots.Length; s++)
        {
            NavNode node = graph.Nodes[candidates[rng.NextInt(candidates.Length)]];
            var at = new Vec2(node.X, node.Z);
            var jittered = new Vec2(node.X + ((rng.NextDouble() * 2 - 1) * AboutJitterMeters), node.Z + ((rng.NextDouble() * 2 - 1) * AboutJitterMeters));
            Vec2 p = ground.Blocked(jittered) ? at : jittered;
            spots[s] = (p.X, p.Y);
        }

        return spots;
    }

    // The nodes a person may stand about at: streets and door fronts, outside the ring round the fountain.
    private static int[] AboutCandidates(NavGraph graph, OpenGround ground)
    {
        double clear = ground.LandmarkSolidRadius + AboutClearOfFountainMeters;
        return Enumerable.Range(0, graph.Nodes.Count)
            .Where(i => (graph.Nodes[i].Kind == NavNodeKind.Street || graph.Nodes[i].Kind == NavNodeKind.Door)
                && Math.Sqrt((graph.Nodes[i].X * graph.Nodes[i].X) + (graph.Nodes[i].Z * graph.Nodes[i].Z)) > clear
                && !ground.Blocked(new Vec2(graph.Nodes[i].X, graph.Nodes[i].Z)))
            .ToArray();
    }

    // A few buildings to visit, near a reference point (the square for a loiterer, the home for a walker): some of the nearest service
    // buildings and some of the nearest houses, so errands are short walks, not trips across the city.
    private static int[] Destinations(NavGraph graph, List<(int Node, Building Building)> doors, List<int> services, int home, Vec2 reference, Rng rng, CrowdSettings settings)
    {
        double Distance(int node) => Math.Sqrt(Math.Pow(graph.Nodes[node].X - reference.X, 2) + Math.Pow(graph.Nodes[node].Z - reference.Y, 2));
        int[] nearHouses = doors.Select(d => d.Node).Where(n => n != home).OrderBy(Distance).ThenBy(n => n).Take(NearHouses).ToArray();
        int[] nearServices = services.Where(n => n != home).OrderBy(Distance).ThenBy(n => n).Take(NearServices).ToArray();
        var chosen = new List<int>();
        for (int k = 0; k < settings.DestinationsPerWalker; k++)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                int[] pool = nearServices.Length > 0 && (nearHouses.Length == 0 || rng.NextDouble() < ServiceChance) ? nearServices : nearHouses;
                if (pool.Length == 0)
                {
                    break;
                }

                int node = pool[rng.NextInt(pool.Length)];
                if (!chosen.Contains(node))
                {
                    chosen.Add(node);
                    break;
                }
            }
        }

        return chosen.ToArray();
    }

    private static List<T> Shuffle<T>(IReadOnlyList<T> items, Rng rng)
    {
        var list = items.ToList();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    private static double Wrap(double minute) => ((minute % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;

    /// <summary>Generates the characters of a settlement with professions: each gets a trade from the shares of its tier, a workplace in
    /// the settlement, a timetable, a name, a walking speed and a lane to keep to.</summary>
    /// <param name="city">The generated city.</param>
    /// <param name="nav">Its walking graph.</param>
    /// <param name="ground">Its open ground.</param>
    /// <param name="settlementId">The settlement ID, part of every seed.</param>
    /// <param name="tier">The settlement's tier, which picks the shares of the professions.</param>
    /// <param name="count">How many characters.</param>
    /// <param name="seed">World seed.</param>
    /// <param name="professions">All professions.</param>
    /// <param name="settings">Tuning; defaults when null.</param>
    /// <returns>The plans, indexed by character ID.</returns>
    public static IReadOnlyList<NpcPlan> Generate(
        City city,
        NavGraphResult nav,
        OpenGround ground,
        string settlementId,
        Settlements.Placement.SettlementTier tier,
        int count,
        ulong seed,
        IReadOnlyCollection<ProfessionDefinition> professions,
        CrowdSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(settlementId);
        ArgumentNullException.ThrowIfNull(professions);
        settings ??= new CrowdSettings();
        var plans = new List<NpcPlan>(Math.Max(0, count));
        if (count <= 0)
        {
            return plans;
        }

        NavGraph graph = nav.Graph;
        var unreachable = nav.UnreachableBuildings.ToHashSet();
        var doors = new List<(int Node, Building Building)>();
        var byService = new SortedDictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (Building b in city.AllBuildings)
        {
            int node = graph.DoorOf(b.Id);
            if (node < 0 || unreachable.Contains(b.Id))
            {
                continue;
            }

            doors.Add((node, b));
            if (b.Service is { } service)
            {
                if (!byService.TryGetValue(service.Id, out List<int>? list))
                {
                    byService[service.Id] = list = new List<int>();
                }

                list.Add(node);
            }
        }

        int[] aboutNodes = AboutCandidates(graph, ground);
        var master = Rng.ForKey(seed, "crowd", settlementId);
        List<(int Node, Building Building)> homes = Shuffle(doors, master);
        var services = byService.Values.SelectMany(x => x).ToList();
        List<(double X, double Z)> posts = GuardPosts(city, ground, settings);
        List<(double X, double Z)> stalls = StallSpots(city, ground);
        int[] patrol = PatrolNodes(graph);
        int innNode = byService.TryGetValue("service.inn", out List<int>? inns) && inns.Count > 0 ? inns[0] : -1;
        var gateOutsides = Enumerable.Range(0, graph.Nodes.Count).Where(i => graph.Nodes[i].Kind == NavNodeKind.Outside).ToList();

        // How many of each profession: its share of the tier, limited by what the settlement has to offer; the rest are fillers.
        string tierWord = tier switch { Settlements.Placement.SettlementTier.City => "city", Settlements.Placement.SettlementTier.Town => "town", _ => "hamlet" };
        List<ProfessionDefinition> ordered = professions.OrderBy(p => p.Id.Value, StringComparer.Ordinal).ToList();
        ProfessionDefinition? filler = ordered.FirstOrDefault(p => p.Tags.Contains("filler")) ?? ordered.FirstOrDefault(p => p.WorkplaceKind == "none");
        var assignment = new List<ProfessionDefinition>();
        foreach (ProfessionDefinition p in ordered)
        {
            if (p == filler)
            {
                continue;
            }

            int n = (int)Math.Round(count * p.ShareFor(tierWord));
            n = p.WorkplaceKind switch
            {
                "service" => p.WorkplaceService is { } id && byService.ContainsKey(id.Value) ? n : 0,
                "gate_post" => Math.Min(n, posts.Count),
                "patrol" => patrol.Length > 0 ? n : 0,
                "fields" => gateOutsides.Count > 0 ? n : 0,
                _ => n,
            };
            for (int k = 0; k < n && assignment.Count < count; k++)
            {
                assignment.Add(p);
            }
        }

        while (assignment.Count < count)
        {
            assignment.Add(filler ?? ordered[0]);
        }

        // Shuffle who gets which trade, so the trades are spread over the ID range; the order is fixed by the seed.
        assignment = Shuffle(assignment, master);

        var serviceUse = new Dictionary<string, int>();
        int postUse = 0;
        int stallUse = 0;
        for (int i = 0; i < count; i++)
        {
            ProfessionDefinition prof = assignment[i];
            var rng = Rng.ForKey(seed, "crowd", settlementId, i);
            ulong own = rng.NextUInt64();
            string name = NpcNamer.Name(rng);
            int home = homes.Count == 0 ? -1 : homes[i % homes.Count].Node;
            (double X, double Z)[] spots = AboutSpots(graph, ground, aboutNodes, rng, settings);
            int workNode = -1;
            (double X, double Z)? workSpot = null;
            int[] patrolNodes = Array.Empty<int>();
            int fieldsNode = -1;
            switch (prof.WorkplaceKind)
            {
                case "service" when prof.WorkplaceService is { } sid && byService.TryGetValue(sid.Value, out List<int>? sites):
                    serviceUse.TryGetValue(sid.Value, out int used);
                    workNode = sites[used % sites.Count];
                    serviceUse[sid.Value] = used + 1;
                    break;
                case "stall":
                    workSpot = stalls.Count > 0 ? stalls[stallUse++ % stalls.Count] : spots[0];
                    break;
                case "gate_post":
                    workSpot = posts[postUse++ % posts.Count];
                    break;
                case "patrol":
                    patrolNodes = patrol;
                    break;
                case "fields":
                    Vec2 homePos = home >= 0 ? new Vec2(graph.Nodes[home].X, graph.Nodes[home].Z) : new Vec2(0, 0);
                    fieldsNode = gateOutsides.OrderBy(g => Vec2.Distance(new Vec2(graph.Nodes[g].X, graph.Nodes[g].Z), homePos)).ThenBy(g => g).FirstOrDefault(-1);
                    break;
            }

            bool watch = prof.Tags.Contains("watch");
            NpcRole role = watch ? NpcRole.Guard : prof.Tags.Contains("leisure") ? NpcRole.Loiterer : NpcRole.Walker;
            var schedule = prof.Day.Select(b => new ScheduleBlock(b.FromMinute, b.ToMinute, Enum.Parse<ScheduleActivity>(b.Activity, ignoreCase: true))).ToArray();
            plans.Add(new NpcPlan(
                i,
                role,
                home,
                Destinations(graph, doors, services, home, home >= 0 ? new Vec2(graph.Nodes[home].X, graph.Nodes[home].Z) : new Vec2(0, 0), rng, settings),
                spots,
                Wrap(settings.BedtimeMinute + ((rng.NextDouble() * 2 - 1) * settings.SleepSpreadMinutes)),
                Wrap(settings.WakeMinute + ((rng.NextDouble() * 2 - 1) * settings.SleepSpreadMinutes)),
                own)
            {
                Name = name,
                Profession = prof.Id.Value,
                Schedule = schedule,
                ScheduleShiftMinutes = (rng.NextDouble() * 2 - 1) * ScheduleSpreadMinutes,
                WorkNode = workNode,
                WorkSpot = workSpot,
                PatrolNodes = patrolNodes,
                InnNode = innNode,
                FieldsNode = fieldsNode,
                SpeedFactor = SpeedMin + (rng.NextDouble() * (SpeedMax - SpeedMin)),
                LaneOffsetMeters = LaneMin + (rng.NextDouble() * (LaneMax - LaneMin)),
                ShelterFromRain = !watch,
                Challenges = watch && prof.WorkplaceKind == "gate_post",
            });
        }

        return plans;
    }

    /// <summary>What to call the places a character goes, for an inspector: service buildings by name, other doors "a house", gates, the fields and the square.</summary>
    /// <param name="city">The generated city.</param>
    /// <param name="nav">Its walking graph.</param>
    /// <returns>A label for every node of the graph.</returns>
    public static IReadOnlyDictionary<int, string> NodeLabels(City city, NavGraphResult nav)
    {
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(nav);
        var labels = new Dictionary<int, string>();
        for (int i = 0; i < nav.Graph.Nodes.Count; i++)
        {
            labels[i] = nav.Graph.Nodes[i].Kind switch
            {
                NavNodeKind.Gate => "a gate",
                NavNodeKind.Outside => "the fields",
                NavNodeKind.BridgeEnd => "the bridge",
                _ => "the square",
            };
        }

        foreach (Building b in city.AllBuildings)
        {
            int node = nav.Graph.DoorOf(b.Id);
            if (node >= 0)
            {
                labels[node] = b.Service is { } s ? $"the {s.Name.ToLowerInvariant()}" : "a house";
            }
        }

        return labels;
    }

    // Standing places by the market stalls (a step toward the middle of the square from each stall); none for a square without stalls.
    private static List<(double X, double Z)> StallSpots(City city, OpenGround ground)
    {
        var spots = new List<(double X, double Z)>();
        if (city.Landmark is null)
        {
            return spots;
        }

        foreach (ConvexPoly stall in city.Landmark.Stalls)
        {
            Vec2 c = stall.Centroid;
            Vec2 toward = c.Length < 1e-6 ? new Vec2(1, 0) : (-c).Normalized();
            for (double reach = 1.0; reach <= 4.0; reach += 0.5)
            {
                Vec2 p = c + (toward * reach);
                if (!ground.Blocked(p))
                {
                    spots.Add((p.X, p.Y));
                    break;
                }
            }
        }

        return spots;
    }

    // The gates and the middle of the square in turn, round and round.
    private static int[] PatrolNodes(NavGraph graph)
    {
        int centre = graph.Nearest(0, 0, NavNodeKind.Street);
        var gates = Enumerable.Range(0, graph.Nodes.Count)
            .Where(i => graph.Nodes[i].Kind == NavNodeKind.Gate)
            .OrderBy(i => Math.Atan2(graph.Nodes[i].Z, graph.Nodes[i].X)).ThenBy(i => i)
            .ToList();
        var route = new List<int>();
        foreach (int g in gates)
        {
            route.Add(g);
            if (centre >= 0)
            {
                route.Add(centre);
            }
        }

        return route.ToArray();
    }

    private const double ScheduleSpreadMinutes = 25;
    private const double SpeedMin = 0.8;
    private const double SpeedMax = 1.25;
    private const double LaneMin = 0.3;
    private const double LaneMax = 0.6;
}
