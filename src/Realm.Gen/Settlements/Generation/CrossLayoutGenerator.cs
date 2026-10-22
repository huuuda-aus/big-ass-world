using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Random;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Generation;

/// <summary>
/// The cross layout: two main streets cross at a square like a crossroads. Each of the four arms runs as far as the houses need (the
/// arms differ in length), with two rows of houses along each side: the first faces the street, the second faces a lane behind it. The
/// wall is the plus-shaped outline round the arms, with a gate at the end of each arm, so it hugs the outermost houses.
/// </summary>
/// <remarks>
/// Produces the same <see cref="City"/> as <see cref="CityGenerator"/> (strips of houses play the part of wards), so services, meshes,
/// roads and the game need no special case. Determinism: all randomness comes from <c>Rng.ForKey(seed, "cross", ...)</c>. Thread-safety: pure.
/// </remarks>
internal static class CrossLayoutGenerator
{
    private const int Arms = 4;
    private const double StreetOffset = 5;
    private const double Lane = 2.5;
    private const double RichRow = 10, SlumRow = 6.5;
    private const double WallMargin = 6;
    private const double RichMinExtension = 22, HumbleMinExtension = 8; // an arm is at least this much longer than the square: a few lots
    private const double BuildFalloff = 0.7;   // the chance of a house on a lot in row 1 falls by this much from the square to the end of the arm
    private const double SecondRowReach = 0.55; // row 2 thins out and is gone at this share of the arm's length
    private const double SideGap = 1.4;
    private const double MinHouseWidth = 3.5, MinHouseDepth = 3.0;
    private const double RichLotMin = 8.5, RichLotMax = 11.5;
    private const double SlumLotMin = 6.5, SlumLotMax = 8;
    private const double RichDepthMin = 7.5, SlumDepthMin = 3.6, SlumDepthMax = 5.2;
    private const double MerchantReach = 0.35, CraftsmenReach = 0.7;
    private const double HousesPerMetre = 0.2;
    private const int DefaultHouses = 120;
    private const int Rounds = 8;
    private const double FarmDepth = 26;
    private const double FarmGap = 3;
    private const double CathedralWidth = 24, CathedralDepth = 18;

    // The sizes that depend on how grand the settlement is: a hamlet (humble) has shallow rows of small dwellings, so a smaller square.
    private readonly record struct Metrics(double SquareHalf, double StripDepth, double MinArm)
    {
        public static Metrics For(CityParams p)
        {
            double row = p.Humble ? SlumRow : RichRow;
            double strip = (2 * row) + Lane;
            double square = StreetOffset + strip + 0.5; // the strips of two arms never overlap at the corner
            return new Metrics(square, strip, square + (p.Humble ? HumbleMinExtension : RichMinExtension));
        }
    }

    /// <summary>Generates a cross-layout settlement.</summary>
    /// <param name="p">Seed, size cap, humble flag, centrepiece, services and water.</param>
    /// <returns>The settlement in plan coordinates (the central square at the origin, arms along the axes).</returns>
    public static City Generate(CityParams p)
    {
        int cap = p.MaxBuildings ?? DefaultHouses;
        Metrics m = Metrics.For(p);
        Rng rng = Rng.ForKey((ulong)(uint)p.Seed, "cross");
        var shares = new double[Arms];
        for (int k = 0; k < Arms; k++)
        {
            shares[k] = 0.5 + rng.NextDouble(); // the arms differ: up to three to one
        }

        // Find the longest arms that keep the houses under the cap: start from a guess, scale by the miss, keep the best that fits.
        City? best = null;
        int bestCount = -1;
        double total = (Arms * m.SquareHalf) + (cap / HousesPerMetre);
        for (int round = 0; round < Rounds; round++)
        {
            double spare = Math.Max(0, total - (Arms * m.MinArm));
            double sum = shares.Sum();
            double[] lengths = shares.Select(s => m.MinArm + (spare * s / sum)).ToArray();
            City city = Build(p, lengths, withWater: false);
            int count = city.CityPatches.Sum(x => x.Buildings.Count);
            if (count <= cap && count > bestCount)
            {
                best = city;
                bestCount = count;
                if (count >= cap * 0.93)
                {
                    break;
                }
            }

            total *= cap / (double)Math.Max(count, 1) * (count > cap ? 0.97 : 1.0);
            if (spare <= 0 && count > cap)
            {
                break; // the shortest arms already hold more than the cap
            }
        }

        if (best is null)
        {
            // The shortest cross is over the cap: trim its outermost houses.
            City trimmed = Build(p, Enumerable.Repeat(m.MinArm, Arms).ToArray(), withWater: true);
            TrimFarthest(trimmed.CityPatches, cap);
            return trimmed;
        }

        // The dry plan decided the size; build it once more with the water known (the same outline, minus the houses in water).
        return p.Water is null ? best : BuildLike(p, best);
    }

    // Rebuilds the plan of an already sized city with the water known.
    private static City BuildLike(CityParams p, City dry)
    {
        // The arm lengths are recoverable from the wall: the far gate of each arm sits at the arm's end.
        double[] lengths = new double[Arms];
        foreach (Vec2 gate in dry.Gates)
        {
            int arm = ArmOf(gate);
            lengths[arm] = Math.Max(lengths[arm], Vec2.Dot(gate, Direction(arm)) - EndMargin);
        }

        return Build(p, lengths, withWater: true);
    }

    private const double EndMargin = WallMargin; // the gate stands this far beyond the end of its arm

    private static int ArmOf(Vec2 v) => Math.Abs(v.X) > Math.Abs(v.Y) ? (v.X > 0 ? 0 : 2) : (v.Y > 0 ? 1 : 3);

    private static Vec2 Direction(int arm) => arm switch
    {
        0 => new Vec2(1, 0),
        1 => new Vec2(0, 1),
        2 => new Vec2(-1, 0),
        _ => new Vec2(0, -1),
    };

    private static City Build(CityParams p, double[] arms, bool withWater)
    {
        Metrics m = Metrics.For(p);
        double SquareHalf = m.SquareHalf, StripDepth = m.StripDepth;
        IWaterSampler? water = withWater ? p.Water : null;
        Rng rng = Rng.ForKey((ulong)(uint)p.Seed, "cross", "lots");
        double reach = arms.Max();
        var patches = new List<Patch>();

        // Patch 0: the central square.
        var square = ConvexPoly.Rect(-SquareHalf, -SquareHalf, SquareHalf, SquareHalf, 0);
        var market = new Patch(0, new Vec2(0, 0), square, Array.Empty<int>()) { Ward = WardType.Market };
        patches.Add(market);

        // Strips of houses: one patch per arm, side and ward zone.
        var stripOfArm = new List<int>[Arms];
        var strips = new List<Patch>();
        for (int arm = 0; arm < Arms; arm++)
        {
            stripOfArm[arm] = new List<int>();
            Vec2 d = Direction(arm);
            Vec2 n = new(-d.Y, d.X);
            double length = arms[arm];
            foreach (int side in new[] { 1, -1 })
            {
                double[] bounds = { SquareHalf, Math.Min(length, Math.Max(SquareHalf, reach * MerchantReach)), Math.Min(length, Math.Max(SquareHalf, reach * CraftsmenReach)), length };
                for (int zone = 0; zone < 3; zone++)
                {
                    double u0 = bounds[zone], u1 = bounds[zone + 1];
                    if (u1 - u0 < 1)
                    {
                        continue;
                    }

                    WardType ward = p.Humble ? WardType.Slum : zone == 0 ? WardType.Merchant : zone == 1 ? WardType.Craftsmen : WardType.Slum;
                    ConvexPoly shape = Strip(d, n, side, u0, u1, StreetOffset, StripDepth + StreetOffset);
                    var patch = new Patch(patches.Count, shape.Centroid, shape, Array.Empty<int>()) { Ward = ward };
                    patches.Add(patch);
                    strips.Add(patch);
                    stripOfArm[arm].Add(patch.Index);
                }
            }
        }

        // A cathedral on the first arm's left side, next to the square.
        bool cathedral = p.Cathedral && arms[0] > SquareHalf + CathedralWidth + 6;
        double cathedralEnd = cathedral ? SquareHalf + CathedralWidth + 3 : SquareHalf;
        if (cathedral)
        {
            Patch host = strips.First(s => s.Shape.Centroid.Y > 0 && s.Shape.Centroid.X > 0 && s.Index <= 4 * 3);
            AddHouse(host, Rect(Direction(0), new Vec2(0, 1), 1, SquareHalf + 1.5, SquareHalf + 1.5 + CathedralWidth, StreetOffset, CathedralDepth), WardType.Cathedral, host);
        }

        // Lots along every strip: row 1 faces the street, row 2 faces the lane behind it.
        foreach (Patch patch in strips)
        {
            (int arm, int side) = ArmAndSide(patch);
            Vec2 d = Direction(arm);
            Vec2 n = new(-d.Y, d.X);
            bool slum = patch.Ward == WardType.Slum;
            double rowDepth = slum ? SlumRow : RichRow;
            double u0 = patch.Shape.Points.Min(v => Vec2.Dot(v, d));
            double u1 = patch.Shape.Points.Max(v => Vec2.Dot(v, d));
            bool hostOfCathedral = cathedral && arm == 0 && side == 1 && patch.Ward == WardType.Merchant;
            double start = hostOfCathedral ? Math.Max(u0, cathedralEnd) : u0;
            for (int row = 0; row < 2; row++)
            {
                double front = StreetOffset + (row * (rowDepth + Lane));
                double u = start + SideGap;
                while (true)
                {
                    double lot = slum ? SlumLotMin + (rng.NextDouble() * (SlumLotMax - SlumLotMin)) : RichLotMin + (rng.NextDouble() * (RichLotMax - RichLotMin));
                    if (u - SideGap + lot > u1 + 1e-6)
                    {
                        break;
                    }

                    double width = lot - (2 * SideGap);
                    double depth = slum ? SlumDepthMin + (rng.NextDouble() * (SlumDepthMax - SlumDepthMin)) : RichDepthMin + (rng.NextDouble() * (rowDepth - RichDepthMin));
                    // The further from the crossroads, the fewer the houses: row 1 thins out along the arm and row 2 is gone by
                    // SecondRowReach of its length. The roll is always drawn so the random sequence does not depend on the outcome.
                    double along = Math.Clamp((u - SquareHalf) / Math.Max(1, arms[arm] - SquareHalf), 0, 1);
                    double chance = row == 0 ? 1 - (BuildFalloff * along) : Math.Clamp(1 - (along / SecondRowReach), 0, 1);
                    bool built = rng.NextDouble() < chance;
                    if (built && width >= MinHouseWidth && depth >= MinHouseDepth)
                    {
                        AddHouse(patch, Rect(d, n, side, u, u + width, front, depth), patch.Ward, patch);
                    }

                    u += lot;
                }
            }
        }

        // The outline round the arms, with a gate in the middle of each arm's end.
        double h = StreetOffset + StripDepth + WallMargin;
        double[] end = arms.Select(a => a + WallMargin).ToArray();
        var wall = new List<Vec2>
        {
            new(end[0], -h), new(end[0], 0), new(end[0], h),
            new(h, h), new(h, end[1]),
            new(0, end[1]), new(-h, end[1]),
            new(-h, h), new(-end[2], h), new(-end[2], 0), new(-end[2], -h),
            new(-h, -h), new(-h, -end[3]), new(0, -end[3]), new(h, -end[3]),
            new(h, -h),
        };
        var gates = new List<Vec2> { new(end[0], 0), new(0, end[1]), new(-end[2], 0), new(0, -end[3]) };
        var streets = Enumerable.Range(0, Arms).Select(k => new Street(new[] { new Vec2(0, 0), Direction(k) * end[k] })).ToList();

        if (water is not null)
        {
            CityGenerator.RemoveBuildingsInWater(strips, water, p.WaterBufferMeters);
        }

        // Farmland beside and beyond the arms, for the farm buildings outside the wall.
        var all = new List<Patch>(patches);
        for (int arm = 0; arm < Arms; arm++)
        {
            Vec2 d = Direction(arm);
            Vec2 n = new(-d.Y, d.X);
            foreach (int side in new[] { 1, -1 })
            {
                double v0 = h + FarmGap, v1 = h + FarmGap + FarmDepth;
                ConvexPoly field = Strip(d, n, side, SquareHalf + 8, arms[arm], v0, v1);
                all.Add(new Patch(all.Count, field.Centroid, field, stripOfArm[arm]));
            }

            ConvexPoly cap = Strip(d, n, 1, end[arm] + FarmGap, end[arm] + FarmGap + FarmDepth, -h, h);
            all.Add(new Patch(all.Count, cap.Centroid, cap, stripOfArm[arm]));
        }

        Patch[] cityPatches = patches.ToArray();
        ServiceAssigner.Assign(cityPatches, all, wall, gates, p.Services, Rng.ForKey((ulong)(uint)p.Seed, "cross", "services"), water, p.WaterBufferMeters);
        Landmark? landmark = CityGenerator.PlaceLandmark(market, p.Centre, Rng.ForKey((ulong)(uint)p.Seed, "cross", "landmark"));
        IReadOnlyList<CityBridge> bridges = water is null ? Array.Empty<CityBridge>() : CityBridgeFinder.Find(streets, water, p.MaxBridges);
        return new City(p.Seed, all, cityPatches, wall, gates, streets, landmark, bridges);
    }

    // A rectangle along arm direction d: u from u0 to u1 and lateral offset (on the given side) from v0 to v1.
    private static ConvexPoly Strip(Vec2 d, Vec2 n, int side, double u0, double u1, double v0, double v1)
    {
        Vec2 P(double u, double v) => (d * u) + (n * (side * v));
        var points = new[] { P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1) };
        return Counterclockwise(points, new[] { 0, 0, 0, 0 });
    }

    // A house footprint whose front is the first edge, facing the street (toward smaller lateral offsets).
    private static ConvexPoly Rect(Vec2 d, Vec2 n, int side, double u0, double u1, double front, double depth)
    {
        Vec2 P(double u, double v) => (d * u) + (n * (side * v));
        Vec2 a = P(u0, front), b = P(u1, front), c = P(u1, front + depth), e = P(u0, front + depth);
        double area = Signed(new[] { a, b, c, e });
        return area >= 0
            ? new ConvexPoly(new[] { a, b, c, e }, new[] { 1, 0, 0, 0 })
            : new ConvexPoly(new[] { b, a, e, c }, new[] { 1, 0, 0, 0 }); // mirrored side: reverse, so the front stays edge 0
    }

    private static ConvexPoly Counterclockwise(Vec2[] points, int[] tags) =>
        Signed(points) >= 0 ? new ConvexPoly(points, tags) : new ConvexPoly(points.Reverse(), tags);

    private static double Signed(Vec2[] p)
    {
        double sum = 0;
        for (int i = 0; i < p.Length; i++)
        {
            Vec2 a = p[i], b = p[(i + 1) % p.Length];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return sum;
    }

    private static void AddHouse(Patch patch, ConvexPoly footprint, WardType ward, Patch owner) =>
        patch.Buildings.Add(new Building((owner.Index * 1000) + patch.Buildings.Count, footprint, 0, ward));

    // Removes the houses farthest from the square until at most `cap` remain.
    private static void TrimFarthest(IEnumerable<Patch> patches, int cap)
    {
        var all = patches.SelectMany(p => p.Buildings.Select(b => (Patch: p, Building: b))).ToList();
        foreach ((Patch patch, Building building) in all.OrderByDescending(x => x.Building.Footprint.Centroid.Length).ThenBy(x => x.Building.Id).Take(Math.Max(0, all.Count - cap)))
        {
            patch.Buildings.Remove(building);
        }
    }

    // Which arm and side a strip patch lies along (from its centre).
    private static (int Arm, int Side) ArmAndSide(Patch patch)
    {
        Vec2 c = patch.Shape.Centroid;
        int arm = ArmOf(c);
        Vec2 d = Direction(arm);
        Vec2 n = new(-d.Y, d.X);
        return (arm, Vec2.Dot(c, n) >= 0 ? 1 : -1);
    }
}
