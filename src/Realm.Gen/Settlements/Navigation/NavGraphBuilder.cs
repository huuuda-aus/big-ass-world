using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Validation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;
using Realm.Sim.Navigation;

namespace Realm.Gen.Settlements.Navigation;

/// <summary>
/// Builds the walking graph of a generated settlement: a ring around the central square, the streets to the gates, a node in front of
/// every front door, and links between neighbours that can see each other across open ground (lanes between the rows of houses emerge
/// from those links). Walls, buildings, the centrepiece and water block links; gates and bridges are explicit.
/// </summary>
/// <remarks>
/// The graph is in the city's plan frame (see <c>Settlement.ToWorld</c> for world coordinates). Determinism: pure; fixed iteration
/// orders and node-ID tie breaks. Thread-safety: pure.
/// </remarks>
public static class NavGraphBuilder
{
    /// <summary>Generator identifier recorded with derived data.</summary>
    public const string GeneratorId = "gen.settlements.nav";

    /// <summary>Generator version; bump when the graph changes.</summary>
    public const int GeneratorVersion = 1;

    private const double RingExtraMeters = 1.5;
    private const double MinRingRadiusMeters = 3;
    private const int RingNodes = 8;
    private const double StreetSpacingMeters = 8;
    private const double MergeMeters = 0.5;
    private const double DoorFrontMeters = 1.3;
    private const double NeighbourRadiusMeters = 40;
    private const int NeighbourCount = 8;
    private const double YardWeight = 1.35; // a link from door to door across open ground costs more than the same length of street
    private const double RepairRadiusMeters = 90;
    private const int RepairTries = 40;
    private const double GateOutsideMeters = 8;
    private const double GateToleranceMeters = 6;
    private const double BridgeLinkMeters = 25;
    private const double GridCellMeters = 1;
    private const double GridPadMeters = 12;
    private static readonly (int Dx, int Dy)[] Neighbours = { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1) };

    /// <summary>Builds the graph of a city.</summary>
    /// <param name="city">The generated city.</param>
    /// <param name="water">Where water lies (links never cross it except on bridges), or null for a dry site.</param>
    /// <param name="fortification">What stands along the wall line, which blocks walkers (a settlement without walls has no obstacle there).</param>
    /// <returns>The graph and anything that could not be connected.</returns>
    public static NavGraphResult Build(City city, IWaterSampler? water = null, Fortification fortification = Fortification.Stone)
    {
        ArgumentNullException.ThrowIfNull(city);
        var b = new Builder(city, water, fortification);
        return b.Run();
    }

    /// <summary>Reports the problems of a built graph.</summary>
    /// <param name="result">The build result.</param>
    /// <param name="id">The settlement ID, used as the location in issues.</param>
    /// <param name="report">Receives NAV001 (door unreachable) and NAV002 (gate unreachable).</param>
    public static void Validate(NavGraphResult result, string id, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(report);
        if (result.UnreachableBuildings.Count > 0)
        {
            report.Warning(
                NavCodes.DoorUnreachable,
                id,
                $"{result.UnreachableBuildings.Count} building(s) cannot be reached on foot from the central square; for example building {result.UnreachableBuildings[0]}.",
                "A building may be walled in by its neighbours; widen the gaps between buildings or check the site's water.");
        }

        foreach (int gate in result.UnreachableGates)
        {
            report.Error(NavCodes.GateUnreachable, id, $"Gate {gate} cannot be reached on foot from the central square.", "Check the streets to the gates and the water around the gate.");
        }
    }

    private sealed class Builder
    {
        private readonly City _city;
        private readonly IWaterSampler? _water;
        private readonly List<NavNode> _nodes = new();
        private readonly List<NavEdge> _edges = new();
        private readonly HashSet<(int, int)> _linked = new();
        private readonly Dictionary<(int, int), int> _merge = new();
        private readonly List<Building> _buildings;
        private readonly OpenGround _open;
        private readonly double _landmarkRadius;

        public Builder(City city, IWaterSampler? water, Fortification fortification)
        {
            _city = city;
            _water = water;
            _buildings = city.AllBuildings.ToList();
            _open = new OpenGround(city, water, fortification);
            _landmarkRadius = _open.LandmarkSolidRadius;
        }

        public NavGraphResult Run()
        {
            var ring = AddRing();
            AddStreets(ring);
            List<int> gateNodes = AddGates();
            AddBridges();
            List<int> doors = AddDoors();
            LinkDoors(doors);

            var graph = new NavGraph(_nodes, _edges);
            int home = ring[0];
            for (int node = 0; node < _nodes.Count; node++)
            {
                if (graph.AreConnected(node, home))
                {
                    continue;
                }

                // First the cheap way: a straight link to the main group; then a search through the open ground, which finds the lanes
                // the straight links cannot see (a door in a pocket between two rows of houses).
                if (Repair(node, graph, home) || GridRepair(node, graph, home))
                {
                    graph = new NavGraph(_nodes, _edges);
                }
            }

            var unreachableBuildings = doors.Where(d => !graph.AreConnected(d, home)).Select(d => _nodes[d].Ref).OrderBy(x => x).ToList();
            var unreachableGates = new List<int>();
            for (int g = 0; g < gateNodes.Count; g++)
            {
                if (gateNodes[g] >= 0 && !graph.AreConnected(gateNodes[g], home))
                {
                    unreachableGates.Add(g);
                }
            }

            return new NavGraphResult(graph, unreachableBuildings, unreachableGates);
        }

        // ---- nodes -----------------------------------------------------------------------------------------------

        private int AddNode(Vec2 p, NavNodeKind kind, int reference = -1)
        {
            if (kind == NavNodeKind.Street)
            {
                var key = ((int)Math.Round(p.X / MergeMeters), (int)Math.Round(p.Y / MergeMeters));
                if (_merge.TryGetValue(key, out int existing))
                {
                    return existing;
                }

                _merge[key] = _nodes.Count;
            }

            _nodes.Add(new NavNode(p.X, p.Y, kind, reference));
            return _nodes.Count - 1;
        }

        private void Link(int a, int b, bool bridge = false, double weight = 1)
        {
            if (a == b || !_linked.Add((Math.Min(a, b), Math.Max(a, b))))
            {
                return;
            }

            _edges.Add(new NavEdge(a, b, Distance(a, b), bridge, weight));
        }

        private double Distance(int a, int b) =>
            Math.Sqrt(((_nodes[a].X - _nodes[b].X) * (_nodes[a].X - _nodes[b].X)) + ((_nodes[a].Z - _nodes[b].Z) * (_nodes[a].Z - _nodes[b].Z)));

        private Vec2 At(int node) => new(_nodes[node].X, _nodes[node].Z);

        private List<int> AddRing()
        {
            double radius = Math.Max(MinRingRadiusMeters, _landmarkRadius + RingExtraMeters);
            var ring = new List<int>();
            for (int k = 0; k < RingNodes; k++)
            {
                double angle = 2 * Math.PI * k / RingNodes;
                ring.Add(AddNode(new Vec2(radius * Math.Cos(angle), radius * Math.Sin(angle)), NavNodeKind.Street));
            }

            for (int k = 0; k < RingNodes; k++)
            {
                Link(ring[k], ring[(k + 1) % RingNodes]);
            }

            return ring;
        }

        private void AddStreets(List<int> ring)
        {
            double keepOut = Math.Max(MinRingRadiusMeters, _landmarkRadius + RingExtraMeters) + (MergeMeters * 2);
            foreach (Street street in _city.Streets)
            {
                int previous = -1;
                bool joined = false;
                foreach (Vec2 p in Resample(street.Points, StreetSpacingMeters))
                {
                    if (p.Length < keepOut)
                    {
                        continue;
                    }

                    if (_water is not null && _water.IsWater(p.X, p.Y))
                    {
                        previous = -1; // a bridge joins the two banks (AddBridges), not the street
                        joined = true; // and what lies beyond the water must not be linked straight to the square
                        continue;
                    }

                    if (_open.Blocked(p))
                    {
                        // A street that the generator ran along the wall (or through a corner of a house) is not walkable there: skip
                        // that stretch; the repair below finds the way round.
                        previous = -1;
                        continue;
                    }

                    int node = AddNode(p, NavNodeKind.Street);
                    if (previous < 0 && !joined)
                    {
                        joined = true;
                        // The first point outside the ring joins the nearest ring node.
                        // (Not across water or a wall: then the repair below finds another way.)
                        foreach (int r in ring.OrderBy(r => Vec2.Distance(At(r), p)).ThenBy(r => r))
                        {
                            if (_open.Clear(p, At(r)))
                            {
                                Link(node, r);
                                break;
                            }
                        }
                    }
                    else if (previous >= 0 && _open.Clear(At(previous), p))
                    {
                        Link(previous, node);
                    }

                    previous = node;
                }
            }
        }

        private List<int> AddGates()
        {
            var gateNodes = new List<int>();
            foreach (Vec2 gate in _city.Gates)
            {
                if (_water is not null && _water.IsWater(gate.X, gate.Y))
                {
                    gateNodes.Add(-1); // a gate in the river cannot be walked through; nothing to connect
                    continue;
                }

                int node = AddNode(gate, NavNodeKind.Gate);
                gateNodes.Add(node);
                int street = NearestOfKind(gate, NavNodeKind.Street, GateToleranceMeters * 2);
                if (street >= 0 && _open.Clear(gate, At(street)))
                {
                    Link(node, street);
                }

                Vec2 outward = OpenGround.GateOutward(_city, gate);
                int outside = AddNode(gate + (outward * GateOutsideMeters), NavNodeKind.Outside);
                Link(node, outside);
            }

            return gateNodes;
        }

        private void AddBridges()
        {
            foreach (CityBridge bridge in _city.Bridges)
            {
                int from = AddNode(bridge.From, NavNodeKind.BridgeEnd);
                int to = AddNode(bridge.To, NavNodeKind.BridgeEnd);
                Link(from, to, bridge: true);
                foreach (int end in new[] { from, to })
                {
                    foreach (int other in Candidates(At(end), BridgeLinkMeters).Where(o => o != from && o != to && _nodes[o].Kind != NavNodeKind.BridgeEnd).Take(RepairTries))
                    {
                        if (_open.Clear(At(end), At(other)))
                        {
                            Link(end, other);
                            break;
                        }
                    }
                }
            }
        }

        private List<int> AddDoors()
        {
            var doors = new List<int>();
            foreach (Building building in _buildings)
            {
                ConvexPoly poly = building.Footprint;
                Vec2 mid = (poly.Points[building.DoorEdge] + poly.EdgeEnd(building.DoorEdge)) * 0.5;
                Vec2 normal = poly.OutwardNormal(building.DoorEdge);
                Vec2 front = mid + (normal * DoorFrontMeters);
                for (double reach = DoorFrontMeters; reach <= DoorFrontMeters + 3; reach += 0.8)
                {
                    front = mid + (normal * reach);
                    if (!_open.Blocked(front))
                    {
                        break;
                    }
                }

                doors.Add(AddNode(front, NavNodeKind.Door, building.Id));
            }

            return doors;
        }

        // ---- links -----------------------------------------------------------------------------------------------

        private void LinkDoors(List<int> doors)
        {
            foreach (int door in doors)
            {
                int linked = 0;
                foreach (int other in Candidates(At(door), NeighbourRadiusMeters))
                {
                    if (other == door)
                    {
                        continue;
                    }

                    if (_open.Clear(At(door), At(other)))
                    {
                        Link(door, other, weight: _nodes[other].Kind == NavNodeKind.Door ? YardWeight : 1);
                        if (++linked >= NeighbourCount)
                        {
                            break;
                        }
                    }
                }
            }
        }

        // Joins a node that is cut off to the nearest node of the main group that it can see.
        private bool Repair(int node, NavGraph graph, int home)
        {
            int tried = 0;
            foreach (int other in Candidates(At(node), RepairRadiusMeters))
            {
                if (!graph.AreConnected(other, home))
                {
                    continue;
                }

                if (_open.Clear(At(node), At(other)))
                {
                    Link(node, other, weight: _nodes[other].Kind == NavNodeKind.Door && _nodes[node].Kind == NavNodeKind.Door ? YardWeight : 1);
                    return true;
                }

                if (++tried >= RepairTries)
                {
                    break;
                }
            }

            return false;
        }

        // ---- search through open ground --------------------------------------------------------------------------

        private bool[]? _blockedCell;
        private double _gridMinX;
        private double _gridMinY;
        private int _gridWidth;
        private int _gridHeight;

        private void BuildGrid()
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (NavNode n in _nodes)
            {
                minX = Math.Min(minX, n.X);
                maxX = Math.Max(maxX, n.X);
                minY = Math.Min(minY, n.Z);
                maxY = Math.Max(maxY, n.Z);
            }

            foreach (Building b in _buildings)
            {
                foreach (Vec2 p in b.Footprint.Points)
                {
                    minX = Math.Min(minX, p.X);
                    maxX = Math.Max(maxX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxY = Math.Max(maxY, p.Y);
                }
            }

            _gridMinX = minX - GridPadMeters;
            _gridMinY = minY - GridPadMeters;
            _gridWidth = (int)Math.Ceiling((maxX - minX + (2 * GridPadMeters)) / GridCellMeters) + 1;
            _gridHeight = (int)Math.Ceiling((maxY - minY + (2 * GridPadMeters)) / GridCellMeters) + 1;
            _blockedCell = new bool[_gridWidth * _gridHeight];
            for (int y = 0; y < _gridHeight; y++)
            {
                for (int x = 0; x < _gridWidth; x++)
                {
                    _blockedCell[(y * _gridWidth) + x] = _open.Blocked(CellCentre(x, y));
                }
            }

            // The wall blocks the ground except at the gates.
            IReadOnlyList<Vec2> wall = _city.Wall;
            for (int i = 0; i < wall.Count; i++)
            {
                Vec2 a = wall[i];
                Vec2 c = wall[(i + 1) % wall.Count];
                int steps = Math.Max(1, (int)Math.Ceiling(Vec2.Distance(a, c) / (GridCellMeters * 0.5)));
                for (int s = 0; s <= steps; s++)
                {
                    Vec2 p = Vec2.Lerp(a, c, s / (double)steps);
                    if (_city.Gates.Any(g => Vec2.Distance(g, p) <= GateToleranceMeters))
                    {
                        continue;
                    }

                    int cx = (int)Math.Floor((p.X - _gridMinX) / GridCellMeters);
                    int cy = (int)Math.Floor((p.Y - _gridMinY) / GridCellMeters);
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int x = cx + dx, y = cy + dy;
                            if (x >= 0 && y >= 0 && x < _gridWidth && y < _gridHeight)
                            {
                                _blockedCell[(y * _gridWidth) + x] = true;
                            }
                        }
                    }
                }
            }
        }

        private Vec2 CellCentre(int x, int y) => new(_gridMinX + ((x + 0.5) * GridCellMeters), _gridMinY + ((y + 0.5) * GridCellMeters));

        private int CellOf(Vec2 p)
        {
            int x = (int)Math.Floor((p.X - _gridMinX) / GridCellMeters);
            int y = (int)Math.Floor((p.Y - _gridMinY) / GridCellMeters);
            return (uint)x < (uint)_gridWidth && (uint)y < (uint)_gridHeight ? (y * _gridWidth) + x : -1;
        }

        // Breadth-first search over free cells from the node to the nearest cell holding a node of the main group; the route, straightened,
        // becomes new street nodes joining the node to the main group.
        private bool GridRepair(int node, NavGraph graph, int home)
        {
            if (_blockedCell is null)
            {
                BuildGrid();
            }

            int start = CellOf(At(node));
            if (start < 0)
            {
                return false;
            }

            var target = new Dictionary<int, int>();
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (i != node && graph.AreConnected(i, home))
                {
                    int cell = CellOf(At(i));
                    if (cell >= 0 && !target.ContainsKey(cell))
                    {
                        target[cell] = i;
                    }
                }
            }

            var parent = new Dictionary<int, int> { [start] = -1 };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            int found = -1;
            while (queue.Count > 0 && found < 0)
            {
                int cell = queue.Dequeue();
                if (target.ContainsKey(cell))
                {
                    found = cell;
                    break;
                }

                int cx = cell % _gridWidth;
                int cy = cell / _gridWidth;
                for (int k = 0; k < 8; k++)
                {
                    int x = cx + Neighbours[k].Dx;
                    int y = cy + Neighbours[k].Dy;
                    if ((uint)x >= (uint)_gridWidth || (uint)y >= (uint)_gridHeight)
                    {
                        continue;
                    }

                    int next = (y * _gridWidth) + x;
                    bool diagonal = Neighbours[k].Dx != 0 && Neighbours[k].Dy != 0;
                    // The start cell may itself be blocked by a margin; the door front was chosen as open ground.
                    if (parent.ContainsKey(next) || (_blockedCell![next] && !target.ContainsKey(next))
                        || (diagonal && (_blockedCell![(cy * _gridWidth) + x] || _blockedCell[(y * _gridWidth) + cx])))
                    {
                        continue;
                    }

                    parent[next] = cell;
                    queue.Enqueue(next);
                }
            }

            if (found < 0)
            {
                return false;
            }

            var route = new List<Vec2> { At(node) };
            var cells = new List<int>();
            for (int c = parent[found]; c >= 0 && c != start; c = parent[c])
            {
                cells.Add(c);
            }

            cells.Reverse();
            foreach (int c in cells)
            {
                route.Add(CellCentre(c % _gridWidth, c / _gridWidth));
            }

            route.Add(At(target[found]));

            // Straighten the route: keep the furthest point that can still be seen from the last kept one.
            int previous = node;
            int i0 = 0;
            while (i0 < route.Count - 1)
            {
                int j = route.Count - 1;
                while (j > i0 + 1 && !_open.Clear(route[i0], route[j]))
                {
                    j--;
                }

                i0 = j;
                int next = j == route.Count - 1 ? target[found] : AddNode(route[j], NavNodeKind.Street);
                Link(previous, next);
                previous = next;
            }

            return true;
        }

        // Nodes within a radius, nearest first, ties by ID.
        private IEnumerable<int> Candidates(Vec2 from, double radius) =>
            Enumerable.Range(0, _nodes.Count)
                .Select(i => (Id: i, D: Vec2.Distance(At(i), from)))
                .Where(t => t.D <= radius)
                .OrderBy(t => t.D).ThenBy(t => t.Id)
                .Select(t => t.Id);

        private int NearestOfKind(Vec2 from, NavNodeKind kind, double radius)
        {
            int best = -1;
            double bestD = radius;
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].Kind != kind)
                {
                    continue;
                }

                double d = Vec2.Distance(At(i), from);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            return best;
        }

    }

    // Points along a polyline no further apart than a spacing, including both ends.
    private static IEnumerable<Vec2> Resample(IReadOnlyList<Vec2> points, double spacing)
    {
        if (points.Count == 0)
        {
            yield break;
        }

        yield return points[0];
        for (int i = 0; i + 1 < points.Count; i++)
        {
            double length = Vec2.Distance(points[i], points[i + 1]);
            int steps = Math.Max(1, (int)Math.Ceiling(length / spacing));
            for (int s = 1; s <= steps; s++)
            {
                yield return Vec2.Lerp(points[i], points[i + 1], s / (double)steps);
            }
        }
    }
}
