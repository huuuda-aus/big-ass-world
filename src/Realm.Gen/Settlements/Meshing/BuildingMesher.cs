using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>
/// Turns a generated building into triangle meshes. A <b>far</b> mesh is a closed solid with painted-on doors and windows, cheap
/// enough for every house of a city; a <b>near</b> mesh has real openings, rooms, floors and stairs and a matching collision mesh,
/// and is built only for houses the player is close to.
/// </summary>
/// <remarks>
/// Coordinates are metres in the city's local frame: plan x is X, plan y is Z, height is Y. Triangles are wound counter-clockwise
/// seen from outside. Ported from the three.js viewer of the CityGen project (buildNear, facadeMesh, timberMesh).
/// Determinism: pure functions of the building and its interior. Thread-safety: pure.
/// </remarks>
public static class BuildingMesher
{
    /// <summary>Thickness of the outer walls, matching <see cref="InteriorGenerator.ExteriorWallThickness"/>.</summary>
    public const double WallThickness = InteriorGenerator.ExteriorWallThickness;

    private const double CathedralRoofRise = 5.0;
    private const double HouseRoofRise = 2.4;
    private const double SlumRoofRise = 1.5;

    /// <summary>Floor tops sit this far above the level line, so they never z-fight with the foundation top.</summary>
    private const double FloorLift = 0.01;

    private const double InteriorWallEmbed = 0.05;
    private const double RoofOverhang = 0.5;
    private const double FrameWidth = 0.1;
    private const float DoorSlab = 0.05f;
    private const int StairSteps = 14;
    private const float RampThickness = 0.2f;
    private const double OnWallTolerance = 0.02;
    private static readonly Vector3 DoorColour = SettlementPalette.Wood;

    /// <summary>Builds the cheap exterior of a building.</summary>
    /// <param name="building">The building.</param>
    /// <param name="interior">Its interior (used for the positions of doors and windows and the storey count).</param>
    /// <param name="site">Where the floor sits on uneven ground; null for level ground. The mesh is built with the floor at 0 and a foundation below it; the caller places it at <see cref="BuildingSite.BaseY"/>.</param>
    /// <returns>A closed mesh: walls, roof, painted openings and half-timbering, on its foundation.</returns>
    public static PropMesh Far(Building building, Interior interior, BuildingSite? site = null)
    {
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(interior);
        var b = new PropMeshBuilder();
        double height = interior.Floors.Count * interior.FloorHeight;
        IReadOnlyList<WallOpening> openings = Openings(building, interior);

        var loop = building.Footprint.Points.Select(p => P(p, 0)).ToList();
        b.Prism(loop, (float)height, SettlementPalette.WallColour(building));
        Roof(b, building, height);
        Facade(b, building, openings);
        if (building.Style == BuildingStyle.Rich)
        {
            Timber(b, building, interior, openings);
        }

        Support(b, building, site, openings);
        return b.Build($"building.{building.Id}.far");
    }

    /// <summary>Builds the detailed exterior and interior of a building, with its collision.</summary>
    /// <param name="building">The building.</param>
    /// <param name="interior">Its interior.</param>
    /// <param name="site">Where the floor sits on uneven ground; null for level ground. See <see cref="Far"/>.</param>
    /// <returns>The visual and collision meshes.</returns>
    public static BuildingMeshes Near(Building building, Interior interior, BuildingSite? site = null)
    {
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(interior);
        var visual = new PropMeshBuilder();
        var collision = new PropMeshBuilder();
        double storey = interior.FloorHeight;
        int levels = interior.Floors.Count;
        double height = levels * storey;
        ConvexPoly fp = building.Footprint;
        IReadOnlyList<WallOpening> openings = Openings(building, interior);
        Vector3 stucco = SettlementPalette.WallColour(building);
        double thickness = interior.ExteriorWallThickness;

        // Outer shell: one plane per footprint edge with the openings cut out.
        for (int j = 0; j < fp.Count; j++)
        {
            Vec2 a = fp.Points[j];
            Vec2 dir = fp.Edge(j).Normalized();
            double length = fp.EdgeLength(j);
            var holes = openings.Where(o => o.Edge == j).Select(o => new Hole(Math.Max(0.05, o.U - (o.Width / 2)), Math.Min(length - 0.05, o.U + (o.Width / 2)), o.Bottom, o.Top)).ToList();
            Vector3 outward = new((float)dir.Y, 0, (float)-dir.X);
            Wall(visual, a, dir, length, height, 0, holes, outward, stucco);
            Wall(collision, a, dir, length, height, 0, holes, outward, stucco);
        }

        foreach (WallOpening o in openings)
        {
            OpeningDetail(visual, fp, o, thickness);
        }

        // Interior. Outer walls: the inner face of each room's outer edges, with the openings cut out. Walls between rooms are solid boxes
        // (below); floors and ceilings are thick slabs.
        for (int level = 0; level < levels; level++)
        {
            Floor floor = interior.Floors[level];
            double y = level * storey;
            for (int r = 0; r < interior.Rooms.Count; r++)
            {
                ConvexPoly shape = interior.Rooms[r].Shape;
                for (int i = 0; i < shape.Count; i++)
                {
                    if (shape.Tags[i] < InteriorGenerator.ExteriorTagBase)
                    {
                        continue; // a wall between rooms
                    }

                    Vec2 a = shape.Points[i];
                    Vec2 dir = shape.Edge(i).Normalized();
                    double length = shape.EdgeLength(i);
                    var holes = new List<Hole>();
                    foreach (Door door in floor.Doors.Where(d => d.RoomA == r || d.RoomB == r))
                    {
                        Hole? h = HoleOn(a, dir, length, door.Position, door.Width, 0.005, door.Height);
                        if (h is not null)
                        {
                            holes.Add(h.Value);
                        }
                    }

                    foreach (Window w in floor.Windows.Where(w => w.Room == r))
                    {
                        Hole? h = HoleOn(a, dir, length, w.Position, w.Width, w.Sill, w.Sill + w.Height);
                        if (h is not null)
                        {
                            holes.Add(h.Value);
                        }
                    }

                    Vector3 inward = new((float)-dir.Y, 0, (float)dir.X);
                    Wall(visual, a, dir, length, storey, y, holes, inward, SettlementPalette.Inner);
                    Wall(collision, a, dir, length, storey, y, holes, inward, SettlementPalette.Inner);
                }
            }

            InteriorWalls(visual, collision, interior, floor, y);
            foreach (Door door in floor.Doors)
            {
                DoorLeaf(visual, door, y, thickness);
            }
        }

        // Slabs: the floor of each level and the ceiling under it, one slab 30 cm thick per boundary (ground, between storeys, under the roof).
        for (int boundary = 0; boundary <= levels; boundary++)
        {
            for (int r = 0; r < interior.Rooms.Count; r++)
            {
                ConvexPoly shape = interior.Rooms[r].Shape;
                IReadOnlyList<Vec2>? hole = interior.Stairs is { } s && s.Room == r && boundary >= 1 && boundary < levels ? StairRect(s) : null;
                Vector3 top = boundary < levels ? SettlementPalette.FloorColour(building, interior.Floors[boundary].RoomTypes[r]) : SettlementPalette.Ceiling;
                foreach (ConvexPoly piece in Pieces(shape, hole))
                {
                    double topY = (boundary * storey) + FloorLift;
                    Slab(visual, piece, topY, boundary < levels, boundary > 0, top);
                    Slab(collision, piece, topY, boundary < levels, boundary > 0, top);
                }
            }
        }

        if (interior.Stairs is { } stairs)
        {
            for (int level = 0; level < levels - 1; level++)
            {
                Stair(visual, collision, stairs, level * storey, storey);
            }
        }

        Roof(visual, building, height);
        if (building.Style == BuildingStyle.Rich)
        {
            Timber(visual, building, interior, openings);
        }

        Support(visual, building, site, openings);
        Support(collision, building, site, openings, sign: false);
        return new BuildingMeshes(visual.Build($"building.{building.Id}.near"), collision.Build($"building.{building.Id}.collision"));
    }

    /// <summary>Finds the front door and windows on the outer walls.</summary>
    /// <param name="building">The building.</param>
    /// <param name="interior">Its interior.</param>
    /// <returns>Every opening with the footprint edge it lies on and its position along that edge.</returns>
    public static IReadOnlyList<WallOpening> Openings(Building building, Interior interior)
    {
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(interior);
        var result = new List<WallOpening>();
        ConvexPoly fp = building.Footprint;
        double storey = interior.FloorHeight;

        void Add(int edge, Vec2 position, double width, double bottom, double top, bool isDoor)
        {
            if (edge < 0 || edge >= fp.Count)
            {
                return;
            }

            Vec2 a = fp.Points[edge];
            result.Add(new WallOpening(edge, Vec2.Dot(position - a, fp.Edge(edge).Normalized()), width, bottom, top, isDoor));
        }

        for (int level = 0; level < interior.Floors.Count; level++)
        {
            Floor floor = interior.Floors[level];
            foreach (Door door in floor.Doors.Where(d => d.RoomB == -1))
            {
                Add(ExteriorEdge(interior, door.RoomA, door.Position), door.Position, door.Width, 0.005, door.Height, true);
            }

            foreach (Window w in floor.Windows)
            {
                Add(ExteriorEdge(interior, w.Room, w.Position), w.Position, w.Width, (level * storey) + w.Sill, (level * storey) + w.Sill + w.Height, false);
            }
        }

        return result;
    }

    // The footprint edge an outer wall point of a room lies on, or -1.
    private static int ExteriorEdge(Interior interior, int room, Vec2 p)
    {
        ConvexPoly shape = interior.Rooms[room].Shape;
        for (int i = 0; i < shape.Count; i++)
        {
            if (shape.Tags[i] < InteriorGenerator.ExteriorTagBase)
            {
                continue;
            }

            Vec2 a = shape.Points[i];
            Vec2 dir = shape.Edge(i).Normalized();
            Vec2 rel = p - a;
            if (Math.Abs(Vec2.Cross(dir, rel)) < OnWallTolerance)
            {
                return shape.Tags[i] - InteriorGenerator.ExteriorTagBase;
            }
        }

        return -1;
    }

    // ---- exterior ----------------------------------------------------------------------------------------------

    // The foundation and front steps (when the ground is uneven) and the sign over the door (service buildings).
    private static void Support(PropMeshBuilder b, Building building, BuildingSite? site, IReadOnlyList<WallOpening> openings, bool sign = true)
    {
        foreach (WallOpening door in openings)
        {
            if (!door.IsDoor)
            {
                continue;
            }

            if (site is not null)
            {
                FoundationMesher.Add(b, building, site, door.Edge, door.U);
            }

            if (sign && building.Service is { } service)
            {
                Sign(b, building, door, service);
            }

            return;
        }
    }

    // A board hanging from a bracket over the door; its colour tells the service apart at a glance.
    private static void Sign(PropMeshBuilder b, Building building, WallOpening door, ServiceSpec service)
    {
        ConvexPoly fp = building.Footprint;
        Vec2 a = fp.Points[door.Edge];
        Vec2 dir = fp.Edge(door.Edge).Normalized();
        Vec2 outward = fp.OutwardNormal(door.Edge);
        var along = new Vector3((float)dir.X, 0, (float)dir.Y);
        var across = new Vector3((float)outward.X, 0, (float)outward.Y);
        Vec2 at = a + (dir * door.U);
        const float bracketY = 2.7f;
        const float armLength = 1.0f;
        Vector3 Point(double outwardMeters, double y) => P(at + (outward * outwardMeters), y);

        b.OrientedBox(Point(armLength / 2, bracketY), across, Vector3.UnitY, along, new Vector3(armLength, 0.08f, 0.08f), SettlementPalette.Wood);
        b.OrientedBox(Point(armLength - 0.05, bracketY - 0.35f), along, Vector3.UnitY, across, new Vector3(0.9f, 0.6f, 0.06f), SettlementPalette.Sign(service.Id));
    }

    private static void Roof(PropMeshBuilder b, Building building, double baseY)
    {
        ConvexPoly fp = building.Footprint;
        Vec2 centre = fp.Centroid;
        bool slum = building.Style == BuildingStyle.Slum;
        double rise = building.Ward == WardType.Cathedral ? CathedralRoofRise : slum ? SlumRoofRise : HouseRoofRise;
        var ring = fp.Points.Select(p => P(p + ((p - centre).Normalized() * RoofOverhang), baseY)).ToList();
        Vector3 apex = P(centre, baseY + rise);
        Vector3 inside = P(centre, baseY);
        Vector3 colour = slum ? SettlementPalette.SlumRoof : SettlementPalette.WardRoof(building.Ward);
        for (int i = 0; i < ring.Count; i++)
        {
            b.Triangle(ring[i], ring[(i + 1) % ring.Count], apex, inside, colour);
        }
    }

    // Painted-on doors and windows for houses seen from afar.
    private static void Facade(PropMeshBuilder b, Building building, IReadOnlyList<WallOpening> openings)
    {
        ConvexPoly fp = building.Footprint;
        foreach (WallOpening o in openings)
        {
            Vec2 a = fp.Points[o.Edge];
            Vec2 dir = fp.Edge(o.Edge).Normalized();
            Vector3 outward = new((float)dir.Y, 0, (float)-dir.X);
            Vector3 At(double u, double v, double offset) => P(a + (dir * u), v) + (outward * (float)offset);
            void Rect(double u0, double u1, double v0, double v1, double offset, Vector3 colour) =>
                b.FacingQuad(At(u0, v0, offset), At(u1, v0, offset), At(u1, v1, offset), At(u0, v1, offset), outward, colour);

            double half = o.Width / 2;
            Rect(o.U - half - FrameWidth, o.U + half + FrameWidth, o.IsDoor ? 0 : o.Bottom - FrameWidth, o.Top + FrameWidth, 0.03, SettlementPalette.Frame);
            Rect(o.U - half, o.U + half, o.Bottom, o.Top, 0.06, o.IsDoor ? DoorColour : SettlementPalette.Glass);
        }
    }

    // Half-timbering on the outside walls: corner posts, rails at every floor line, and studs and braces between,
    // all kept clear of doors and windows.
    private static void Timber(PropMeshBuilder b, Building building, Interior interior, IReadOnlyList<WallOpening> openings)
    {
        if (building.Ward == WardType.Cathedral)
        {
            return; // stone, not timber
        }

        ConvexPoly fp = building.Footprint;
        double storey = interior.FloorHeight;
        int levels = interior.Floors.Count;
        const double post = 0.14;
        const double rail = 0.2;
        const double depth = 0.1;

        for (int j = 0; j < fp.Count; j++)
        {
            Vec2 a = fp.Points[j];
            Vec2 dir = fp.Edge(j).Normalized();
            double length = fp.EdgeLength(j);
            Vector3 outward = new((float)dir.Y, 0, (float)-dir.X);
            Vector3 At(double u, double v) => P(a + (dir * u), v);
            List<WallOpening> ops = openings.Where(o => o.Edge == j).ToList();

            Beam(b, At(0, 0), At(0, levels * storey), outward, 0.28, depth + 0.04);

            var railHeights = new List<double> { 0.1 };
            for (int l = 1; l < levels; l++)
            {
                railHeights.Add(l * storey);
            }

            railHeights.Add((levels * storey) - 0.1);
            foreach (double y in railHeights)
            {
                var blocked = ops.Where(o => o.Bottom - 0.15 < y + 0.1 && o.Top + 0.15 > y - 0.1).Select(o => (o.U - (o.Width / 2) - 0.1, o.U + (o.Width / 2) + 0.1)).ToList();
                foreach ((double f0, double f1) in FreeIntervals(0, length, blocked))
                {
                    if (f1 - f0 > 0.3)
                    {
                        Beam(b, At(f0, y), At(f1, y), outward, rail, depth + 0.02);
                    }
                }
            }

            for (int l = 0; l < levels; l++)
            {
                double v0 = l == 0 ? 0.1 : l * storey;
                double v1 = l == levels - 1 ? (levels * storey) - 0.1 : (l + 1) * storey;
                List<WallOpening> here = ops.Where(o => o.Top > l * storey && o.Bottom < (l + 1) * storey).ToList();
                foreach (WallOpening o in here)
                {
                    foreach (int side in new[] { -1, 1 })
                    {
                        double u = o.U + (side * ((o.Width / 2) + 0.15));
                        if (u > 0.3 && u < length - 0.3)
                        {
                            Beam(b, At(u, v0), At(u, v1), outward, post, depth);
                        }
                    }
                }

                var blockedStretch = here.Select(o => (o.U - (o.Width / 2) - 0.22, o.U + (o.Width / 2) + 0.22)).ToList();
                foreach ((double f0, double f1) in FreeIntervals(0, length, blockedStretch))
                {
                    double w = f1 - f0;
                    if (w < 0.9)
                    {
                        continue;
                    }

                    int panels = Math.Max(1, (int)Math.Round(w / 2.2));
                    for (int k = 0; k < panels; k++)
                    {
                        double pa = f0 + (w * k / panels);
                        double pb = f0 + (w * (k + 1) / panels);
                        if (k > 0)
                        {
                            Beam(b, At(pa, v0), At(pa, v1), outward, post, depth);
                        }

                        if (pb - pa < 0.9)
                        {
                            continue;
                        }

                        bool rising = (((j * 7) + (l * 13) + (k * 5)) & 1) == 0;
                        const double inset = 0.12;
                        Beam(b, At(rising ? pa + inset : pb - inset, v0 + 0.1), At(rising ? pb - inset : pa + inset, v1 - 0.1), outward, 0.12, depth - 0.02);
                    }
                }
            }
        }
    }

    // A beam from p0 to p1 lying on a wall with the given outward normal, sticking out by `depth`.
    private static void Beam(PropMeshBuilder b, Vector3 p0, Vector3 p1, Vector3 normal, double width, double depth)
    {
        Vector3 along = p1 - p0;
        float length = along.Length();
        if (length < 0.05f)
        {
            return;
        }

        along /= length;
        Vector3 side = Vector3.Normalize(Vector3.Cross(along, normal));
        float half = (float)(width / 2);
        const float sunk = 0.03f;
        Vector3 Corner(bool end, int s, float outward) => (end ? p1 : p0) + (side * (s * half)) + (normal * outward);
        float top = (float)depth;
        Vector3 colour = SettlementPalette.Beam;
        b.FacingQuad(Corner(false, -1, top), Corner(true, -1, top), Corner(true, 1, top), Corner(false, 1, top), normal, colour);
        b.FacingQuad(Corner(false, 1, -sunk), Corner(true, 1, -sunk), Corner(true, 1, top), Corner(false, 1, top), side, colour);
        b.FacingQuad(Corner(true, -1, -sunk), Corner(false, -1, -sunk), Corner(false, -1, top), Corner(true, -1, top), -side, colour);
    }

    // ---- near: openings, doors, stairs ---------------------------------------------------------------------------

    // Fills the wall thickness around an opening, then adds the frame (and glass for windows).
    private static void OpeningDetail(PropMeshBuilder b, ConvexPoly fp, WallOpening o, double wallThickness)
    {
        Vec2 a = fp.Points[o.Edge];
        Vec2 dir = fp.Edge(o.Edge).Normalized();
        Vector3 outward = new((float)dir.Y, 0, (float)-dir.X);
        Vector3 At(double u, double v, double offset) => P(a + (dir * u), v) + (outward * (float)offset);
        double u0 = o.U - (o.Width / 2);
        double u1 = o.U + (o.Width / 2);
        double height = o.Top - o.Bottom;
        double middleV = (o.Bottom + o.Top) / 2;
        double mid = -wallThickness / 2;

        void Reveal(double ua, double va, double ub, double vb)
        {
            Vector3 normal = Vector3.Normalize(Vector3.Cross(At(ub, vb, 0) - At(ua, va, 0), outward));
            b.DoubleQuad(At(ua, va, 0), At(ub, vb, 0), At(ub, vb, -wallThickness), At(ua, va, -wallThickness), normal, SettlementPalette.Reveal);
        }

        Reveal(u0, o.Bottom, u0, o.Top);
        Reveal(u1, o.Bottom, u1, o.Top);
        Reveal(u0, o.Top, u1, o.Top);
        if (!o.IsDoor)
        {
            Reveal(u0, o.Bottom, u1, o.Bottom);
        }

        void FrameBox(double u, double v, double sx, double sy, double sz, Vector3 colour)
        {
            Vector3 centre = At(u, v, mid);
            Vector3 along = new((float)dir.X, 0, (float)dir.Y);
            b.OrientedBox(centre, along, Vector3.UnitY, outward, new Vector3((float)sx, (float)sy, (float)sz), colour);
        }

        const double bar = 0.06;
        const double deep = 0.1;
        FrameBox(u0 + (bar / 2), middleV, bar, height, deep, SettlementPalette.Frame);
        FrameBox(u1 - (bar / 2), middleV, bar, height, deep, SettlementPalette.Frame);
        FrameBox(o.U, o.Top - (bar / 2), o.Width, bar, deep, SettlementPalette.Frame);
        if (o.IsDoor)
        {
            return;
        }

        FrameBox(o.U, o.Bottom + (bar / 2), o.Width, bar, deep, SettlementPalette.Frame);
        FrameBox(o.U, middleV, o.Width, 0.04, 0.06, SettlementPalette.Frame);
        FrameBox(o.U, middleV, 0.04, height, 0.06, SettlementPalette.Frame);
        FrameBox(o.U, middleV, o.Width - bar, height - bar, 0.01, SettlementPalette.Glass);
    }

    // A closed door, thin enough to sit in its doorway. The collision mesh ignores doors, so a walker passes through.
    private static void DoorLeaf(PropMeshBuilder b, Door door, double floorY, double wallThickness)
    {
        Vec2 dir = door.WallDirection;
        Vec2 inward = new(-dir.Y, dir.X);
        double shift = door.RoomB == -1 ? wallThickness / 2 : 0; // a front door sits midway through the thick wall
        Vec2 centre = door.Position - (inward * shift);
        float yaw = (float)Math.Atan2(dir.Y, dir.X);
        float width = (float)door.Width - 0.04f;
        float height = (float)door.Height - 0.04f;
        b.YawBox(P(centre, floorY + 0.02), yaw, new Vector3(width, height, DoorSlab), DoorColour);
        foreach (int side in new[] { -1, 1 })
        {
            Vec2 knob = centre + (dir * ((width / 2) - 0.12)) + (inward * (side * (DoorSlab + 0.03)));
            b.YawBox(P(knob, floorY + 0.96), yaw, new Vector3(0.07f, 0.07f, 0.09f), SettlementPalette.Handle);
        }
    }

    // Visual: a flight of steps. Collision: a smooth ramp, so a character body walks up without catching on each riser.
    private static void Stair(PropMeshBuilder visual, PropMeshBuilder collision, Stair stair, double baseY, double rise)
    {
        Vec2 direction = (stair.End - stair.Start).Normalized();
        double length = Vec2.Distance(stair.Start, stair.End);
        float yaw = (float)Math.Atan2(direction.Y, direction.X);
        for (int k = 0; k < StairSteps; k++)
        {
            double h = (k + 1) * rise / StairSteps;
            double t = (k + 0.5) / StairSteps;
            Vec2 centre = stair.Start + (direction * (length * t));
            visual.YawBox(P(centre, baseY), yaw, new Vector3((float)(length / StairSteps), (float)h, (float)stair.Width), SettlementPalette.Stair);
        }

        Vector3 start = P(stair.Start, baseY);
        Vector3 end = P(stair.End, baseY + rise);
        Vector3 slope = Vector3.Normalize(end - start);
        (Vector3 _, Vector3 across) = PropMeshBuilder.Axes(yaw);
        Vector3 up = Vector3.Normalize(Vector3.Cross(across, slope));
        float slopeLength = (end - start).Length();
        // The ramp surface runs half a step above the line through the tread corners so the walker never sinks into a tread.
        Vector3 centreLine = ((start + end) / 2) + (Vector3.UnitY * (float)(rise / (2 * StairSteps))) - (up * (RampThickness / 2));
        collision.OrientedBox(centreLine, slope, up, across, new Vector3(slopeLength, RampThickness, (float)stair.Width), SettlementPalette.Stair);
    }

    // ---- walls between rooms and slabs ---------------------------------------------------------------------------

    // Every wall between rooms as a solid box of the interior wall thickness, with the doorways cut out. A wall can be split into several
    // room edges, so the edges that share a cut are merged into runs along the cut line first.
    private static void InteriorWalls(PropMeshBuilder visual, PropMeshBuilder collision, Interior interior, Floor floor, double levelY)
    {
        var byCut = new SortedDictionary<int, List<(Vec2 From, Vec2 To)>>();
        foreach (Room room in interior.Rooms)
        {
            ConvexPoly shape = room.Shape;
            for (int i = 0; i < shape.Count; i++)
            {
                if (shape.Tags[i] >= 0 && shape.Tags[i] < InteriorGenerator.ExteriorTagBase)
                {
                    if (!byCut.TryGetValue(shape.Tags[i], out List<(Vec2, Vec2)>? list))
                    {
                        byCut[shape.Tags[i]] = list = new List<(Vec2, Vec2)>();
                    }

                    list.Add((shape.Points[i], shape.EdgeEnd(i)));
                }
            }
        }

        double bottom = levelY + FloorLift;
        double top = levelY + interior.FloorHeight - InteriorGenerator.SlabThickness + FloorLift + InteriorWallEmbed;
        foreach ((int _, List<(Vec2 From, Vec2 To)> segments) in byCut)
        {
            Vec2 origin = segments[0].From;
            Vec2 dir = (segments[0].To - segments[0].From).Normalized();
            var runs = new List<(double Lo, double Hi)>();
            foreach ((Vec2 from, Vec2 to) in segments)
            {
                double a = Vec2.Dot(from - origin, dir), c = Vec2.Dot(to - origin, dir);
                runs.Add((Math.Min(a, c), Math.Max(a, c)));
            }

            // Merge overlapping runs.
            runs.Sort((x, y) => x.Lo.CompareTo(y.Lo));
            var merged = new List<(double Lo, double Hi)>();
            foreach ((double lo, double hi) in runs)
            {
                if (merged.Count > 0 && lo <= merged[^1].Hi + 1e-6)
                {
                    merged[^1] = (merged[^1].Lo, Math.Max(merged[^1].Hi, hi));
                }
                else
                {
                    merged.Add((lo, hi));
                }
            }

            // The doorways in this wall.
            var doors = new List<(double Lo, double Hi, double Height)>();
            foreach (Door door in floor.Doors.Where(d => d.RoomB != -1))
            {
                Vec2 rel = door.Position - origin;
                if (Math.Abs(Vec2.Cross(dir, rel)) < OnWallTolerance)
                {
                    double u = Vec2.Dot(rel, dir);
                    doors.Add((u - (door.Width / 2), u + (door.Width / 2), door.Height));
                }
            }

            foreach ((double lo, double hi) in merged)
            {
                double at = lo;
                foreach ((double d0, double d1, double doorHeight) in doors.Where(d => d.Hi > lo && d.Lo < hi).OrderBy(d => d.Lo))
                {
                    WallRun(visual, collision, origin, dir, at, Math.Max(at, d0), bottom, top, interior.InteriorWallThickness, lo, hi);
                    WallRun(visual, collision, origin, dir, Math.Max(d0, lo), Math.Min(d1, hi), bottom + doorHeight, top, interior.InteriorWallThickness, lo, hi); // the lintel
                    at = Math.Min(d1, hi);
                }

                WallRun(visual, collision, origin, dir, at, hi, bottom, top, interior.InteriorWallThickness, lo, hi);
            }
        }
    }

    // One solid piece of interior wall from u0 to u1 along the line, y0 to y1. The ends of the whole wall reach a little into whatever
    // they meet; the ends beside a doorway are the door jambs.
    private static void WallRun(PropMeshBuilder visual, PropMeshBuilder collision, Vec2 origin, Vec2 dir, double u0, double u1, double y0, double y1, double thickness, double wallLo, double wallHi)
    {
        if (u1 - u0 < 0.02 || y1 - y0 < 0.02)
        {
            return;
        }

        double from = u0 <= wallLo + 1e-6 ? u0 - InteriorWallEmbed : u0;
        double to = u1 >= wallHi - 1e-6 ? u1 + InteriorWallEmbed : u1;
        foreach (PropMeshBuilder b in new[] { visual, collision })
        {
            Box(b, origin + (dir * from), origin + (dir * to), y0, y1, thickness);
        }
    }

    // A box along p to q, `thickness` wide, from y0 to y1: its long faces (Inner colour) face the rooms either side; the rest is wall section.
    private static void Box(PropMeshBuilder b, Vec2 p, Vec2 q, double y0, double y1, double thickness)
    {
        Vec2 dir = (q - p).Normalized();
        Vec2 n = new(-dir.Y, dir.X);
        double h = thickness / 2;
        Vec2 pl = p + (n * h), pr = p - (n * h), ql = q + (n * h), qr = q - (n * h);
        var left = new Vector3((float)n.X, 0, (float)n.Y);
        var along = new Vector3((float)dir.X, 0, (float)dir.Y);
        Vector3 Pt(Vec2 v, double y) => P(v, y);
        b.FacingQuad(Pt(pl, y0), Pt(ql, y0), Pt(ql, y1), Pt(pl, y1), left, SettlementPalette.Inner);
        b.FacingQuad(Pt(qr, y0), Pt(pr, y0), Pt(pr, y1), Pt(qr, y1), -left, SettlementPalette.Inner);
        b.FacingQuad(Pt(pl, y1), Pt(ql, y1), Pt(qr, y1), Pt(pr, y1), Vector3.UnitY, SettlementPalette.Reveal);
        b.FacingQuad(Pt(pl, y0), Pt(ql, y0), Pt(qr, y0), Pt(pr, y0), -Vector3.UnitY, SettlementPalette.Reveal);
        b.FacingQuad(Pt(pr, y0), Pt(pl, y0), Pt(pl, y1), Pt(pr, y1), -along, SettlementPalette.Reveal);
        b.FacingQuad(Pt(ql, y0), Pt(qr, y0), Pt(qr, y1), Pt(ql, y1), along, SettlementPalette.Reveal);
    }

    // A floor and ceiling slab over a convex piece of a room: `topY` is the top surface, the slab is SlabThickness thick. The top face is
    // the floor of the room above, the bottom face the ceiling of the room below; the sides show round a stairwell.
    private static void Slab(PropMeshBuilder b, ConvexPoly piece, double topY, bool drawTop, bool drawBottom, Vector3 topColour)
    {
        double bottomY = topY - InteriorGenerator.SlabThickness;
        if (drawTop)
        {
            b.FacingPolygon(piece.Points.Select(p => P(p, topY)).ToList(), Vector3.UnitY, topColour);
        }

        if (drawBottom)
        {
            b.FacingPolygon(piece.Points.Select(p => P(p, bottomY)).ToList(), -Vector3.UnitY, SettlementPalette.Ceiling);
        }

        for (int i = 0; i < piece.Count; i++)
        {
            Vec2 a = piece.Points[i], c = piece.EdgeEnd(i);
            Vec2 outward = piece.OutwardNormal(i);
            b.FacingQuad(P(a, bottomY), P(c, bottomY), P(c, topY), P(a, topY), new Vector3((float)outward.X, 0, (float)outward.Y), SettlementPalette.Reveal);
        }
    }

    // ---- shared geometry helpers ---------------------------------------------------------------------------------

    private readonly record struct Hole(double U0, double U1, double V0, double V1);

    private static Hole? HoleOn(Vec2 origin, Vec2 dir, double length, Vec2 p, double width, double v0, double v1)
    {
        Vec2 rel = p - origin;
        double u = Vec2.Dot(rel, dir);
        double off = Math.Abs(Vec2.Cross(dir, rel));
        if (off > OnWallTolerance || u < 0 || u > length)
        {
            return null;
        }

        return new Hole(Math.Max(0.05, u - (width / 2)), Math.Min(length - 0.05, u + (width / 2)), v0, v1);
    }

    // A rectangular wall from `a` along `dir`, `height` tall starting at `y`, facing `normal`, with rectangular holes.
    private static void Wall(PropMeshBuilder b, Vec2 a, Vec2 dir, double length, double height, double y, IReadOnlyList<Hole> holes, Vector3 normal, Vector3 colour)
    {
        var cuts = new SortedSet<double> { 0, length };
        foreach (Hole h in holes)
        {
            cuts.Add(Math.Clamp(h.U0, 0, length));
            cuts.Add(Math.Clamp(h.U1, 0, length));
        }

        double[] u = cuts.ToArray();
        for (int i = 0; i + 1 < u.Length; i++)
        {
            double ua = u[i];
            double ub = u[i + 1];
            if (ub - ua < 1e-6)
            {
                continue;
            }

            var blocked = holes.Where(h => h.U0 <= ua + 1e-9 && h.U1 >= ub - 1e-9).Select(h => (h.V0, h.V1)).ToList();
            foreach ((double v0, double v1) in FreeIntervals(0, height, blocked))
            {
                b.FacingQuad(P(a + (dir * ua), y + v0), P(a + (dir * ub), y + v0), P(a + (dir * ub), y + v1), P(a + (dir * ua), y + v1), normal, colour);
            }
        }
    }

    // The parts of [lo, hi] not covered by any blocked interval.
    private static IEnumerable<(double, double)> FreeIntervals(double lo, double hi, IEnumerable<(double, double)> blocked)
    {
        double at = lo;
        foreach ((double b0, double b1) in blocked.OrderBy(x => x.Item1))
        {
            if (b0 > at)
            {
                double end = Math.Min(b0, hi);
                if (end > at)
                {
                    yield return (at, end);
                }
            }

            at = Math.Max(at, b1);
        }

        if (at < hi)
        {
            yield return (at, hi);
        }
    }

    private static void Flat(PropMeshBuilder b, ConvexPoly poly, double y, bool faceUp, Vector3 colour) =>
        b.FacingPolygon(poly.Points.Select(p => P(p, y)).ToList(), faceUp ? Vector3.UnitY : -Vector3.UnitY, colour);

    // Convex pieces of a convex polygon with a rectangle cut out of it (or the polygon itself when there is no hole).
    private static IReadOnlyList<ConvexPoly> Pieces(ConvexPoly polygon, IReadOnlyList<Vec2>? hole)
    {
        if (hole is null)
        {
            return new[] { polygon };
        }

        var pieces = new List<ConvexPoly>();
        ConvexPoly? current = polygon;
        double area = 0;
        for (int i = 0; i < hole.Count; i++)
        {
            area += Vec2.Cross(hole[i], hole[(i + 1) % hole.Count]);
        }

        for (int i = 0; i < hole.Count && current is not null; i++)
        {
            Vec2 p = hole[i];
            Vec2 q = hole[(i + 1) % hole.Count];
            Vec2 edge = q - p;
            Vec2 outward = (area >= 0 ? new Vec2(edge.Y, -edge.X) : new Vec2(-edge.Y, edge.X)).Normalized();
            ConvexPoly? outside = current.Clip(p, -outward, -1);
            if (outside is not null)
            {
                pieces.Add(outside);
            }

            current = current.Clip(p, outward, -1);
        }

        return pieces;
    }

    private static IReadOnlyList<Vec2> StairRect(Stair s)
    {
        Vec2 d = (s.End - s.Start).Normalized();
        Vec2 side = new Vec2(-d.Y, d.X) * (s.Width / 2);
        return new[] { s.Start - side, s.End - side, s.End + side, s.Start + side };
    }

    private static Vector3 P(Vec2 p, double y) => new((float)p.X, (float)y, (float)p.Y);
}
