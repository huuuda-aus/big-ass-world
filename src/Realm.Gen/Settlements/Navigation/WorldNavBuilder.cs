using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Validation;
using Realm.Gen.Settlements.Placement;
using Realm.Gen.Settlements.Roads;
using Realm.Sim.Navigation;

namespace Realm.Gen.Settlements.Navigation;

/// <summary>
/// Joins the walking graphs of the settlements with the roads into one graph of the world, so a character can walk from a door in one
/// settlement to a door in another. Each settlement graph is moved into world coordinates; each road becomes a chain of nodes (its bridges
/// become bridge edges) whose ends join the outside nodes beyond the gates; roads that meet or cross join each other.
/// </summary>
/// <remarks>Determinism: pure; fixed iteration orders. Thread-safety: pure.</remarks>
public static class WorldNavBuilder
{
    private const double EndJoinMeters = 250;
    private const double JunctionMeters = 10;
    private const double CellMeters = 32;

    /// <summary>Builds the world graph.</summary>
    /// <param name="settlements">The settlements.</param>
    /// <param name="locals">Each settlement's own graph, in the same order, in the plan frame.</param>
    /// <param name="roads">The roads between the settlements.</param>
    /// <returns>The world graph and what the roads failed to join.</returns>
    /// <exception cref="ArgumentException">The two lists differ in length.</exception>
    public static WorldNavResult Build(IReadOnlyList<Settlement> settlements, IReadOnlyList<NavGraphResult> locals, IReadOnlyList<Road> roads)
    {
        ArgumentNullException.ThrowIfNull(settlements);
        ArgumentNullException.ThrowIfNull(locals);
        ArgumentNullException.ThrowIfNull(roads);
        if (settlements.Count != locals.Count)
        {
            throw new ArgumentException("There must be one graph per settlement.", nameof(locals));
        }

        var nodes = new List<NavNode>();
        var edges = new List<NavEdge>();
        var placed = new List<WorldNavSettlement>();
        var byId = new Dictionary<string, int>();
        for (int s = 0; s < settlements.Count; s++)
        {
            Settlement settlement = settlements[s];
            NavGraph local = locals[s].Graph;
            int first = nodes.Count;
            foreach (NavNode n in local.Nodes)
            {
                (double x, double z) = settlement.ToWorld(new Geometry.Vec2(n.X, n.Z));
                nodes.Add(n with { X = x, Z = z });
            }

            foreach (NavEdge e in local.Edges)
            {
                edges.Add(e with { A = e.A + first, B = e.B + first });
            }

            placed.Add(new WorldNavSettlement(settlement.Id, first, local.Nodes.Count));
            byId[settlement.Id] = s;
        }

        var roadNodes = new List<(int Node, int Road)>();
        for (int r = 0; r < roads.Count; r++)
        {
            Road road = roads[r];
            if (road.Points.Count < 2)
            {
                continue;
            }

            int firstNode = nodes.Count;
            foreach (RoadPoint p in road.Points)
            {
                nodes.Add(new NavNode(p.X, p.Z, NavNodeKind.Road));
                roadNodes.Add((nodes.Count - 1, r));
            }

            for (int i = 1; i < road.Points.Count; i++)
            {
                bool bridge = road.Bridges.Any(b => i - 1 >= b.FirstPoint && i <= b.LastPoint);
                edges.Add(new NavEdge(firstNode + i - 1, firstNode + i, Distance(nodes[firstNode + i - 1], nodes[firstNode + i]), bridge));
            }

            JoinEnd(road.From, firstNode, nodes, edges, placed, byId);
            JoinEnd(road.To, firstNode + road.Points.Count - 1, nodes, edges, placed, byId);
        }

        JoinJunctions(roadNodes, roads, nodes, edges);

        var graph = new NavGraph(nodes, edges);
        var notJoined = new List<string>();
        if (placed.Count > 0 && roads.Count > 0)
        {
            int home = placed[0].FirstNode;
            foreach (WorldNavSettlement s in placed.Skip(1))
            {
                if (s.NodeCount == 0 || !graph.AreConnected(s.FirstNode, home))
                {
                    notJoined.Add(s.Id);
                }
            }
        }

        return new WorldNavResult(graph, placed, notJoined);
    }

    /// <summary>Reports the problems of a built world graph.</summary>
    /// <param name="result">The build result.</param>
    /// <param name="report">Receives NAV003 for each settlement the roads do not reach.</param>
    public static void Validate(WorldNavResult result, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(report);
        foreach (string id in result.NotJoined)
        {
            report.Warning(NavCodes.SettlementNotJoined, id, $"'{id}' is not joined to the first settlement by the road network on the walking graph.", "Check that a road reaches one of its gates.");
        }
    }

    // Joins a road's end to the nearest outside node of its settlement (a gate node if the gate has no outside node).
    private static void JoinEnd(string settlementId, int roadNode, List<NavNode> nodes, List<NavEdge> edges, List<WorldNavSettlement> placed, Dictionary<string, int> byId)
    {
        if (!byId.TryGetValue(settlementId, out int s))
        {
            return;
        }

        WorldNavSettlement home = placed[s];
        int best = -1;
        double bestD = EndJoinMeters;
        foreach (NavNodeKind kind in new[] { NavNodeKind.Outside, NavNodeKind.Gate })
        {
            for (int i = home.FirstNode; i < home.FirstNode + home.NodeCount; i++)
            {
                if (nodes[i].Kind != kind)
                {
                    continue;
                }

                double d = Distance(nodes[i], nodes[roadNode]);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            if (best >= 0)
            {
                break;
            }
        }

        if (best >= 0)
        {
            edges.Add(new NavEdge(roadNode, best, bestD));
        }
    }

    // Roads that meet or cross share a junction: nodes of different roads close together are linked (not on a bridge deck).
    private static void JoinJunctions(List<(int Node, int Road)> roadNodes, IReadOnlyList<Road> roads, List<NavNode> nodes, List<NavEdge> edges)
    {
        var cells = new Dictionary<(int, int), List<int>>();
        for (int k = 0; k < roadNodes.Count; k++)
        {
            NavNode n = nodes[roadNodes[k].Node];
            var key = ((int)Math.Floor(n.X / CellMeters), (int)Math.Floor(n.Z / CellMeters));
            if (!cells.TryGetValue(key, out List<int>? list))
            {
                cells[key] = list = new List<int>();
            }

            list.Add(k);
        }

        var linked = new HashSet<(int, int)>();
        for (int k = 0; k < roadNodes.Count; k++)
        {
            NavNode n = nodes[roadNodes[k].Node];
            int cx = (int)Math.Floor(n.X / CellMeters);
            int cz = (int)Math.Floor(n.Z / CellMeters);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!cells.TryGetValue((cx + dx, cz + dz), out List<int>? list))
                    {
                        continue;
                    }

                    foreach (int other in list)
                    {
                        if (other <= k || roadNodes[other].Road == roadNodes[k].Road)
                        {
                            continue;
                        }

                        int a = roadNodes[k].Node;
                        int b = roadNodes[other].Node;
                        double d = Distance(nodes[a], nodes[b]);
                        if (d <= JunctionMeters && linked.Add((a, b)))
                        {
                            edges.Add(new NavEdge(a, b, d));
                        }
                    }
                }
            }
        }
    }

    private static double Distance(NavNode a, NavNode b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));
}
