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
    private const double SideGap = 1.4;

    /// <summary>Average houses per ward at the default settings; used to guess a ward count for a target size.</summary>
    private const int TypicalBuildingsPerWard = 18;

    /// <summary>Slum lots are cut about this small (square metres) so the dwellings can be small and many.</summary>
    private const double SlumLotArea = 34;

    /// <summary>A slum dwelling is at most this wide and deep, plus up to <see cref="SlumSizeSpread"/> more, in metres.</summary>
    private const double SlumMaxWidth = 4.2, SlumMaxDepth = 3.6, SlumSizeSpread = 1.6;

    /// <summary>Generates a walled city.</summary>
    /// <param name="p">Size, seed and options.</param>
    /// <returns>The city, in a flat 2D plane in metres with the central square at the origin.</returns>
    /// <remarks>Determinism: the same <paramref name="p"/> always gives the same city on every machine (all randomness comes from <see cref="Rng"/>). Thread-safety: pure.</remarks>
    public static City Generate(CityParams p)
    {
        if (p.CityPatchCount is int fixedWards) return Build(p, fixedWards, p.MaxBuildings);
        if (p.MaxBuildings is not int max) return Build(p, CityParams.DefaultWardCount, null);

        // Find the smallest number of wards that holds `max` houses (Build trims any excess): start from a guess, grow while short,
        // then shrink while still enough. The excess is trimmed from the outermost ward first, so a city that is too big would lose its
        // slums; the smallest sufficient city keeps its outskirts.
        int limit = Math.Max(3, p.SiteCount / 3);
        int wards = Math.Min(limit, Math.Max(3, (int)Math.Ceiling(max / (double)TypicalBuildingsPerWard)));
        City city = Build(p, wards, max);
        while (BuildingCount(city) < max && wards < limit)
        {
            int missing = max - BuildingCount(city);
            wards = Math.Min(limit, wards + Math.Max(1, missing / TypicalBuildingsPerWard));
            city = Build(p, wards, max);
        }

        while (wards > 3)
        {
            City smaller = Build(p, wards - 1, max);
            if (BuildingCount(smaller) < max)
            {
                break;
            }

            wards--;
            city = smaller;
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

        // after the trim, so a service building is never trimmed away; its own random stream, so it never moves a house
        ServiceAssigner.Assign(cityPatches, patches, wall, gates, p.Services, Rng.ForKey((ulong)(uint)p.Seed, "city", "services"));

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
            case WardType.Slum: return SlumLotArea;
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
            Emit(lot, patch, rng);
            return;
        }

        double t = 0.4 + 0.2 * rng.NextDouble();
        Vec2 cut = Vec2.Lerp(lot.Points[front], lot.EdgeEnd(front), t);
        Vec2 normal = lot.Edge(front).Normalized().Rotated((rng.NextDouble() - 0.5) * 0.5);

        ConvexPoly? a = lot.Clip(cut, normal, 0);
        ConvexPoly? b = lot.Clip(cut, -normal, 0);
        if (a == null || b == null)
        {
            Emit(lot, patch, rng);
            return;
        }
        Split(a, minArea, depth + 1, patch, rng);
        Split(b, minArea, depth + 1, patch, rng);
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
    private static void Emit(ConvexPoly lot, Patch patch, Rng rng)
    {
        var gaps = Enumerable.Range(0, lot.Count)
            .Select(i => lot.Tags[i] == OnStreetTag ? 0.0 : SideGap)
            .ToArray();
        ConvexPoly? lotInner = lot.Inset(gaps);
        if (lotInner == null) return;

        // Every building has a rectangular floor plan: the biggest rectangle that fits the lot, with its front on the street edge.
        bool slum = patch.Ward == WardType.Slum;
        ConvexPoly? house = slum
            ? FitRectangle(lotInner, SlumMaxWidth + (SlumSizeSpread * rng.NextDouble()), SlumMaxDepth + (SlumSizeSpread * rng.NextDouble()))
            : FitRectangle(lotInner, double.MaxValue, double.MaxValue);
        if (house == null) return;

        int door = 0; // FitRectangle makes edge 0 the front
        if (house.Area < MinHouseArea || house.EdgeLength(door) < MinFrontage) return;
        patch.Buildings.Add(new Building(patch.Index * 1000 + patch.Buildings.Count, house, door, patch.Ward));
    }

    /// <summary>Smallest floor area a house may have, in square metres.</summary>
    private const double MinHouseArea = 10;

    /// <summary>Smallest depth a building may have, in metres.</summary>
    private const double MinDepth = 3.0;

    /// <summary>Depth steps tried when looking for the biggest rectangle, in metres.</summary>
    private const double DepthStep = 0.25;

    /// <summary>
    /// The largest rectangle inside a convex polygon that has one side on the polygon's street-facing front edge. Written in
    /// (u, v) with u along the front edge and v inward: the polygon is convex, so a rectangle from v = 0 to v = d fits exactly when
    /// it fits at both v = 0 and v = d. Returns null when the lot is too shallow or narrow. Edge 0 of the result is the front. The rectangle is
    /// no wider than <paramref name="maxWidth"/> and no deeper than <paramref name="maxDepth"/> (slum dwellings are small).
    /// </summary>
    private static ConvexPoly? FitRectangle(ConvexPoly lot, double maxWidth, double maxDepth)
    {
        int front = FrontEdge(lot);
        if (front < 0) return null;

        Vec2 origin = lot.Points[front];
        Vec2 u = lot.Edge(front).Normalized();
        Vec2 v = new Vec2(-u.Y, u.X); // inward: the polygon is counter-clockwise, so its inside is left of every edge
        double polygonDepth = lot.Points.Max(p => Vec2.Dot(p - origin, v));

        (double lo0, double hi0) = Span(lot, origin, u, v, 0);
        double bestArea = 0, bestLo = 0, bestHi = 0, bestDepth = 0;
        for (double d = MinDepth; d <= Math.Min(maxDepth, polygonDepth) + 1e-9; d += DepthStep)
        {
            (double lo, double hi) = Span(lot, origin, u, v, Math.Min(d, polygonDepth));
            double left = Math.Max(lo0, lo), right = Math.Min(hi0, hi);
            double width = right - left;
            if (width > maxWidth)
            {
                // A size limit: keep the rectangle centred in the room the lot gives it.
                left += (width - maxWidth) / 2;
                right = left + maxWidth;
                width = maxWidth;
            }

            if (width < MinFrontage) continue;
            double area = width * d;
            if (area > bestArea) { bestArea = area; bestLo = left; bestHi = right; bestDepth = d; }
        }

        if (bestArea <= 0) return null;
        Vec2 a = origin + u * bestLo, b = origin + u * bestHi;
        int tag = lot.Tags[front];
        return new ConvexPoly(new[] { a, b, b + v * bestDepth, a + v * bestDepth }, new[] { tag, 0, 0, 0 });
    }

    // The u-range of the polygon on the line at inward depth `depth`.
    private static (double Lo, double Hi) Span(ConvexPoly poly, Vec2 origin, Vec2 u, Vec2 v, double depth)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i < poly.Count; i++)
        {
            Vec2 p = poly.Points[i] - origin, q = poly.EdgeEnd(i) - origin;
            double pv = Vec2.Dot(p, v) - depth, qv = Vec2.Dot(q, v) - depth;
            if (pv * qv > 0) continue; // the edge does not cross the line
            double t = Math.Abs(pv - qv) < 1e-12 ? 0 : pv / (pv - qv);
            double x = Vec2.Dot(p, u) + (t * (Vec2.Dot(q, u) - Vec2.Dot(p, u)));
            lo = Math.Min(lo, x);
            hi = Math.Max(hi, x);
            if (Math.Abs(pv - qv) < 1e-12)
            {
                lo = Math.Min(lo, Math.Min(Vec2.Dot(p, u), Vec2.Dot(q, u)));
                hi = Math.Max(hi, Math.Max(Vec2.Dot(p, u), Vec2.Dot(q, u)));
            }
        }

        return (lo, hi);
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
