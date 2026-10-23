using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Realm.Game.Bridge;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Meshing;
using Realm.Gen.Settlements.Placement;
using Realm.Sim.Npcs;
using Realm.Sim.Time;

namespace Realm.Game.World.Settlements;

/// <summary>
/// A city standing in the world. Every house is a cheap closed mesh; houses within <see cref="NearBuildMeters"/> of the player are
/// swapped for a detailed one with real doors, windows, rooms, stairs and collision, and swapped back beyond
/// <see cref="NearDropMeters"/>. Nodes are added a few at a time so no frame is spent creating a whole city.
/// </summary>
/// <remarks>
/// The node sits at the settlement's centre at its ground height and is rotated so that the city's local frame (plan x, height,
/// plan y) lines up with the world. Main thread only; the heavy work happens in <see cref="CityBuild"/> and in near-building tasks.
/// </remarks>
public sealed partial class CityInstance : Node3D
{
    /// <summary>Houses closer than this to the player get their detailed interior built, in metres.</summary>
    public const double NearBuildMeters = 35;

    /// <summary>Detailed houses farther than this from the player are dropped again, in metres.</summary>
    public const double NearDropMeters = 60;

    private const int FarNodesPerFrame = 40;
    private const int MaxNearTasks = 2;
    private const double NearScanSeconds = 0.2;

    private readonly CityBuild _build;
    private readonly Settlement _settlement;
    private readonly MeshInstance3D?[] _far;
    private readonly Node3D?[] _near;
    private readonly bool[] _nearBuilding;
    private readonly ConcurrentQueue<NearResult> _finished = new();
    private readonly List<int> _wanted = new();
    private int _nextFar;
    private int _tasks;
    private double _scanTimer;
    private bool _attached;
    private const double WalkMetresPerSecond = 1.4;
    private const double BodyRadiusMeters = 0.35;
    private const double CrowdActiveMeters = 450;
    private const double PlayerNearMeters = 30;
    private const double PickToleranceMeters = 0.35;
    private static readonly float[] PickHeights = { -0.6f, 0f, 0.6f };
    private const double PickGrowthPerMeter = 0.02;
    private Crowd? _crowd;
    private CrowdView? _crowdView;
    private WorldClock? _clock;
    private NavDebugView? _navDebug;
    private bool _navDebugVisible;

    // The lamps of occupied houses (see UpdateLights).
    private const double LampFadePerSecond = 1.2;
    private const double LampGainFadePerSecond = 5;
    private const float OutsideLampGain = 3f; // lamps look brighter from the street; walking in eases them back to normal
    private const double LampEpsilon = 0.005;
    private readonly int[] _doorNodeOf;
    private readonly int[] _inside;
    private readonly float[] _lamp;
    private readonly float[] _gain;
    private readonly MeshInstance3D?[] _interior;
    private readonly MeshInstance3D?[] _aura;
    private readonly MeshInstance3D?[] _glow;

    private sealed record NearResult(int Index, ArrayMesh Visual, ConcavePolygonShape3D Shape, ArrayMesh? Rooms, ArrayMesh? Glass);

    /// <summary>Creates the instance from a finished build; call <see cref="Attach"/> next frame loop via <see cref="Process"/>.</summary>
    /// <param name="build">The worker-built city.</param>
    /// <param name="clock">The world clock the characters live by; null for a city without characters.</param>
    public CityInstance(CityBuild build, WorldClock? clock = null)
    {
        _build = build ?? throw new ArgumentNullException(nameof(build));
        _settlement = build.Settlement;
        _far = new MeshInstance3D?[build.Buildings.Length];
        _near = new Node3D?[build.Buildings.Length];
        _nearBuilding = new bool[build.Buildings.Length];
        _lamp = new float[build.Buildings.Length];
        _gain = new float[build.Buildings.Length];
        Array.Fill(_gain, OutsideLampGain);
        _interior = new MeshInstance3D?[build.Buildings.Length];
        _aura = new MeshInstance3D?[build.Buildings.Length];
        _glow = new MeshInstance3D?[build.Buildings.Length];
        _inside = new int[build.Nav.Graph.Nodes.Count];
        _doorNodeOf = new int[build.Buildings.Length];
        Array.Fill(_doorNodeOf, -1);
        var indexOfId = new Dictionary<int, int>();
        for (int i = 0; i < build.Buildings.Length; i++)
        {
            indexOfId[build.Buildings[i].Building.Id] = i;
        }

        for (int node = 0; node < build.Nav.Graph.Nodes.Count; node++)
        {
            Realm.Sim.Navigation.NavNode n = build.Nav.Graph.Nodes[node];
            if (n.Kind == Realm.Sim.Navigation.NavNodeKind.Door && indexOfId.TryGetValue(n.Ref, out int house))
            {
                _doorNodeOf[house] = node;
            }
        }

        Name = _settlement.Id;
        Position = new Vector3((float)_settlement.X, (float)_settlement.GroundHeightMeters, (float)_settlement.Z);
        // Plan rotation by theta counter-clockwise (plan x toward plan y) is a rotation about the vertical axis by -theta.
        Rotation = new Vector3(0, (float)-_settlement.RotationRadians, 0);
        if (clock is not null && build.CrowdPlans.Count > 0)
        {
            _clock = clock;
            double metresPerMinute = WalkMetresPerSecond * clock.RealSecondsPerGameDay / GameTime.MinutesPerDay;
            _crowd = new Crowd(
                build.Nav.Graph,
                build.CrowdPlans,
                clock.Days * GameTime.MinutesPerDay,
                metresPerMinute,
                (ax, az, bx, bz) => build.Open.Clear(new Vec2(ax, az), new Vec2(bx, bz)),
                (rx, rz, wx, wz) =>
                {
                    Vec2 v = build.Open.Constrain(new Vec2(rx, rz), new Vec2(wx, wz), BodyRadiusMeters);
                    return (v.X, v.Y);
                });
            _crowdView = new CrowdView(build.CrowdPlans.Count);
            AddChild(_crowdView);
        }
    }

    // The characters only live (and are drawn) while the player is near; when the player comes back the crowd catches up with the clock.
    private void UpdateCrowd(Vector3 playerWorld, double rain, double delta)
    {
        if (_crowd is null || _crowdView is null || _clock is null)
        {
            return;
        }

        bool active = _settlement.DistanceTo(playerWorld.X, playerWorld.Z) < _settlement.WallRadiusMeters + CrowdActiveMeters;
        _crowdView.Visible = active;
        if (active)
        {
            double now = _clock.Days * GameTime.MinutesPerDay;
            // What the characters can sense: the rain, and the player when near (in the city's own frame).
            Vector3 local = ToLocal(playerWorld);
            bool near = _settlement.DistanceTo(playerWorld.X, playerWorld.Z) < _settlement.WallRadiusMeters + PlayerNearMeters;
            _crowd.Update(now, new CrowdContext(rain, near, local.X, local.Z));
            _crowdView.Show(_crowd, now, delta, _build.Ground, _build.City.Bridges);
        }
    }

    // At night the windows of a house glow while someone is home. A house's lamp level eases toward 1 when it is occupied and 0 when it is
    // empty; its interior lamps, the glow painted on a far house and the light thrown on the street all follow that one number.
    private void UpdateLights(Vector3 playerWorld, double delta)
    {
        bool night = CityLighting.Night > 0.15f;
        bool any = false;
        foreach (float v in _lamp)
        {
            if (v > LampEpsilon)
            {
                any = true;
                break;
            }
        }

        if (!night && !any)
        {
            return;
        }

        bool active = _crowd is not null && _crowdView is { Visible: true };
        if (active)
        {
            _crowd!.CountInside(_inside); // Safe: active implies a crowd.
        }

        Vec2 player = _settlement.ToLocal(playerWorld.X, playerWorld.Z);
        float step = (float)Math.Min(1, delta * LampFadePerSecond);
        float gainStep = (float)Math.Min(1, delta * LampGainFadePerSecond);
        for (int i = 0; i < _lamp.Length; i++)
        {
            float target = night && active && _doorNodeOf[i] >= 0 && _inside[_doorNodeOf[i]] > 0 ? 1f : 0f;
            float level = _lamp[i];
            if (Math.Abs(target - level) > LampEpsilon)
            {
                level += (target - level) * step;
                if (Math.Abs(target - level) <= LampEpsilon)
                {
                    level = target;
                }

                _lamp[i] = level;
                ApplyLamp(i, level);
            }

            if (_interior[i] is { } interior)
            {
                float wantedGain = Inside(i, player) ? 1f : OutsideLampGain;
                if (Math.Abs(wantedGain - _gain[i]) > LampEpsilon)
                {
                    _gain[i] += (wantedGain - _gain[i]) * gainStep;
                    interior.SetInstanceShaderParameter("lamp_gain", _gain[i]);
                }
            }
        }
    }

    private void ApplyLamp(int i, float level)
    {
        CityBuilding b = _build.Buildings[i];
        bool lit = level > LampEpsilon;
        _interior[i]?.SetInstanceShaderParameter("lamp_level", level);
        if (b.Aura is not null)
        {
            if (_aura[i] is null && lit)
            {
                _aura[i] = new MeshInstance3D
                {
                    Mesh = b.Aura,
                    MaterialOverride = CityLighting.Aura,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    Name = $"Aura{b.Building.Id}",
                };
                AddChild(_aura[i]);
            }

            if (_aura[i] is { } aura)
            {
                aura.SetInstanceShaderParameter("lamp_level", level);
                aura.Visible = lit;
            }
        }

        if (b.Glow is not null && _far[i] is { } far)
        {
            if (_glow[i] is null && lit)
            {
                _glow[i] = new MeshInstance3D { Mesh = b.Glow, MaterialOverride = CityLighting.Glow, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Name = "Glow" };
                far.AddChild(_glow[i]);
            }

            if (_glow[i] is { } glow)
            {
                glow.SetInstanceShaderParameter("lamp_level", level);
                glow.Visible = lit;
            }
        }
    }

    // Whether a point (in the city's frame) is inside the walls of a house.
    private bool Inside(int i, Vec2 p)
    {
        Realm.Gen.Settlements.Geometry.ConvexPoly fp = _build.Buildings[i].Building.Footprint;
        for (int k = 0; k < fp.Count; k++)
        {
            if (Vec2.Dot(p - fp.Points[k], fp.OutwardNormal(k)) > 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Shows or hides the debug drawing of the walking graph (built on first use).</summary>
    /// <param name="visible">Whether to show it.</param>
    public void SetNavDebug(bool visible)
    {
        _navDebugVisible = visible;
        if (visible && _navDebug is null)
        {
            _navDebug = new NavDebugView(_build.Nav.Graph, _build.Ground);
            AddChild(_navDebug);
        }

        if (_navDebug is not null)
        {
            _navDebug.Visible = visible;
        }
    }

    /// <summary>Finds the character the viewer is looking at: the one nearest the line of sight, within a tolerance that grows with distance.</summary>
    /// <param name="origin">The viewer's position in the world.</param>
    /// <param name="direction">The direction looked along (unit).</param>
    /// <param name="maxMeters">How far to look.</param>
    /// <returns>The description of the character, or null when none is in the line of sight.</returns>
    public string? Pick(Vector3 origin, Vector3 direction, double maxMeters)
    {
        if (_crowd is null || _crowdView is null || !_crowdView.Visible || _clock is null)
        {
            return null;
        }

        int best = -1;
        double bestScore = double.MaxValue;
        foreach ((int index, Vector3 local) in _crowdView.Shown)
        {
            // The ray may pass anywhere along the body, not just through its middle: test the head, the middle and the feet.
            foreach (float up in PickHeights)
            {
                Vector3 toward = ToGlobal(local + new Vector3(0, up, 0)) - origin;
                double along = toward.Dot(direction);
                if (along <= 0 || along > maxMeters)
                {
                    continue;
                }

                double across = (toward - (direction * (float)along)).Length();
                if (across <= PickToleranceMeters + (PickGrowthPerMeter * along) && across < bestScore)
                {
                    bestScore = across;
                    best = index;
                }
            }
        }

        return best < 0 ? null : Describe(best);
    }

    /// <summary>Says who a character is and what it is doing and why, in words.</summary>
    /// <param name="index">The character's index.</param>
    /// <returns>For example <c>Bram Fletcher, baker: walking to the inn (arrives about 18:40)</c>.</returns>
    public string Describe(int index)
    {
        NpcPlan plan = _crowd!.PlanOf(index); // Safe: only called with a crowd.
        NpcStatus status = _crowd.Describe(index);
        string trade = plan.Profession.Length == 0 ? plan.Role.ToString().ToLowerInvariant() : plan.Profession.Replace("profession.", string.Empty).Replace('_', ' ');
        string target = status.TargetNode >= 0 && _build.NodeLabels.TryGetValue(status.TargetNode, out string? label) ? label : "somewhere";
        string until = $"{(int)(status.UntilMinute % GameTime.MinutesPerDay) / 60:00}:{(int)(status.UntilMinute % GameTime.MinutesPerDay) % 60:00}";
        string doing = (status.State, status.Purpose) switch
        {
            (NpcState.Inside, NpcPurpose.Fields) => $"out in the fields (back about {until})",
            (NpcState.Inside, NpcPurpose.Shelter) => $"sheltering from the rain in {target}",
            (NpcState.Inside, NpcPurpose.Work) => $"working in {target} (until about {until})",
            (NpcState.Inside, _) => $"inside {target}",
            (NpcState.Walking, NpcPurpose.Work) => $"walking to work at {target} (arrives about {until})",
            (NpcState.Walking, NpcPurpose.Home) => $"walking home (arrives about {until})",
            (NpcState.Walking, NpcPurpose.Inn) => $"walking to the inn (arrives about {until})",
            (NpcState.Walking, NpcPurpose.Errand) => $"on an errand to {target} (arrives about {until})",
            (NpcState.Walking, NpcPurpose.Shelter) => $"hurrying out of the rain to {target}",
            (NpcState.Walking, NpcPurpose.Fields) => "going out to the fields",
            (NpcState.Walking, NpcPurpose.Patrol) => $"on patrol (next stop about {until})",
            (NpcState.Walking, _) => $"strolling (stops about {until})",
            (_, NpcPurpose.Patrol) => "pausing on patrol",
            (_, NpcPurpose.Work) => "at the post",
            (_, NpcPurpose.About) => "out and about",
            _ => "standing about",
        };
        return $"{(plan.Name.Length == 0 ? $"#{plan.Id}" : plan.Name)}, {trade}: {doing}";
    }

    /// <summary>Whether a guard is challenging the player now (the player is close to a guard at a gate).</summary>
    /// <param name="playerWorld">The player's world position.</param>
    /// <returns><c>true</c> when a guard has stopped the player.</returns>
    public bool Challenged(Vector3 playerWorld)
    {
        if (_crowd is null || _crowdView is null || !_crowdView.Visible || _clock is null)
        {
            return false;
        }

        double now = _clock.Days * GameTime.MinutesPerDay;
        foreach ((int index, _) in _crowdView.Shown)
        {
            if (_crowd.TryGetSnapshot(index, now, out NpcSnapshot s) && s.Alert)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Number of characters that live here.</summary>
    public int CrowdCount => _crowd?.Count ?? 0;

    /// <summary>Number of characters that are outside right now.</summary>
    public int CrowdOutside => _crowd?.CountOutside() ?? 0;

    /// <summary>Whether the walking graph is drawn.</summary>
    public bool NavDebugVisible => _navDebugVisible;

    /// <summary>Settlement this city shows.</summary>
    public Settlement Settlement => _settlement;

    /// <summary>Houses currently shown as detailed buildings.</summary>
    public int NearCount { get; private set; }

    /// <summary>Houses whose far mesh node exists.</summary>
    public int FarCount => _nextFar;

    /// <summary>Total houses in the city.</summary>
    public int BuildingCount => _far.Length;

    /// <summary>Whether the ground, wall and landmark have been added.</summary>
    public bool Attached => _attached;

    /// <summary>Does one frame of work: attaches parts, adds far nodes, scans for near houses and finishes near builds.</summary>
    /// <param name="playerWorld">The player's world position.</param>
    /// <param name="delta">Seconds since the last call.</param>
    /// <param name="rain">The rain intensity at the city, 0 to 1.</param>
    public void Process(Vector3 playerWorld, double delta, double rain = 0)
    {
        if (!_attached)
        {
            Attach();
            return;
        }

        UpdateCrowd(playerWorld, rain, delta);
        UpdateLights(playerWorld, delta);

        for (int added = 0; added < FarNodesPerFrame && _nextFar < _far.Length; added++)
        {
            AddFar(_nextFar++);
        }

        while (_finished.TryDequeue(out NearResult? result))
        {
            Complete(result);
        }

        _scanTimer += delta;
        if (_scanTimer >= NearScanSeconds)
        {
            _scanTimer = 0;
            Scan(playerWorld);
        }
    }

    private void Attach()
    {
        if (_build.Wall is not null && _build.WallShape is not null)
        {
            AddChild(new MeshInstance3D { Mesh = _build.Wall, MaterialOverride = CityMaterials.Solid, Name = "Wall" });
            var body = new StaticBody3D { Name = "WallBody" };
            body.AddChild(new CollisionShape3D { Shape = _build.WallShape });
            AddChild(body);
        }

        foreach (CityBridgeMesh bridge in _build.Bridges)
        {
            AddChild(new MeshInstance3D { Mesh = bridge.Mesh, MaterialOverride = CityMaterials.Solid, Name = "Bridge" });
            var bridgeBody = new StaticBody3D { Name = "BridgeBody" };
            bridgeBody.AddChild(new CollisionShape3D { Shape = bridge.Shape });
            AddChild(bridgeBody);
        }

        if (_build.Landmark is { } landmark)
        {
            // The landmark mesh has its base at 0; the root puts it on the ground at the square's height.
            var root = new Node3D { Name = "LandmarkRoot", Position = new Vector3(0, (float)_build.LandmarkBaseY, 0) };
            AddChild(root);
            root.AddChild(new MeshInstance3D { Mesh = landmark.Solid, MaterialOverride = CityMaterials.Solid, Name = "Landmark" });
            var body = new StaticBody3D { Name = "LandmarkBody" };
            body.AddChild(new CollisionShape3D { Shape = landmark.SolidShape });
            root.AddChild(body);
            if (landmark.Water is not null)
            {
                root.AddChild(new MeshInstance3D { Mesh = landmark.Water, MaterialOverride = CityMaterials.Water, Name = "Water", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            }

            if (landmark.Flames is not null)
            {
                root.AddChild(new MeshInstance3D { Mesh = landmark.Flames, MaterialOverride = CityMaterials.Fire, Name = "Flames", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            }

            if (landmark.FireLight is { } at)
            {
                root.AddChild(new OmniLight3D { Position = at, LightColor = new Color(1f, 0.55f, 0.23f), LightEnergy = 3f, OmniRange = 40f, Name = "FireLight" });
            }

            if (landmark.JetOrigin is { } jet)
            {
                root.AddChild(BuildJets(jet, (float)_build.City.Landmark!.Radius));
            }
        }

        _attached = true;
    }

    // Water thrown from the spout in arcs that fall into the basin.
    private static CpuParticles3D BuildJets(Vector3 origin, float radius) => new()
    {
        Name = "Jets",
        Position = origin,
        Amount = 160,
        Lifetime = 1.5,
        Direction = Vector3.Up,
        Spread = 38,
        InitialVelocityMin = radius * 0.5f,
        InitialVelocityMax = radius * 0.7f,
        Gravity = new Vector3(0, -7, 0),
        Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f, RadialSegments = 4, Rings = 2, Material = new StandardMaterial3D { AlbedoColor = new Color(0.87f, 0.94f, 1f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } },
    };

    private void AddFar(int index)
    {
        // Meshes are built with the floor at 0; the node sits at the floor's height on the ground.
        var node = new MeshInstance3D
        {
            Mesh = _build.Buildings[index].Far,
            MaterialOverride = CityMaterials.Solid,
            Name = $"House{_build.Buildings[index].Building.Id}",
            Position = new Vector3(0, (float)_build.Buildings[index].Site.BaseY, 0),
        };
        node.Visible = !_nearBuilding[index] || _near[index] is null;
        AddChild(node);
        _far[index] = node;
        if (_lamp[index] > LampEpsilon)
        {
            ApplyLamp(index, _lamp[index]);
        }
    }

    // Finds houses near the player that need a detailed build, and detailed houses that are now far away.
    private void Scan(Vector3 playerWorld)
    {
        Vec2 p = _settlement.ToLocal(playerWorld.X, playerWorld.Z);
        _wanted.Clear();
        for (int i = 0; i < _far.Length; i++)
        {
            CityBuilding b = _build.Buildings[i];
            double d = Math.Sqrt(((b.CentreX - p.X) * (b.CentreX - p.X)) + ((b.CentreY - p.Y) * (b.CentreY - p.Y)));
            if (_near[i] is not null && d > NearDropMeters)
            {
                DropNear(i);
            }
            else if (_near[i] is null && !_nearBuilding[i] && d < NearBuildMeters && _far[i] is not null)
            {
                _wanted.Add(i);
            }
        }

        _wanted.Sort((a, c) => Distance(a, p).CompareTo(Distance(c, p)));
        foreach (int index in _wanted)
        {
            if (_tasks >= MaxNearTasks)
            {
                break;
            }

            StartNear(index);
        }
    }

    private double Distance(int index, Vec2 p)
    {
        CityBuilding b = _build.Buildings[index];
        return ((b.CentreX - p.X) * (b.CentreX - p.X)) + ((b.CentreY - p.Y) * (b.CentreY - p.Y));
    }

    private void StartNear(int index)
    {
        _nearBuilding[index] = true;
        _tasks++;
        CityBuilding b = _build.Buildings[index];
        Task.Run(() =>
        {
            BuildingMeshes meshes = BuildingMesher.Near(b.Building, b.Interior, b.Site);
            _finished.Enqueue(new NearResult(
                index,
                PropMeshConverter.ToArrayMesh(meshes.Visual),
                PropMeshConverter.ToCollisionShape(meshes.Collision),
                PropMeshConverter.ToRoomMesh(meshes.Rooms),
                meshes.Glass is null ? null : PropMeshConverter.ToArrayMesh(meshes.Glass)));
        });
    }

    private void Complete(NearResult result)
    {
        _tasks--;
        if (!_nearBuilding[result.Index])
        {
            return; // dropped while it was being built
        }

        var root = new Node3D
        {
            Name = $"Near{_build.Buildings[result.Index].Building.Id}",
            Position = new Vector3(0, (float)_build.Buildings[result.Index].Site.BaseY, 0),
        };
        root.AddChild(new MeshInstance3D { Mesh = result.Visual, MaterialOverride = CityMaterials.Solid });
        var body = new StaticBody3D();
        body.AddChild(new CollisionShape3D { Shape = result.Shape });
        root.AddChild(body);
        if (result.Rooms is not null)
        {
            var interior = new MeshInstance3D { Mesh = result.Rooms, MaterialOverride = CityLighting.Interior, Name = "Rooms" };
            interior.SetInstanceShaderParameter("lamp_level", _lamp[result.Index]);
            interior.SetInstanceShaderParameter("lamp_gain", _gain[result.Index]);
            root.AddChild(interior);
            _interior[result.Index] = interior;
        }

        if (result.Glass is not null)
        {
            root.AddChild(new MeshInstance3D { Mesh = result.Glass, MaterialOverride = CityLighting.Glass, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Name = "Glass" });
        }

        AddChild(root);
        _near[result.Index] = root;
        NearCount++;
        if (_far[result.Index] is { } far)
        {
            far.Visible = false;
        }
    }

    private void DropNear(int index)
    {
        _near[index]?.QueueFree();
        _near[index] = null;
        _interior[index] = null;
        _nearBuilding[index] = false;
        NearCount--;
        if (_far[index] is { } far)
        {
            far.Visible = true;
        }
    }
}
