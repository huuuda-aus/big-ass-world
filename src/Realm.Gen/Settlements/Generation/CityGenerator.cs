using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Random;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Generation;

/// <summary>
/// Deterministic: the same CityParams always produce the same City.
/// Pipeline: sites -> relaxed Voronoi patches -> walled core -> gates and streets -> wards -> lots.
/// </summary>
public static class CityGenerator
{
    private const double StreetInset = 4.5;
    private const double AlleyInset = 2.2;
    private const double WallInset = 6.0;
    private const int OnStreetTag = 1;
    private const double MinFrontage = 3.5;
    private const double MaxLotDepth = 22;

    /// <summary>Each side of a house is set back this far from the lot line; neighbours stand twice this apart.</summary>
    private const double SideGap = 0.9;

    /// <summary>Average houses per ward at the default settings; used to guess a ward count for a target size.</summary>
    private const int TypicalBuildingsPerWard = 18;

    /// <summary>Generates a walled city.</summary>
    /// <param name="p">Size, seed and options.</param>
    /// <returns>The city, in a flat 2D plane in metres with the central square at the origin.</returns>
    /// <remarks>Determinism: the same <paramref name="p"/> always gives the same city on every machine (all randomness comes from <see cref="Rng"/>). Thread-safety: pure.</remarks>
    public static City Generate(CityParams p)
    {
        if (p.CityPatchCount is int fixedWards) return Build(p, fixedWards, p.MaxBuildings);
        if (p.MaxBuildings is not int max) return Build(p, CityParams.DefaultWardCount, null);

        // Grow the city until it can hold `max` houses (Build trims any excess), up to the number
        // of wards the site count supports.
        int limit = Math.Max(3, p.SiteCount / 3);
        int wards = Math.Min(limit, Math.Max(3, (int)Math.Ceiling(max / (double)TypicalBuildingsPerWard)));
        City city = Build(p, wards, max);
        while (BuildingCount(city) < max && wards < limit)
        {
            int missing = max - BuildingCount(city);
            wards = Math.Min(limit, wards + Math.Max(1, missing / TypicalBuildingsPerWard));
            city = Build(p, wards, max);
        }
        return city;
    }

    private static int BuildingCount(City city) => city.CityPatches.Sum(x => x.Buildings.Count);

    private static City Build(CityParams p, int wardCount, int? maxBuildings)
    {
        var rng = Rng.ForKey((ulong)(uint)p.Seed, "city");
        double halfSize = p.SiteSpacing * Math.Sqrt(p.SiteCount) * 2;

        Patch[] patches = BuildPatches(p, rng, halfSize);
        Patch[] cityPatches = patches
            .OrderBy(x => x.Site.Length)
            .Take(wardCount)
            .ToArray();
        var inCity = new HashSet<int>(cityPatches.Select(x => x.Index));

        List<Vec2> wall = BuildWall(cityPatches, inCity);
        List<Vec2> gates = PickGates(wall, Math.Min(p.GateCount, wall.Count), rng);
        List<Street> streets = BuildStreets(cityPatches, gates);

        var streetEdges = new HashSet<(VKey, VKey)>();
        foreach (Street s in streets)
            for (int i = 0; i + 1 < s.Points.Count; i++)
                streetEdges.Add(EdgeKey(s.Points[i], s.Points[i + 1]));

        var wallEdges = new HashSet<(VKey, VKey)>();
        for (int i = 0; i < wall.Count; i++)
            wallEdges.Add(EdgeKey(wall[i], wall[(i + 1) % wall.Count]));

        AssignWards(cityPatches, rng, p.Cathedral);
        foreach (Patch patch in cityPatches)
            BuildLots(patch, streetEdges, wallEdges, rng);

        if (maxBuildings is int cap) TrimBuildings(cityPatches, cap);

        // placed last, so choosing a centrepiece never changes where a single house stands
        Landmark? landmark = PlaceLandmark(cityPatches[0], p.Centre, Rng.ForKey((ulong)(uint)p.Seed, "city", "landmark"));

        return new City(p.Seed, patches, cityPatches, wall, gates, streets, landmark);
    }

    // ---- patches -------------------------------------------------------------------

    private static Patch[] BuildPatches(CityParams p, Rng rng, double halfSize)
    {
        const double goldenAngle = 2.399963229728653;
        var sites = new List<Vec2> { new Vec2(0, 0) };
        for (int i = 1; i < p.SiteCount; i++)
        {
            double r = p.SiteSpacing * Math.Sqrt(i) * (0.85 + rng.NextDouble() * 0.3);
            double a = i * goldenAngle + (rng.NextDouble() - 0.5) * 0.4;
            sites.Add(new Vec2(Math.Cos(a) * r, Math.Sin(a) * r));
        }

        Vec2[] relaxed = Voronoi.Relax(sites, halfSize, 2);
        ConvexPoly[] cells = Voronoi.Cells(relaxed, halfSize);

        var patches = new Patch[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            int[] neighbors = cells[i].Tags.Where(t => t >= 0).Distinct().ToArray();
            patches[i] = new Patch(i, relaxed[i], cells[i], neighbors);
        }
        return patches;
    }

    // ---- wall and gates ------------------------------------------------------------

    private static List<Vec2> BuildWall(Patch[] cityPatches, HashSet<int> inCity)
    {
        var next = new Dictionary<VKey, (Vec2 from, Vec2 to)>();
        foreach (Patch patch in cityPatches)
        {
            ConvexPoly s = patch.Shape;
            for (int i = 0; i < s.Count; i++)
            {
                int neighbor = s.Tags[i];
                if (neighbor >= 0 && inCity.Contains(neighbor)) continue;
                next[Key(s.Points[i])] = (s.Points[i], s.EdgeEnd(i));
            }
        }

        var loop = new List<Vec2>();
        if (next.Count == 0) return loop;

        VKey start = next.Keys.OrderBy(k => k.X).ThenBy(k => k.Y).First();
        VKey cur = start;
        while (next.TryGetValue(cur, out var seg))
        {
            loop.Add(seg.from);
            cur = Key(seg.to);
            if (cur.Equals(start) || loop.Count > next.Count) break;
        }
        return loop;
    }

    private static List<Vec2> PickGates(List<Vec2> wall, int count, Rng rng)
    {
        var gates = new List<Vec2>();
        if (count <= 0) return gates;
        int step = wall.Count / count;
        int offset = rng.NextInt(Math.Max(step, 1));
        for (int k = 0; k < count; k++)
            gates.Add(wall[(offset + k * step) % wall.Count]);
        return gates;
    }

    // ---- streets -------------------------------------------------------------------

    private static List<Street> BuildStreets(Patch[] cityPatches, List<Vec2> gates)
    {
        var verts = new Dictionary<VKey, Vec2>();
        var adj = new Dictionary<VKey, List<VKey>>();
        foreach (Patch patch in cityPatches)
        {
            ConvexPoly s = patch.Shape;
            for (int i = 0; i < s.Count; i++)
            {
                Vec2 a = s.Points[i], b = s.EdgeEnd(i);
                VKey ka = Key(a), kb = Key(b);
                verts[ka] = a;
                verts[kb] = b;
                Link(adj, ka, kb);
                Link(adj, kb, ka);
            }
        }

        Patch center = cityPatches[0];
        VKey plaza = center.Shape.Points
            .Select(Key)
            .OrderBy(k => Vec2.Distance(verts[k], center.Site))
            .First();

        // Dijkstra from the plaza; every gate walks its shortest path back to it.
        var dist = new Dictionary<VKey, double> { [plaza] = 0 };
        var prev = new Dictionary<VKey, VKey>();
        var open = new SortedSet<(double d, VKey k)>(Comparer<(double d, VKey k)>.Create(
            (x, y) =>
            {
                int c = x.d.CompareTo(y.d);
                if (c != 0) return c;
                c = x.k.X.CompareTo(y.k.X);
                return c != 0 ? c : x.k.Y.CompareTo(y.k.Y);
            }));
        open.Add((0, plaza));
        while (open.Count > 0)
        {
            var (d, k) = open.Min;
            open.Remove(open.Min);
            if (d > dist[k]) continue;
            foreach (VKey n in adj[k])
            {
                double nd = d + Vec2.Distance(verts[k], verts[n]);
                if (dist.TryGetValue(n, out double old) && old <= nd) continue;
                dist[n] = nd;
                prev[n] = k;
                open.Add((nd, n));
            }
        }

        var streets = new List<Street>();
        foreach (Vec2 gate in gates)
        {
            VKey k = Key(gate);
            if (!dist.ContainsKey(k)) continue;
            var path = new List<Vec2> { verts[k] };
            while (prev.TryGetValue(k, out VKey p))
            {
                k = p;
                path.Add(verts[k]);
            }
            streets.Add(new Street(path));
        }
        return streets;
    }

    private static void Link(Dictionary<VKey, List<VKey>> adj, VKey a, VKey b)
    {
        if (!adj.TryGetValue(a, out var list)) adj[a] = list = new List<VKey>();
        if (!list.Contains(b)) list.Add(b);
    }

    // ---- wards ---------------------------------------------------------------------

    private static void AssignWards(Patch[] cityPatches, Rng rng, bool cathedral)
    {
        // cityPatches is already sorted by distance from the centre.
        for (int i = 0; i < cityPatches.Length; i++)
        {
            double rank = (double)i / cityPatches.Length;
            WardType w;
            if (i == 0) w = WardType.Market;
            else if (i == 1 && cathedral) w = WardType.Cathedral;
            else if (i > 3 && rng.NextDouble() < 0.08) w = WardType.Park;
            else if (rank < 0.35) w = WardType.Merchant;
            else if (rank < 0.7) w = WardType.Craftsmen;
            else w = WardType.Slum;
            cityPatches[i].Ward = w;
        }
    }

    // ---- centrepiece ---------------------------------------------------------------

    private const double SquareMargin = 4.0; // clear ground kept between a centrepiece and the streets
    private const double StallLength = 3.2, StallDepth = 2.2, StallPitch = 5.0, RingPitch = 5.5;

    private static Landmark? PlaceLandmark(Patch square, CentrepieceKind kind, Rng rng)
    {
        if (kind == CentrepieceKind.None) return null;

        ConvexPoly shape = square.Shape;
        Vec2 c = shape.Centroid;
        double inradius = Enumerable.Range(0, shape.Count)
            .Min(i => -Vec2.Dot(shape.OutwardNormal(i), c - shape.Points[i]));
        double usable = Math.Max(3.0, inradius - SquareMargin);

        switch (kind)
        {
            case CentrepieceKind.Fountain:
                return new Landmark(kind, c, Math.Min(9.0, Math.Max(2.0, usable * 0.55)), new ConvexPoly[0]);
            case CentrepieceKind.Pyre:
                return new Landmark(kind, c, Math.Min(11.0, Math.Max(2.5, usable * 0.6)), new ConvexPoly[0]);
            case CentrepieceKind.ExecutionBlock:
                return new Landmark(kind, c, Math.Min(7.0, Math.Max(2.0, usable * 0.4)), new ConvexPoly[0]);
            default:
                return new Landmark(kind, c, usable, PlaceStalls(shape, c, usable, rng));
        }
    }

    /// <summary>
    /// Stalls on concentric rings around an open middle, each turned to face the centre, kept
    /// clear of the square's edge, with the odd gap so people can get through.
    /// </summary>
    private static List<ConvexPoly> PlaceStalls(ConvexPoly square, Vec2 c, double usable, Rng rng)
    {
        var edgeClearance = Enumerable.Repeat(SquareMargin * 0.6, square.Count).ToArray();
        ConvexPoly? area = square.Inset(edgeClearance);
        var stalls = new List<ConvexPoly>();
        if (area == null) return stalls;

        for (double ring = usable * 0.35; ring <= usable * 0.92; ring += RingPitch)
        {
            int count = (int)Math.Floor(2 * Math.PI * ring / StallPitch);
            double offset = rng.NextDouble() * 2 * Math.PI;
            for (int k = 0; k < count; k++)
            {
                if (k % 7 == 6 || rng.NextDouble() < 0.1) continue; // lanes and gaps

                double angle = offset + 2 * Math.PI * k / count;
                var radial = new Vec2(Math.Cos(angle), Math.Sin(angle));
                var tangent = new Vec2(-radial.Y, radial.X);
                Vec2 centre = c + radial * ring;

                // counter-clockwise: tangent runs along the stall's width, radial across its depth
                Vec2 a = tangent * (StallLength / 2), b = radial * (StallDepth / 2);
                var corners = new[] { centre - a - b, centre + a - b, centre + a + b, centre - a + b };
                if (!corners.All(v => Contains(area, v))) continue;
                stalls.Add(new ConvexPoly(corners, new[] { 0, 0, 0, 0 }));
            }
        }
        return stalls;
    }

    private static bool Contains(ConvexPoly poly, Vec2 p)
    {
        for (int i = 0; i < poly.Count; i++)
            if (Vec2.Cross(poly.Edge(i), p - poly.Points[i]) < 0) return false;
        return true;
    }

    // ---- size limit ----------------------------------------------------------------

    /// <summary>
    /// Removes houses until at most `cap` remain: outermost ward first (cityPatches is ordered by
    /// distance from the centre), farthest house first within it. The centre stays intact.
    /// </summary>
    private static void TrimBuildings(Patch[] cityPatches, int cap)
    {
        int total = cityPatches.Sum(x => x.Buildings.Count);
        for (int i = cityPatches.Length - 1; i >= 0 && total > Math.Max(0, cap); i--)
        {
            List<Building> houses = cityPatches[i].Buildings;
            foreach (Building b in houses.OrderByDescending(h => h.Footprint.Centroid.Length).ToList())
            {
                if (total <= Math.Max(0, cap)) break;
                houses.Remove(b);
                total--;
            }
        }
    }

    // ---- lots ----------------------------------------------------------------------

    private static double MinLotArea(WardType w)
    {
        switch (w)
        {
            case WardType.Cathedral: return double.MaxValue;
            case WardType.Merchant: return 130;
            case WardType.Craftsmen: return 90;
            default: return 60;
        }
    }

    private static void BuildLots(Patch patch, HashSet<(VKey, VKey)> streetEdges,
        HashSet<(VKey, VKey)> wallEdges, Rng rng)
    {
        if (patch.Ward == WardType.Market || patch.Ward == WardType.Park) return;

        ConvexPoly shape = patch.Shape;
        var distances = new double[shape.Count];
        for (int i = 0; i < shape.Count; i++)
        {
            var key = EdgeKey(shape.Points[i], shape.EdgeEnd(i));
            distances[i] = wallEdges.Contains(key) ? WallInset
                : streetEdges.Contains(key) ? StreetInset
                : AlleyInset;
        }

        // Re-tag: every outer block edge faces a street or alley, so it can hold a door.
        var block = new ConvexPoly(shape.Points, Enumerable.Repeat(OnStreetTag, shape.Count));
        ConvexPoly? inner = block.Inset(distances);
        if (inner == null) return;

        double minArea = MinLotArea(patch.Ward);
        if (patch.Ward == WardType.Cathedral)
        {
            Split(inner, minArea, 0, patch, rng);
            return;
        }

        foreach (ConvexPoly region in FrontageRegions(inner))
            Split(region, minArea, 0, patch, rng);
    }

    /// <summary>
    /// One region per block edge: the part of the block closer to that edge than to any other.
    /// Each region touches its street, so every lot cut from it has frontage.
    /// </summary>
    private static IEnumerable<ConvexPoly> FrontageRegions(ConvexPoly block)
    {
        for (int i = 0; i < block.Count; i++)
        {
            Vec2 ni = block.OutwardNormal(i);
            ConvexPoly? region = block;
            for (int j = 0; j < block.Count && region != null; j++)
            {
                if (j == i) continue;
                Vec2 nj = block.OutwardNormal(j);
                Vec2 normal = nj - ni;
                double len2 = Vec2.Dot(normal, normal);
                if (len2 < 1e-9) continue;

                // keep points whose inward distance to edge i is <= that to edge j
                double c = Vec2.Dot(nj, block.Points[j]) - Vec2.Dot(ni, block.Points[i]);
                region = region.Clip(normal * (c / len2), normal, 0);
            }
            if (region != null) yield return region;
        }
    }

    /// <summary>
    /// Lots are always cut perpendicular to their street frontage, so both halves keep a piece
    /// of it. A lot with no frontage could not have a reachable door and is never produced.
    /// Lots deeper than <see cref="MaxLotDepth"/> are trimmed; the rest of the block stays yard.
    /// </summary>
    private static void Split(ConvexPoly lot, double minArea, int depth, Patch patch, Rng rng)
    {
        int front = FrontEdge(lot);
        if (front < 0) return;

        if (minArea != double.MaxValue)
        {
            ConvexPoly? trimmed = TrimDepth(lot, front);
            if (trimmed == null) return;
            lot = trimmed;
            front = FrontEdge(lot);
            if (front < 0) return;
        }

        bool small = lot.Area < minArea * (1 + 0.6 * rng.NextDouble());
        // halves must still have MinFrontage left once SideGap is taken off both ends
        bool narrow = lot.EdgeLength(front) < 2 * (MinFrontage + 2 * SideGap);
        if (small || narrow || depth > 8)
        {
            Emit(lot, patch);
            return;
        }

        double t = 0.4 + 0.2 * rng.NextDouble();
        Vec2 cut = Vec2.Lerp(lot.Points[front], lot.EdgeEnd(front), t);
        Vec2 normal = lot.Edge(front).Normalized().Rotated((rng.NextDouble() - 0.5) * 0.5);

        ConvexPoly? a = lot.Clip(cut, normal, 0);
        ConvexPoly? b = lot.Clip(cut, -normal, 0);
        if (a == null || b == null)
        {
            Emit(lot, patch);
            return;
        }
        Split(a, minArea, depth + 1, patch, rng);
        Split(b, minArea, depth + 1, patch, rng);
    }

    /// <summary>How much of each side next to the apex is cut away when a triangle is made into a quadrilateral.</summary>
    private const double ApexCut = 0.4;

    /// <summary>
    /// Turns a triangle into a quadrilateral by cutting off the corner opposite its street-facing
    /// edge, so the front edge (and the door on it) is untouched. Returns null if it has no front.
    /// </summary>
    private static ConvexPoly? CutApex(ConvexPoly triangle)
    {
        int front = FrontEdge(triangle);
        if (front < 0 || triangle.Count != 3) return front < 0 ? null : triangle;

        // front edge runs a -> b; c is the apex opposite it
        Vec2 a = triangle.Points[front];
        Vec2 b = triangle.Points[(front + 1) % 3];
        Vec2 c = triangle.Points[(front + 2) % 3];

        Vec2 p = c + (b - c) * ApexCut;  // on the side b -> c, near c
        Vec2 q = c + (a - c) * ApexCut;  // on the side c -> a, near c

        // counter-clockwise a, b, p, q: the sides b-p and q-a keep the tags of the sides they came from
        return new ConvexPoly(
            new[] { a, b, p, q },
            new[] { triangle.Tags[front], triangle.Tags[(front + 1) % 3], 0, triangle.Tags[(front + 2) % 3] });
    }

    /// <summary>Longest edge facing a street or alley, or -1 if the lot is landlocked.</summary>
    private static int FrontEdge(ConvexPoly lot)
    {
        int best = -1;
        double bestLen = -1;
        for (int i = 0; i < lot.Count; i++)
        {
            if (lot.Tags[i] != OnStreetTag) continue;
            double len = lot.EdgeLength(i);
            if (len > bestLen) { bestLen = len; best = i; }
        }
        return best;
    }

    private static ConvexPoly? TrimDepth(ConvexPoly lot, int front)
    {
        Vec2 inward = -lot.OutwardNormal(front);
        double depth = lot.Points.Max(p => Vec2.Dot(inward, p - lot.Points[front]));
        if (depth <= MaxLotDepth) return lot;
        return lot.Clip(lot.Points[front] + inward * MaxLotDepth, inward, 0);
    }

    /// <summary>
    /// Pulls the sides shared with neighbouring lots (and the yard) in by <see cref="SideGap"/>, so
    /// houses stand apart with a narrow walkway between them instead of sharing walls.
    /// </summary>
    private static void Emit(ConvexPoly lot, Patch patch)
    {
        var gaps = Enumerable.Range(0, lot.Count)
            .Select(i => lot.Tags[i] == OnStreetTag ? 0.0 : SideGap)
            .ToArray();
        ConvexPoly? house = lot.Inset(gaps);
        if (house == null) return;

        // no triangular houses: they come from corner pieces of a block
        if (house.Count < 4)
        {
            house = CutApex(house);
            if (house == null) return;
        }

        int door = FrontEdge(house);
        if (door < 0 || house.Area < 12 || house.EdgeLength(door) < MinFrontage) return;
        patch.Buildings.Add(new Building(patch.Index * 1000 + patch.Buildings.Count, house, door, patch.Ward));
    }

    // ---- vertex keys ---------------------------------------------------------------

    // Voronoi cells are computed independently, so shared corners differ by float noise.
    private readonly struct VKey : IEquatable<VKey>, IComparable<VKey>
    {
        public readonly long X;
        public readonly long Y;
        public VKey(long x, long y) { X = x; Y = y; }
        public bool Equals(VKey o) => X == o.X && Y == o.Y;
        public override bool Equals(object? obj) => obj is VKey k && Equals(k);
        public override int GetHashCode() => (X, Y).GetHashCode();
        public int CompareTo(VKey o) => X != o.X ? X.CompareTo(o.X) : Y.CompareTo(o.Y);
    }

    private static VKey Key(Vec2 v) => new VKey((long)Math.Round(v.X * 100), (long)Math.Round(v.Y * 100));

    private static (VKey, VKey) EdgeKey(Vec2 a, Vec2 b)
    {
        VKey ka = Key(a), kb = Key(b);
        return ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka);
    }
}
