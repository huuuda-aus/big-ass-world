using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Random;
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

    private const int SpotTries = 40;
    private const int NearHouses = 24;
    private const int NearServices = 6;
    private const double ServiceChance = 0.4;
    private const double MinutesPerDay = 1440;
    private const double GuardInwardMeters = 3;
    private const double GuardSideMeters = 2.5;
    private const double DefaultSquareRadius = 8;
    private const double SquareOuterShare = 1.6;
    private const double SquareOuterExtraMeters = 6;

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
                plans.Add(new NpcPlan(i, NpcRole.Loiterer, home, Destinations(nav.Graph, doors, services, home, new Vec2(0, 0), rng, settings), SquareSpots(city, ground, rng, settings), sleep, wake, own));
            }
            else
            {
                int home = homes[i % homes.Count].Node;
                plans.Add(new NpcPlan(i, NpcRole.Walker, home, Destinations(nav.Graph, doors, services, home, home >= 0 ? new Vec2(nav.Graph.Nodes[home].X, nav.Graph.Nodes[home].Z) : new Vec2(0, 0), rng, settings), Array.Empty<(double, double)>(), sleep, wake, own));
            }
        }

        return plans;
    }

    // Two guards on each side of every gate, just inside it.
    private static List<(double X, double Z)> GuardPosts(City city, OpenGround ground, CrowdSettings settings)
    {
        var posts = new List<(double X, double Z)>();
        foreach (Vec2 gate in city.Gates)
        {
            Vec2 inward = gate.Length < 1e-6 ? new Vec2(-1, 0) : (-gate).Normalized();
            Vec2 side = new(-inward.Y, inward.X);
            for (int k = 0; k < settings.GuardsPerGate; k++)
            {
                double offset = (k % 2 == 0 ? 1 : -1) * GuardSideMeters * (1 + (k / 2));
                Vec2 p = gate + (inward * GuardInwardMeters) + (side * offset);
                if (!ground.Blocked(p))
                {
                    posts.Add((p.X, p.Y));
                }
            }
        }

        return posts;
    }

    // Places in the open around the centrepiece; thicker toward its edge (exponential fall-off), never inside a stall or the fountain.
    private static (double X, double Z)[] SquareSpots(City city, OpenGround ground, Rng rng, CrowdSettings settings)
    {
        Vec2 centre = city.Landmark?.Center ?? new Vec2(0, 0);
        double inner = ground.LandmarkSolidRadius + settings.SquareInnerMeters;
        double outer = Math.Max(inner + DefaultSquareRadius, (city.Landmark?.Radius ?? 0) * SquareOuterShare) + SquareOuterExtraMeters;
        var spots = new List<(double X, double Z)>();
        for (int s = 0; s < settings.SpotsPerLoiterer; s++)
        {
            for (int attempt = 0; attempt < SpotTries; attempt++)
            {
                double r = inner - (Math.Log(1 - rng.NextDouble()) * settings.SquareSpreadMeters);
                double angle = rng.NextDouble() * 2 * Math.PI;
                Vec2 p = centre + new Vec2(r * Math.Cos(angle), r * Math.Sin(angle));
                if (r <= outer && !ground.Blocked(p))
                {
                    spots.Add((p.X, p.Y));
                    break;
                }
            }
        }

        if (spots.Count == 0)
        {
            spots.Add((centre.X + inner + 0.5, centre.Y));
        }

        return spots.ToArray();
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
}
