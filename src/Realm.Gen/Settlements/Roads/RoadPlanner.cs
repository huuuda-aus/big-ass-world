using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Validation;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Settlements.Placement;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;

namespace Realm.Gen.Settlements.Roads;

/// <summary>
/// Plans the roads between settlements (CLAUDE.md section 6.1, steps 7 and 8): the settlements are joined by a spanning tree plus
/// extra links; each link is the cheapest route (A*) over the terrain from a gate of one city to a gate of the other, with a cost
/// for slope and for crossing rivers; the route is smoothed and given a graded surface height; river crossings become bridges.
/// </summary>
/// <remarks>
/// Determinism: identified by <see cref="GeneratorId"/>, <see cref="GeneratorVersion"/>, the settlements, the terrain and the settings;
/// ties are broken by node index. Thread-safety: pure.
/// </remarks>
public static class RoadPlanner
{
    /// <summary>Identifies this generator in saved or baked data.</summary>
    public const string GeneratorId = "road.planner";

    /// <summary>Bump when the output for the same input changes, and regenerate the golden tests in the same change.</summary>
    public const int GeneratorVersion = 1;

    private const int ChaikinPasses = 2;
    private const double Sqrt2 = 1.4142135623730951;
    private const int SmoothingHalfWindow = 4;
    private const int GradeSweeps = 3;
    private const int MergeBridgeGap = 2;
    private const double FootingMeters = 3; // how far in from the end of the deck an abutment reaches
    private const int FootingAlongSamples = 3;

    private static readonly (int Dx, int Dz)[] Neighbours =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
    };

    /// <summary>Plans the road network.</summary>
    /// <param name="field">Terrain heights after the pads were flattened.</param>
    /// <param name="network">Rivers and lakes, or null.</param>
    /// <param name="settlements">The settlements to join.</param>
    /// <param name="settings">Tuning; defaults when null.</param>
    /// <param name="report">Receives <see cref="RoadCodes"/> issues.</param>
    /// <returns>The roads, in link order. Empty when there are fewer than two settlements.</returns>
    public static IReadOnlyList<Road> Plan(
        HeightField field,
        RiverNetwork? network,
        IReadOnlyList<Settlement> settlements,
        RoadSettings? settings,
        ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(settlements);
        ArgumentNullException.ThrowIfNull(report);
        settings ??= new RoadSettings();
        if (!settings.TryValidate(out string? problem))
        {
            report.Error(RoadCodes.SettingsInvalid, "roads", problem!);
            return Array.Empty<Road>();
        }

        var raster = new RiverRaster(field.WorldSizeMeters, network);
        var grid = new SearchGrid(field, raster, settings);
        var gates = settlements.ToDictionary(s => s.Id, s => GatePoints(s));
        var roads = new List<Road>();

        // Each road is graded on the terrain left by the roads before it, so roads that share a corridor share a surface.
        float[] working = field.Heights.ToArray();
        var current = new HeightField(field.WorldSizeMeters, field.SpacingMeters, working);
        int number = 1;
        foreach ((int a, int b) in Links(settlements, settings))
        {
            Settlement from = settlements[a];
            Settlement to = settlements[b];
            (double X, double Z) gateFrom = NearestGate(gates[from.Id], to.X, to.Z);
            (double X, double Z) gateTo = NearestGate(gates[to.Id], from.X, from.Z);
            (double X, double Z) outFrom = Outside(from, gateFrom, settings.GateApproachMeters);
            (double X, double Z) outTo = Outside(to, gateTo, settings.GateApproachMeters);

            List<(double X, double Z)>? route = grid.FindRoute(outFrom, outTo);
            if (route is null)
            {
                continue;
            }

            var polyline = new List<(double X, double Z)> { gateFrom };
            polyline.AddRange(route);
            polyline.Add(gateTo);
            Road road = Build($"road.{number}", from.Id, to.Id, polyline, current, raster, settings);
            roads.Add(road);
            RoadTerrain.ApplyRoad(working, field.WorldSizeMeters, field.SpacingMeters, road, settings);
            number++;
        }

        ReportConnectivity(settlements, roads, report);
        return roads;
    }

    // ---- links between settlements -------------------------------------------------------------------------------

    // A minimum spanning tree over straight-line distances, plus (optionally) each settlement's nearest neighbour not yet linked to it.
    private static List<(int A, int B)> Links(IReadOnlyList<Settlement> s, RoadSettings settings)
    {
        var links = new List<(int, int)>();
        int n = s.Count;
        if (n < 2)
        {
            return links;
        }

        double D(int i, int j) => s[i].DistanceTo(s[j].X, s[j].Z);
        var inTree = new bool[n];
        inTree[0] = true;
        for (int step = 1; step < n; step++)
        {
            double best = double.MaxValue;
            (int, int) edge = (0, 0);
            for (int i = 0; i < n; i++)
            {
                if (!inTree[i])
                {
                    continue;
                }

                for (int j = 0; j < n; j++)
                {
                    if (!inTree[j] && D(i, j) < best)
                    {
                        best = D(i, j);
                        edge = (i, j);
                    }
                }
            }

            inTree[edge.Item2] = true;
            links.Add(edge);
        }

        if (settings.ExtraLinks)
        {
            var linked = new HashSet<(int, int)>(links.Select(l => (Math.Min(l.Item1, l.Item2), Math.Max(l.Item1, l.Item2))));
            for (int i = 0; i < n; i++)
            {
                int nearest = -1;
                for (int j = 0; j < n; j++)
                {
                    if (j != i && !linked.Contains((Math.Min(i, j), Math.Max(i, j))) && (nearest < 0 || D(i, j) < D(i, nearest)))
                    {
                        nearest = j;
                    }
                }

                if (nearest >= 0)
                {
                    (int, int) key = (Math.Min(i, nearest), Math.Max(i, nearest));
                    linked.Add(key);
                    links.Add(key);
                }
            }
        }

        return links;
    }

    private static List<(double X, double Z)> GatePoints(Settlement s)
    {
        City city = CityGenerator.Generate(s.ToCityParams());
        return city.Gates.Select(g => s.ToWorld(g)).ToList();
    }

    private static (double X, double Z) NearestGate(List<(double X, double Z)> gates, double x, double z) =>
        gates.OrderBy(g => ((g.X - x) * (g.X - x)) + ((g.Z - z) * (g.Z - z))).First();

    private static (double X, double Z) Outside(Settlement s, (double X, double Z) gate, double distance)
    {
        double dx = gate.X - s.X;
        double dz = gate.Z - s.Z;
        double length = Math.Sqrt((dx * dx) + (dz * dz));
        return length < 1e-9 ? gate : (gate.X + (dx / length * distance), gate.Z + (dz / length * distance));
    }

    private static void ReportConnectivity(IReadOnlyList<Settlement> settlements, IReadOnlyList<Road> roads, ValidationReport report)
    {
        if (settlements.Count < 2)
        {
            return;
        }

        var parent = settlements.ToDictionary(s => s.Id, s => s.Id);
        string Find(string id)
        {
            while (parent[id] != id)
            {
                id = parent[id];
            }

            return id;
        }

        foreach (Road r in roads)
        {
            parent[Find(r.From)] = Find(r.To);
        }

        string main = Find(settlements[0].Id);
        foreach (Settlement s in settlements.Where(s => Find(s.Id) != main))
        {
            report.Warning(
                RoadCodes.NotConnected,
                s.Id,
                $"'{s.Id}' ({s.Name}) is not connected to '{settlements[0].Id}' by road.",
                "Allow steeper roads or more river crossings, or check that the land between them is not sea or lake.");
        }
    }

    // ---- from a polyline to a graded road ------------------------------------------------------------------------

    private static Road Build(string id, string from, string to, List<(double X, double Z)> polyline, HeightField field, RiverRaster raster, RoadSettings settings)
    {
        List<(double X, double Z)> smooth = polyline;
        for (int pass = 0; pass < ChaikinPasses; pass++)
        {
            smooth = Chaikin(smooth);
        }

        List<(double X, double Z)> samples = Resample(smooth, settings.SampleMeters);
        int n = samples.Count;
        var raw = new double[n];
        var river = new bool[n];
        for (int i = 0; i < n; i++)
        {
            raw[i] = field.Sample(samples[i].X, samples[i].Z);
            river[i] = raster.IsRiver(samples[i].X, samples[i].Z);
        }

        List<(int First, int Last)> spans = BridgeSpans(river);
        double[] profile = Profile(raw, spans, settings);

        var points = new List<RoadPoint>(n);
        for (int i = 0; i < n; i++)
        {
            points.Add(new RoadPoint(Math.Round(samples[i].X, 2), Math.Round(samples[i].Z, 2), Math.Round(profile[i], 2)));
        }

        var bridges = new List<RoadBridge>();
        foreach ((int first, int last) in spans)
        {
            RoadPoint a = points[first];
            RoadPoint b = points[last];
            double dx = b.X - a.X;
            double dz = b.Z - a.Z;
            bridges.Add(new RoadBridge(
                Math.Round((a.X + b.X) / 2, 2),
                Math.Round((a.Z + b.Z) / 2, 2),
                Math.Round(Math.Atan2(dz, dx), 6),
                Math.Round(Math.Sqrt((dx * dx) + (dz * dz)), 2),
                settings.WidthMeters,
                a.Height,
                b.Height,
                first,
                last,
                Math.Round(LowestGround(field, a, dx, dz, settings.WidthMeters, 1), 2),
                Math.Round(LowestGround(field, b, dx, dz, settings.WidthMeters, -1), 2)));
        }

        return new Road(id, from, to, settings.WidthMeters, points, bridges);
    }

    // The lowest ground under an abutment's footprint (a few metres inward from the end of the deck, as wide as the deck), so the
    // abutment can be built down to it. `inward` is +1 at the first end and -1 at the second.
    private static double LowestGround(HeightField field, RoadPoint end, double dx, double dz, double width, int inward)
    {
        double length = Math.Max(Math.Sqrt((dx * dx) + (dz * dz)), 1e-6);
        double ax = dx / length * inward;
        double az = dz / length * inward;
        double lowest = double.MaxValue;
        double half = field.WorldSizeMeters / 2.0;
        for (int along = 0; along <= FootingAlongSamples; along++)
        {
            for (int across = -1; across <= 1; across++)
            {
                double x = end.X + (ax * along * FootingMeters / FootingAlongSamples) - (az * across * width / 2);
                double z = end.Z + (az * along * FootingMeters / FootingAlongSamples) + (ax * across * width / 2);
                lowest = Math.Min(lowest, field.Sample(Math.Clamp(x, -half, half), Math.Clamp(z, -half, half)));
            }
        }

        return lowest;
    }

    // Runs of river samples become bridge spans: from the sample before the water to the sample after it. Runs separated by
    // a short stretch of land are one bridge.
    private static List<(int First, int Last)> BridgeSpans(bool[] river)
    {
        var runs = new List<(int Start, int End)>();
        int n = river.Length;
        for (int i = 0; i < n; i++)
        {
            if (!river[i])
            {
                continue;
            }

            int start = i;
            while (i + 1 < n && river[i + 1])
            {
                i++;
            }

            if (runs.Count > 0 && start - runs[^1].End - 1 <= MergeBridgeGap)
            {
                runs[^1] = (runs[^1].Start, i);
            }
            else
            {
                runs.Add((start, i));
            }
        }

        var spans = new List<(int, int)>();
        foreach ((int start, int end) in runs)
        {
            int first = Math.Max(0, start - 1);
            int last = Math.Min(n - 1, end + 1);
            if (spans.Count > 0 && first <= spans[^1].Item2)
            {
                spans[^1] = (spans[^1].Item1, Math.Max(spans[^1].Item2, last));
            }
            else
            {
                spans.Add((first, last));
            }
        }

        return spans;
    }

    // The graded surface: terrain heights with bridge decks bridged over, smoothed, then limited to the maximum grade.
    private static double[] Profile(double[] raw, List<(int First, int Last)> spans, RoadSettings settings)
    {
        int n = raw.Length;
        double[] filled = (double[])raw.Clone();
        foreach ((int first, int last) in spans)
        {
            for (int i = first + 1; i < last; i++)
            {
                filled[i] = raw[first] + ((raw[last] - raw[first]) * (i - first) / (last - first));
            }
        }

        var smooth = new double[n];
        for (int i = 0; i < n; i++)
        {
            int lo = Math.Max(0, i - SmoothingHalfWindow);
            int hi = Math.Min(n - 1, i + SmoothingHalfWindow);
            double sum = 0;
            for (int k = lo; k <= hi; k++)
            {
                sum += filled[k];
            }

            smooth[i] = sum / (hi - lo + 1);
        }

        double step = settings.MaxGrade * settings.SampleMeters;
        for (int sweep = 0; sweep < GradeSweeps; sweep++)
        {
            for (int i = 1; i < n; i++)
            {
                smooth[i] = Math.Clamp(smooth[i], smooth[i - 1] - step, smooth[i - 1] + step);
            }

            for (int i = n - 2; i >= 0; i--)
            {
                smooth[i] = Math.Clamp(smooth[i], smooth[i + 1] - step, smooth[i + 1] + step);
            }
        }

        // A deck is straight between its abutments.
        foreach ((int first, int last) in spans)
        {
            for (int i = first + 1; i < last; i++)
            {
                smooth[i] = smooth[first] + ((smooth[last] - smooth[first]) * (i - first) / (last - first));
            }
        }

        return smooth;
    }

    private static List<(double X, double Z)> Chaikin(List<(double X, double Z)> points)
    {
        if (points.Count < 3)
        {
            return points;
        }

        var result = new List<(double, double)> { points[0] };
        for (int i = 0; i + 1 < points.Count; i++)
        {
            (double ax, double az) = points[i];
            (double bx, double bz) = points[i + 1];
            result.Add((ax + (0.25 * (bx - ax)), az + (0.25 * (bz - az))));
            result.Add((ax + (0.75 * (bx - ax)), az + (0.75 * (bz - az))));
        }

        result.Add(points[^1]);
        return result;
    }

    private static List<(double X, double Z)> Resample(List<(double X, double Z)> points, double spacing)
    {
        var result = new List<(double, double)> { points[0] };
        double carried = 0;
        for (int i = 0; i + 1 < points.Count; i++)
        {
            (double ax, double az) = points[i];
            (double bx, double bz) = points[i + 1];
            double length = Math.Sqrt(((bx - ax) * (bx - ax)) + ((bz - az) * (bz - az)));
            double at = spacing - carried;
            while (at <= length)
            {
                result.Add((ax + ((bx - ax) * at / length), az + ((bz - az) * at / length)));
                at += spacing;
            }

            carried = length - (at - spacing);
        }

        if (Math.Abs(result[^1].Item1 - points[^1].X) > 1e-6 || Math.Abs(result[^1].Item2 - points[^1].Z) > 1e-6)
        {
            result.Add(points[^1]);
        }

        return result;
    }

    // ---- route search --------------------------------------------------------------------------------------------

    private sealed class SearchGrid
    {
        private readonly HeightField _field;
        private readonly RiverRaster _raster;
        private readonly RoadSettings _settings;
        private readonly int _side;
        private readonly double _half;
        private readonly double[] _height;
        private readonly byte[] _kind; // 0 land, 1 river, 2 blocked (sea or lake)

        public SearchGrid(HeightField field, RiverRaster raster, RoadSettings settings)
        {
            _field = field;
            _raster = raster;
            _settings = settings;
            _half = field.WorldSizeMeters / 2.0;
            _side = (int)Math.Floor(field.WorldSizeMeters / settings.GridMeters) + 1;
            _height = new double[_side * _side];
            _kind = new byte[_side * _side];
            for (int cz = 0; cz < _side; cz++)
            {
                for (int cx = 0; cx < _side; cx++)
                {
                    (double x, double z) = Position(cx, cz);
                    int i = (cz * _side) + cx;
                    _height[i] = field.Sample(Math.Min(x, _half - 1e-6), Math.Min(z, _half - 1e-6));
                    _kind[i] = (byte)(_raster.IsLake(x, z) || _height[i] < settings.MinHeightMeters ? 2 : (_raster.IsRiver(x, z) ? 1 : 0));
                }
            }
        }

        public List<(double X, double Z)>? FindRoute((double X, double Z) from, (double X, double Z) to)
        {
            int start = Nearest(from);
            int goal = Nearest(to);
            if (_kind[start] == 2 || _kind[goal] == 2)
            {
                return null;
            }

            int count = _height.Length;
            var cost = new double[count];
            var previous = new int[count];
            Array.Fill(cost, double.MaxValue);
            Array.Fill(previous, -1);
            var queue = new PriorityQueue<int, (double, int)>();
            cost[start] = 0;
            queue.Enqueue(start, (Heuristic(start, goal), start));
            var closed = new bool[count];
            while (queue.TryDequeue(out int node, out _))
            {
                if (closed[node])
                {
                    continue;
                }

                closed[node] = true;
                if (node == goal)
                {
                    break;
                }

                int cx = node % _side;
                int cz = node / _side;
                foreach ((int dx, int dz) in Neighbours)
                {
                    int nx = cx + dx;
                    int nz = cz + dz;
                    if (nx < 0 || nz < 0 || nx >= _side || nz >= _side)
                    {
                        continue;
                    }

                    int next = (nz * _side) + nx;
                    if (closed[next] || _kind[next] == 2)
                    {
                        continue;
                    }

                    double distance = _settings.GridMeters * (dx != 0 && dz != 0 ? Sqrt2 : 1);
                    double slope = Math.Abs(_height[next] - _height[node]) / distance;
                    double step = (distance * (1 + (_settings.SlopePenalty * slope * slope))) + (_kind[next] == 1 ? _settings.RiverCrossingPenaltyMeters : 0);
                    double candidate = cost[node] + step;
                    if (candidate < cost[next])
                    {
                        cost[next] = candidate;
                        previous[next] = node;
                        queue.Enqueue(next, (candidate + Heuristic(next, goal), next));
                    }
                }
            }

            if (previous[goal] < 0 && goal != start)
            {
                return null;
            }

            var route = new List<(double, double)>();
            for (int node = goal; node >= 0; node = previous[node])
            {
                route.Add(Position(node % _side, node / _side));
            }

            route.Reverse();
            route[0] = from;
            route[^1] = to;
            return route;
        }

        private (double X, double Z) Position(int cx, int cz) => (-_half + (cx * _settings.GridMeters), -_half + (cz * _settings.GridMeters));

        private int Nearest((double X, double Z) p) =>
            (Math.Clamp((int)Math.Round((p.Z + _half) / _settings.GridMeters), 0, _side - 1) * _side)
            + Math.Clamp((int)Math.Round((p.X + _half) / _settings.GridMeters), 0, _side - 1);

        private double Heuristic(int a, int b)
        {
            double dx = ((a % _side) - (b % _side)) * _settings.GridMeters;
            double dz = ((a / _side) - (b / _side)) * _settings.GridMeters;
            return Math.Sqrt((dx * dx) + (dz * dz));
        }
    }
}
