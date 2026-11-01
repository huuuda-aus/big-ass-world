using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>
/// Turns the city plan into meshes: the wall (stone with towers, or a wooden palisade, by the settlement's fortification) and the centrepiece. There is no ground or street mesh: houses stand on the natural terrain (roads and paths come later). Ported from
/// the three.js viewer; the gates are an improvement: the viewer left a whole wall edge open, here each gate is a gap of
/// <see cref="GateWidthMeters"/> flanked by two gate towers.
/// </summary>
/// <remarks>Coordinates: the city's local frame (plan x is X, plan y is Z, height is Y). Determinism: pure. Thread-safety: pure.</remarks>
public static class CityMesher
{
    /// <summary>Height of the wall in metres.</summary>
    public const double WallHeight = 7;

    /// <summary>Thickness of the wall in metres.</summary>
    public const double WallThickness = 1.6;

    /// <summary>Width of a gate opening in metres.</summary>
    public const double GateWidthMeters = 8;

    /// <summary>Width of a street in metres.</summary>
    public const double StreetWidthMeters = 6;

    /// <summary>Longest piece of wall built in one go, in metres; shorter pieces follow the slope more closely.</summary>
    private const double DrapeMeters = 8;

    /// <summary>How far the wall and towers are sunk into the ground, in metres, so no gap shows on slopes.</summary>
    private const double WallSink = 2.0;

    /// <summary>How far a wall cut by water runs on into the water, in metres.</summary>
    private const double WaterOverlapMeters = 2.5;

    /// <summary>Spacing of the water tests along a wall, in metres.</summary>
    private const double WaterStepMeters = 2;

    private const float LogMinRadius = 0.18f;
    private const float LogMaxRadius = 0.25f;
    private const double LogSpacingFactor = 1.55; // centre spacing as a multiple of the radius; under 2 means neighbours overlap
    private const float LogTaper = 0.9f;
    private const int LogSides = 8;
    private const int LogRows = 2;
    private const double LogRowOffsetMeters = 0.2;
    private const double LogRowStagger = 0.2;
    private const double LogEndOverlapMeters = 0.2;
    private const double LogSinkMeters = 0.6;
    private const double PalisadeHeight = 3.2;
    private const double PalisadeHeightSpread = 0.8;
    private const double WatchtowerSide = 3.4;
    // Also the roof's base height (the posts carry it) and, with WatchtowerPlatformHeight, the standing headroom
    // under it: 7.2 - (4.8 + 0.15) = 2.25 m, clear of the player's 1.8 m capsule with margin (it was 6.5 m, giving
    // only 1.55 m, which the player's head poked 0.25 m into: WorldScene.BodyHeight, not referenced directly since
    // Realm.Gen stays Godot-free, but the clearance here must stay ahead of it if that ever changes).
    private const double WatchtowerHeight = 7.2;
    private const double WatchtowerPlatformHeight = 4.8;
    // The platform's centre sits this far inside the wall line: half the platform's own width (1.7 m) less a
    // small overlap (0.2 m), so the front edge sits flush with, and a little past, the wall line instead of
    // leaving a gap. No post reaches the ground on the front side (the wall itself stands where one would); only
    // the back two posts, and the two diagonal braces tying the platform to the wall, need to clear the fence (the
    // front roof posts start above the platform, see Watchtower and Roof). A starting point only: PlaceWatchtower
    // nudges further in by WatchtowerNudgeMeters, up to WatchtowerNudgeSteps times, wherever this alone is not
    // enough for the back corners to clear.
    private const double WatchtowerInsetMeters = (WatchtowerSide / 2) - 0.2;
    private const double WatchtowerRoofOverhang = 0.3;
    private const double WatchtowerNudgeMeters = 0.3;
    private const int WatchtowerNudgeSteps = 20;
    private const double WatchtowerBraceWallHeight = 3.2;
    private const double WatchtowerRailHeight = 1.0; // waist-high, matching FoundationMesher's pier deck rail
    private const double WatchtowerRailSpacing = 1.4;
    private const float WatchtowerRailPost = 0.08f;
    // Half the diameter of a palisade log (LogMinRadius/LogMaxRadius 0.18-0.25 m average to a 0.215 m radius; half
    // that diameter is half that radius).
    private const float WatchtowerRailLogRadius = (LogMinRadius + LogMaxRadius) / 4;
    private static readonly double[] WatchtowerRailCourseHeights = RailCourseHeights();
    private const double LadderHoleSide = 1.0;
    private const double LadderRailHalfWidth = 0.35;
    // How far inward of the ladder's own plan position the top landing point sits: half the hole (0.5 m) plus the
    // player's body radius (0.4 m, see WatchtowerHeight's remarks) plus a margin, so arriving at the top does not
    // land in the hole itself, which has no floor.
    private const double LadderTopLandingInsetMeters = 1.05;
    private const double LadderRungSpacing = 0.3;
    private const double TowerRadius = 2.8;
    private const double GateTowerRadius = 2.4;
    private const double TowerRise = 3;
    private const double GateTowerRise = 4;
    private const int TowerSides = 10;
    private const double SameCorner = 0.01;
    private const double MinLeafMeters = 1;
    private const double StoneLeafHeight = 4.8;
    private const double StoneLeafThickness = 0.45;
    private const double StoneLintelBase = 5.2;
    private const double StoneLintelHeight = 1.8;
    private const double PalisadeLeafHeight = 3.3;
    private const double PalisadeLeafThickness = 0.3;
    private const double PalisadeBeamHeight = 4.4;

    /// <summary>Builds the meshes of a city.</summary>
    /// <param name="city">The generated city.</param>
    /// <param name="citySeed">The seed the city was generated from.</param>
    /// <param name="ground">The ground under the city; level ground when null.</param>
    /// <param name="fortification">What protects the settlement (a stone wall by default).</param>
    /// <param name="water">Where the water is: the wall is cut where it would stand in water. Null for a dry site.</param>
    /// <returns>The meshes.</returns>
    public static CityMeshes Build(City city, int citySeed, IGroundSampler? ground = null, Fortification fortification = Fortification.Stone, IWaterSampler? water = null)
    {
        ArgumentNullException.ThrowIfNull(city);
        ground ??= FlatGround.Instance;
        double landmarkBase = city.Landmark is null ? 0 : ground.HeightAt(city.Landmark.Center.X, city.Landmark.Center.Y);
        (PropMesh? wall, IReadOnlyList<Ladder> ladders) = WallAndLadders(city, ground, fortification, water);
        return new CityMeshes(
            wall,
            city.Landmark is null ? null : LandmarkMesher.Build(city.Landmark, citySeed, ground, landmarkBase),
            landmarkBase,
            ladders);
    }

    /// <summary>Builds the wall, its towers and the gate towers, following the ground.</summary>
    /// <param name="city">The city.</param>
    /// <param name="ground">The ground under the city; level ground when null.</param>
    /// <param name="fortification">Stone wall with towers (default), wooden palisade with wooden gate towers, or none.</param>
    /// <param name="water">Where the water is. The wall stops at the water (a river or lake is the defence there), running a couple of metres into it, with a tower at each cut end. Null for a dry site.</param>
    /// <returns>The mesh, or null when the city has no wall or the fortification is <see cref="Fortification.None"/>.</returns>
    public static PropMesh? Wall(City city, IGroundSampler? ground = null, Fortification fortification = Fortification.Stone, IWaterSampler? water = null) =>
        WallAndLadders(city, ground, fortification, water).Mesh;

    // Like Wall, but also returns the watchtower ladders (a stone wall has none: its towers are reached only by
    // the wall-walk, not modelled yet). Separate from the public Wall so the many existing callers that only want
    // the mesh (tests included) are unaffected; Build is the one caller that needs both.
    private static (PropMesh? Mesh, IReadOnlyList<Ladder> Ladders) WallAndLadders(City city, IGroundSampler? ground, Fortification fortification, IWaterSampler? water)
    {
        ArgumentNullException.ThrowIfNull(city);
        ground ??= FlatGround.Instance;
        IReadOnlyList<Vec2> wall = city.Wall;
        if (wall.Count < 3 || fortification == Fortification.None)
        {
            return (null, Array.Empty<Ladder>());
        }

        if (fortification == Fortification.Palisade)
        {
            return Palisade(city, ground, water);
        }

        var b = new PropMeshBuilder();
        HashSet<int> gateVertices = Enumerable.Range(0, wall.Count).Where(i => city.Gates.Any(g => Vec2.Distance(g, wall[i]) < SameCorner)).ToHashSet();
        float height = (float)WallHeight;

        for (int i = 0; i < wall.Count; i++)
        {
            int next = (i + 1) % wall.Count;
            int previous = (i + wall.Count - 1) % wall.Count;
            Vec2 a = wall[i];
            Vec2 c = wall[next];
            double length = Vec2.Distance(a, c);
            Vec2 dir = (c - a).Normalized();

            // Gate vertices have no tower; the wall stops half a gate short of them and a gate tower stands on each side.
            double start = gateVertices.Contains(i) ? GateWidthMeters / 2 : 0;
            double end = length - (gateVertices.Contains(next) ? GateWidthMeters / 2 : 0);
            if (end - start > 0.5)
            {
                foreach ((double u0, double u1, bool cutStart, bool cutEnd) in LandRuns(a, dir, start, end, water))
                {
                    WallRun(b, ground, a + (dir * (u0 - (cutStart ? WaterOverlapMeters : 0))), a + (dir * (u1 + (cutEnd ? WaterOverlapMeters : 0))));
                    if (cutStart)
                    {
                        Tower(b, ground, a + (dir * (u0 - WaterOverlapMeters)), TowerRadius, height + (float)TowerRise);
                    }

                    if (cutEnd)
                    {
                        Tower(b, ground, a + (dir * (u1 + WaterOverlapMeters)), TowerRadius, height + (float)TowerRise);
                    }
                }
            }

            if (gateVertices.Contains(i))
            {
                Vec2 back = (wall[previous] - a).Normalized();
                Tower(b, ground, a + (dir * (GateWidthMeters / 2)), GateTowerRadius, height + (float)GateTowerRise);
                Tower(b, ground, a + (back * (GateWidthMeters / 2)), GateTowerRadius, height + (float)GateTowerRise);
                GateDoors(b, ground, a, dir, back, GateTowerRadius, stone: true);
            }
            else if (water is null || !water.IsWater(a.X, a.Y))
            {
                Tower(b, ground, a, TowerRadius, height + (float)TowerRise);
            }
        }

        return (b.Build("city.wall"), Array.Empty<Ladder>());
    }

    // The stretches of a wall edge (u along it from `a`) that stand on land. A stretch that ends because the water starts is flagged as
    // cut at that end. Without water the whole edge is one stretch.
    private static IEnumerable<(double Start, double End, bool CutStart, bool CutEnd)> LandRuns(Vec2 a, Vec2 dir, double start, double end, IWaterSampler? water)
    {
        if (water is null)
        {
            yield return (start, end, false, false);
            yield break;
        }

        int steps = Math.Max(1, (int)Math.Ceiling((end - start) / WaterStepMeters));
        double step = (end - start) / steps;
        double? runStart = null;
        bool cutStart = false;
        for (int k = 0; k < steps; k++)
        {
            double u = start + ((k + 0.5) * step);
            bool wet = water.IsWater(a.X + (dir.X * u), a.Y + (dir.Y * u));
            if (!wet && runStart is null)
            {
                runStart = start + (k * step);
                cutStart = k > 0;
            }
            else if (wet && runStart is not null)
            {
                yield return (runStart.Value, start + (k * step), cutStart, true);
                runStart = null;
            }
        }

        if (runStart is not null)
        {
            yield return (runStart.Value, end, cutStart, false);
        }
    }

    // ---- palisade ------------------------------------------------------------------------------------------------

    // A fence of round logs along the wall line with a gap at every gate, and a wooden watchtower just inside the
    // fence, either side of each gate.
    private static (PropMesh Mesh, IReadOnlyList<Ladder> Ladders) Palisade(City city, IGroundSampler ground, IWaterSampler? water)
    {
        var b = new PropMeshBuilder();
        var ladders = new List<Ladder>();
        IReadOnlyList<Vec2> wall = city.Wall;
        HashSet<int> gateVertices = Enumerable.Range(0, wall.Count).Where(i => city.Gates.Any(g => Vec2.Distance(g, wall[i]) < SameCorner)).ToHashSet();
        for (int i = 0; i < wall.Count; i++)
        {
            int next = (i + 1) % wall.Count;
            int previous = (i + wall.Count - 1) % wall.Count;
            Vec2 a = wall[i];
            Vec2 c = wall[next];
            double length = Vec2.Distance(a, c);
            Vec2 dir = (c - a).Normalized();
            double start = gateVertices.Contains(i) ? GateWidthMeters / 2 : 0;
            double end = length - (gateVertices.Contains(next) ? GateWidthMeters / 2 : 0);
            if (end - start > 0.5)
            {
                foreach ((double u0, double u1, bool cutStart, bool cutEnd) in LandRuns(a, dir, start, end, water))
                {
                    Logs(b, ground, a + (dir * (u0 - (cutStart ? WaterOverlapMeters : 0))), a + (dir * (u1 + (cutEnd ? WaterOverlapMeters : 0))));
                }
            }

            if (gateVertices.Contains(i))
            {
                Vec2 back = (wall[previous] - a).Normalized();
                double backLength = Vec2.Distance(a, wall[previous]);
                // The usual along-the-wall offset assumes the edge beside the gate reaches at least that far; a
                // short connecting segment near a tight ward corner can be much shorter, which would otherwise
                // place the "on wall" anchor past the real corner, off the edge PlaceWatchtower reasons about. If
                // the edge has no room at all for one (rare: a kink right beside the gate), skip it on that side;
                // the gate still has its other watchtower.
                if (length - 0.3 >= WatchtowerSide / 2 + 0.2)
                {
                    double alongA = Math.Min(GateWidthMeters / 2 + WatchtowerSide / 2, length - 0.3);
                    (Vec2 centre, Vec2 inward) = PlaceWatchtower(a + (dir * alongA), dir, wall);
                    ladders.Add(Watchtower(b, ground, centre, dir, inward));
                }

                if (backLength - 0.3 >= WatchtowerSide / 2 + 0.2)
                {
                    double alongB = Math.Min(GateWidthMeters / 2 + WatchtowerSide / 2, backLength - 0.3);
                    (Vec2 centre, Vec2 inward) = PlaceWatchtower(a + (back * alongB), -back, wall);
                    ladders.Add(Watchtower(b, ground, centre, -back, inward));
                }

                GateDoors(b, ground, a, dir, back, 0, stone: false);
            }
        }

        return (b.Build("city.palisade"), ladders);
    }

    // Upright round logs standing shoulder to shoulder: two staggered rows so no light shows between them, each log a little
    // different in radius and height, flat-topped, and sunk below the ground so slopes show no gap underneath.
    private static void Logs(PropMeshBuilder b, IGroundSampler ground, Vec2 from, Vec2 to)
    {
        foreach (Log log in LogsAlong(from, to))
        {
            double h = ground.HeightAt(log.At.X, log.At.Y);
            b.Tube(P(log.At, h - LogSinkMeters), P(log.At, h + log.Height), log.Radius, log.Radius * LogTaper, LogSides, SettlementPalette.Log(log.Hash));
        }
    }

    /// <summary>One upright log of a palisade: where it stands, how thick and how tall.</summary>
    internal readonly record struct Log(Vec2 At, float Radius, double Height, uint Hash);

    /// <summary>Lays the logs of one palisade run; neighbours in a row overlap, the second row is staggered behind the first, and the ends reach past the run so corners close.</summary>
    internal static IReadOnlyList<Log> LogsAlong(Vec2 from, Vec2 to)
    {
        var logs = new List<Log>();
        double length = Vec2.Distance(from, to);
        if (length < 1e-6)
        {
            return logs;
        }

        Vec2 dir = (to - from).Normalized();
        Vec2 across = new(-dir.Y, dir.X);
        for (int row = 0; row < LogRows; row++)
        {
            double along = row == 0 ? 0 : -LogRowStagger;
            double offset = row * LogRowOffsetMeters;
            while (along < length + LogEndOverlapMeters)
            {
                Vec2 at = from + (dir * along) + (across * offset);
                uint hash = unchecked((uint)((int)(at.X * 31) ^ (int)(at.Y * 17) ^ (row * 7919)) * 2654435761u);
                float radius = LogMinRadius + ((hash & 0xFF) / 255f * (LogMaxRadius - LogMinRadius));
                double height = PalisadeHeight + ((hash >> 8) % 100 / 100.0 * PalisadeHeightSpread) - (row * 0.15);
                logs.Add(new Log(at, radius, height, hash));
                along += radius * LogSpacingFactor; // closer than a diameter, so the round logs overlap
            }
        }

        return logs;
    }

    // A gate stands open: two leaves hinged at the sides of the opening and swung back along the passage, so nobody is blocked,
    // under a heavy lintel (stone, over the gate towers) or a log beam (palisade). `a` is the gate corner, `dir` and `back` the unit
    // directions of the two wall arms; `reach` is how far the tower or watchtower face stands from the tower centre.
    private static void GateDoors(PropMeshBuilder b, IGroundSampler ground, Vec2 a, Vec2 dir, Vec2 back, double reach, bool stone)
    {
        Vec2 p1 = a + (dir * (GateWidthMeters / 2));
        Vec2 p2 = a + (back * (GateWidthMeters / 2));
        Vec2 mid = Vec2.Lerp(p1, p2, 0.5);
        double gap = Vec2.Distance(p1, p2);
        double leafLength = (gap / 2) - reach;
        // Outward is away from the inside of the corner; a straight wall (no corner) opens along its normal, away from the centre.
        Vec2 sum = dir + back;
        Vec2 outward = sum.Length < 0.1 ? new Vec2(-dir.Y, dir.X) : (-sum).Normalized();
        if (sum.Length < 0.1 && Vec2.Dot(outward, a) < 0)
        {
            outward = -outward;
        }

        float yaw = (float)Math.Atan2(outward.Y, outward.X);
        float height = (float)(stone ? StoneLeafHeight : PalisadeLeafHeight);
        float thick = (float)(stone ? StoneLeafThickness : PalisadeLeafThickness);
        // A sharp wall corner leaves a narrow opening between the towers: no room for leaves, only the lintel.
        foreach (Vec2 post in leafLength < MinLeafMeters ? Array.Empty<Vec2>() : new[] { p1, p2 })
        {
            Vec2 toMid = (mid - post).Normalized();
            Vec2 hinge = post + (toMid * reach);
            Vec2 centre = hinge + (outward * (leafLength / 2)) + (toMid * (thick / 2));
            double h = ground.HeightAt(centre.X, centre.Y) - 0.05;
            b.YawBox(P(centre, h), yaw, new Vector3((float)leafLength, height, thick), stone ? SettlementPalette.Wood : SettlementPalette.Plank);
            float[] bands = stone ? new[] { 0.7f, 2.3f, 3.9f } : new[] { 0.5f, 2.4f };
            foreach (float y in bands)
            {
                b.YawBox(P(centre, h + y), yaw, new Vector3((float)leafLength, stone ? 0.3f : 0.2f, thick + 0.12f), stone ? SettlementPalette.Iron : SettlementPalette.Log(0));
            }
        }

        double beamBase = ground.HeightAt(mid.X, mid.Y);
        if (stone)
        {
            Vector3 centre = P(mid, beamBase + StoneLintelBase + (StoneLintelHeight / 2));
            Vector3 along = Vector3.Normalize(P(p2, 0) - P(p1, 0));
            b.OrientedBox(centre, along, Vector3.UnitY, Vector3.Cross(along, Vector3.UnitY), new Vector3((float)gap, (float)StoneLintelHeight, (float)WallThickness), SettlementPalette.Wall);
        }
        else
        {
            b.Tube(P(p1, beamBase + PalisadeBeamHeight), P(p2, beamBase + PalisadeBeamHeight), 0.24f, 0.24f, LogSides, SettlementPalette.Log(1));
        }
    }

    // Pulls a point on the wall line perpendicular to its own edge (`edgeDir`), towards the central square (always
    // the origin, see City), to WatchtowerInsetMeters (the platform's front edge then sits flush with, slightly
    // overlapping, the wall line: the front posts are not built at all, standing in for them is the wall itself),
    // then nudges it further in, in the same direction, until the two back corners (the only ones meant to clear
    // the wall; the front ones are meant to meet it) are actually inside the wall polygon. The flat inset alone
    // covers an ordinary straight stretch of wall; the nudge covers the gates whose two edges meet at a shallow
    // enough angle, or where a nearby vertex dents the wall inward, that a fixed margin is not always enough.
    // Perpendicular, not straight at the origin: a wall segment is not generally radial, and an offset with any
    // component along the edge would eat into the clearance already kept from the gate beside it.
    private static (Vec2 Centre, Vec2 Inward) PlaceWatchtower(Vec2 onWall, Vec2 edgeDir, IReadOnlyList<Vec2> wall)
    {
        Vec2 normal = new Vec2(edgeDir.Y, -edgeDir.X).Normalized();
        Vec2 inward = Vec2.Dot(normal, -onWall) >= 0 ? normal : -normal;
        double half = WatchtowerSide / 2;
        Vec2 at = onWall + (inward * WatchtowerInsetMeters);
        for (int step = 0; step < WatchtowerNudgeSteps && !BackCornersInside(at, edgeDir, inward, half, wall); step++)
        {
            at += inward * WatchtowerNudgeMeters;
        }

        return (at, inward);
    }

    // Whether the platform's two back (inward-side) corners are inside the wall polygon. The front corners are
    // not checked: they are meant to sit at or past the wall line, not inside it (see Watchtower).
    private static bool BackCornersInside(Vec2 at, Vec2 forward, Vec2 inward, double half, IReadOnlyList<Vec2> wall)
    {
        foreach (int sAlong in new[] { -1, 1 })
        {
            Vec2 corner = at + (forward * (sAlong * half)) + (inward * half);
            if (!PolygonMath.Contains(wall, corner))
            {
                return false;
            }
        }

        return true;
    }

    // Two back posts (the front ones are the wall itself: its own logs stand where they would), two front braces
    // tying the platform back to the wall, a floor with a ladder hole, a ladder and a hip roof whose four corners
    // sit over the four columns that hold it up (the back posts, which already reach that high, and two short
    // front roof posts starting at the platform). `forward` is the wall edge the tower stands beside; everything
    // is built facing it (not world axes), so it stays aligned whatever angle the wall runs at (see
    // PlaceWatchtower). `inward` points from the wall towards the city's centre.
    private static Ladder Watchtower(PropMeshBuilder b, IGroundSampler ground, Vec2 at, Vec2 forward, Vec2 inward)
    {
        double h = ground.HeightAt(at.X, at.Y);
        double postHalf = (WatchtowerSide / 2) - 0.25;
        double roofY = h + WatchtowerHeight;
        foreach (int sAlong in new[] { -1, 1 })
        {
            Vec2 back = at + (forward * (sAlong * postHalf)) + (inward * postHalf);
            // Hash 32, not 0: Log's shade index is (hash >> 5) % shades.Length, so any hash under 32 (0, 1, 2, ...,
            // as used for the fence, the gate beam and the brace/ladder below) collapses to the same shade as 0.
            // The front roof posts below use 64, a shade apart again, so the two stay distinguishable too.
            b.Tube(P(back, h - WallSink / 2), P(back, roofY), 0.26f, 0.22f, LogSides, SettlementPalette.Log(32));
        }

        double half = WatchtowerSide / 2;
        double wallTopY = h + WatchtowerBraceWallHeight;
        double platformY = h + WatchtowerPlatformHeight;
        foreach (int sAlong in new[] { -1, 1 })
        {
            Vec2 wallAnchor = at - (inward * WatchtowerInsetMeters) + (forward * (sAlong * (half - 0.2)));
            Vec2 platformCorner = at + (forward * (sAlong * half)) - (inward * half);
            b.Tube(P(wallAnchor, wallTopY), P(platformCorner, platformY - 0.1), 0.12f, 0.12f, LogSides, SettlementPalette.Log(1));
            // The brace carries the platform's front corner; a short post above it, the same size as the back
            // ones, carries the roof the rest of the way up from there. Hash 64 (not 32, the back posts'), so the
            // two remain distinguishable: the front ones legitimately sit at the wall line, not behind it.
            Vec2 front = at + (forward * (sAlong * postHalf)) - (inward * postHalf);
            b.Tube(P(front, platformY - 0.1), P(front, roofY), 0.26f, 0.22f, LogSides, SettlementPalette.Log(64));
        }

        Vector3 centre3 = P(at, platformY);
        Vector3 along3 = new((float)forward.X, 0, (float)forward.Y);
        Vector3 across3 = new((float)inward.X, 0, (float)inward.Y);
        FloorWithHole(b, centre3, along3, across3, (float)WatchtowerSide, (float)LadderHoleSide, 0.3f, SettlementPalette.Plank);
        Railing(b, centre3 + (Vector3.UnitY * 0.15f), along3, across3, (float)WatchtowerSide);
        LadderProp(b, at, h, platformY);
        Roof(b, at, roofY, along3, across3);

        // The hole itself has no floor to land on, so the top landing point is offset onto the back strip (clear
        // of the hole edge and of the body's own radius, with a margin) instead of sitting at the ladder's own
        // plan position.
        Vec2 topLanding = at + (inward * LadderTopLandingInsetMeters);
        return new Ladder(at, h, platformY + 0.15, topLanding);
    }

    // A four-sided hip roof over the watchtower, its corners WatchtowerRoofOverhang beyond the four columns that
    // carry it (the same postHalf inset Watchtower's own columns use), so each corner sits right over its column
    // with a small eave overhang, instead of a world-axis-aligned shape that drifts out of line whenever the wall
    // runs at an angle. Double-sided like a building's roof (see BuildingMesher.Roof): a single-sided fan is
    // invisible from directly underneath, where the player stands.
    private static void Roof(PropMeshBuilder b, Vec2 at, double baseY, Vector3 along, Vector3 across)
    {
        float half = (float)((WatchtowerSide / 2) - 0.25 + WatchtowerRoofOverhang);
        Vector3 centre = P(at, baseY);
        Vector3[] ring =
        {
            centre + (along * half) + (across * half), centre - (along * half) + (across * half),
            centre - (along * half) - (across * half), centre + (along * half) - (across * half),
        };
        Vector3 apex = centre + (Vector3.UnitY * 2.2f);
        Vector3 inside = centre;
        Vector3 sky = apex + (Vector3.UnitY * 20f);
        for (int i = 0; i < ring.Length; i++)
        {
            Vector3 a = ring[i];
            Vector3 c = ring[(i + 1) % ring.Length];
            b.Triangle(a, c, apex, inside, SettlementPalette.SlumRoof);
            b.Triangle(a, c, apex, sky, SettlementPalette.Log(1));
        }
    }

    // A waist-high rail (posts and a top bar, like FoundationMesher's pier deck rail) running round all four
    // edges of a square at `base` (its top surface), `along`/`across` its local plan axes.
    private static void Railing(PropMeshBuilder b, Vector3 @base, Vector3 along, Vector3 across, float side)
    {
        float s = side / 2;
        Vector3[] corners =
        {
            @base + (along * s) + (across * s), @base - (along * s) + (across * s),
            @base - (along * s) - (across * s), @base + (along * s) - (across * s),
        };
        for (int i = 0; i < corners.Length; i++)
        {
            RailRun(b, corners[i], corners[(i + 1) % corners.Length]);
        }
    }

    // Centre heights of the horizontal log courses, stacked from the floor to the top with no gap between
    // neighbours: a small overlap (courses spaced at 92% of a log's diameter, not the full diameter) so they read
    // as a solid stacked wall rather than a row of logs that merely touch at one point.
    private static double[] RailCourseHeights()
    {
        var heights = new List<double>();
        double spacing = WatchtowerRailLogRadius * 2 * 0.92;
        for (double y = WatchtowerRailLogRadius; y < WatchtowerRailHeight; y += spacing)
        {
            heights.Add(y);
        }

        return heights.ToArray();
    }

    // One straight rail from a to c: evenly spaced upright posts, and horizontal log courses stacked tight between
    // them filling the gap from the floor to the top, round like the palisade's own fence but half its diameter
    // and lying on their side instead of standing upright.
    private static void RailRun(PropMeshBuilder b, Vector3 a, Vector3 c)
    {
        float length = (c - a).Length();
        if (length < 0.1f)
        {
            return;
        }

        int posts = Math.Max(2, (int)Math.Ceiling(length / WatchtowerRailSpacing) + 1);
        for (int k = 0; k < posts; k++)
        {
            Vector3 p = Vector3.Lerp(a, c, k / (float)(posts - 1));
            b.OrientedBox(p + (Vector3.UnitY * (float)(WatchtowerRailHeight / 2)), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(WatchtowerRailPost, (float)WatchtowerRailHeight, WatchtowerRailPost), SettlementPalette.Log(1));
        }

        foreach (double y in WatchtowerRailCourseHeights)
        {
            b.Tube(a + (Vector3.UnitY * (float)y), c + (Vector3.UnitY * (float)y), WatchtowerRailLogRadius, WatchtowerRailLogRadius, LogSides, SettlementPalette.Log(1));
        }
    }

    // A flat square frame (four non-overlapping strips) with a square hole in the middle, `along`/`across` its
    // local plan axes.
    private static void FloorWithHole(PropMeshBuilder b, Vector3 centre, Vector3 along, Vector3 across, float side, float hole, float thickness, Vector3 colour)
    {
        float s = side / 2;
        float h = hole / 2;
        void Strip(float u0, float u1, float v0, float v1)
        {
            Vector3 stripCentre = centre + (along * ((u0 + u1) / 2)) + (across * ((v0 + v1) / 2));
            b.OrientedBox(stripCentre, along, Vector3.UnitY, across, new Vector3(u1 - u0, thickness, v1 - v0), colour);
        }

        Strip(-s, s, h, s);
        Strip(-s, s, -s, -h);
        Strip(h, s, -h, h);
        Strip(-s, -h, -h, h);
    }

    // Two rails and rungs straight up through the floor hole, from the ground to just under the platform: a prop
    // only (the player does not climb it physically, see the "use ladder" interaction it is paired with).
    private static void LadderProp(PropMeshBuilder b, Vec2 at, double bottomY, double topY)
    {
        double rise = topY - bottomY;
        int rungs = Math.Max(2, (int)(rise / LadderRungSpacing));
        foreach (int side in new[] { -1, 1 })
        {
            Vec2 rail = at + new Vec2(side * LadderRailHalfWidth, 0);
            b.Tube(P(rail, bottomY), P(rail, topY), 0.04f, 0.04f, 6, SettlementPalette.Log(2));
        }

        for (int k = 0; k <= rungs; k++)
        {
            double y = bottomY + (rise * k / rungs);
            b.Tube(P(at + new Vec2(-LadderRailHalfWidth, 0), y), P(at + new Vec2(LadderRailHalfWidth, 0), y), 0.03f, 0.03f, 6, SettlementPalette.Log(2));
        }
    }

    // A stretch of wall cut into pieces no longer than DrapeMeters, each tilted to follow the ground and sunk into it.
    private static void WallRun(PropMeshBuilder b, IGroundSampler ground, Vec2 from, Vec2 to)
    {
        int pieces = Math.Max(1, (int)Math.Ceiling(Vec2.Distance(from, to) / DrapeMeters));
        for (int k = 0; k < pieces; k++)
        {
            Vec2 a = Vec2.Lerp(from, to, k / (double)pieces);
            Vec2 c = Vec2.Lerp(from, to, (k + 1) / (double)pieces);
            Vector3 pa = P(a, ground.HeightAt(a.X, a.Y));
            Vector3 pc = P(c, ground.HeightAt(c.X, c.Y));
            Vector3 along = Vector3.Normalize(pc - pa);
            Vector3 across = Vector3.Normalize(new Vector3(-along.Z, 0, along.X));
            Vector3 up = Vector3.Normalize(Vector3.Cross(along, across));
            if (up.Y < 0)
            {
                up = -up;
            }

            float thickness = (float)WallThickness;
            float tall = (float)(WallHeight + WallSink);
            // Overlap the pieces a little so a bend in the ground leaves no gap.
            float length = (pc - pa).Length() + 0.3f;
            Vector3 centre = ((pa + pc) / 2) + (up * (tall / 2 - (float)WallSink));
            b.OrientedBox(centre, along, up, across, new Vector3(length, tall, thickness), SettlementPalette.Wall);
        }
    }

    private static void Tower(PropMeshBuilder b, IGroundSampler ground, Vec2 at, double radius, float height)
    {
        double h = ground.HeightAt(at.X, at.Y);
        b.Tube(P(at, h - WallSink), P(at, h + height), (float)radius * 1.1f, (float)radius * 0.93f, TowerSides, SettlementPalette.Wall);
    }

    private static Vector3 P(Vec2 p, double y) => new((float)p.X, (float)y, (float)p.Y);
}
