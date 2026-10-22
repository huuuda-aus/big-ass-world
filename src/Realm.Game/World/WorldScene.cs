using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Realm.Core.Geometry;
using Realm.Core.Diagnostics;
using Realm.Game.Bridge;
using Realm.Game.World.Settlements;
using Realm.Gen.Textures;

namespace Realm.Game.World;

/// <summary>
/// A walkable preview of the baked world (CLAUDE.md milestone M1): streams baked terrain regions around the player,
/// with a first-person walker, a fly camera for exploring, a sea-level water plane, sky, fog and a small HUD.
/// </summary>
/// <remarks>
/// Run: <c>godot --path src/Realm.Game</c> after <c>realm bake-terrain</c>. Options (after <c>--</c>):
/// baked (directory), pos (x,z metres), yaw/pitch (degrees), fly, fly-speed, screenshot (png path), map (start with the world map open), render-scale (3D resolution relative to the window, default 0.75),
/// screenshot-after (frames), no-capture, autowalk (seconds of automatic forward walking, then a report), autowalk-run, report (json path), vsync (default on; off for measurement runs). Controls: W A S D move, Shift run, Space jump (fly: rise),
/// Q/E down/up in fly mode, F toggles fly, F1 toggles the HUD, Esc releases or recaptures the mouse.
/// The walker is a <c>CharacterBody3D</c> colliding with Terrain3D's dynamic collision (slope limit 50 degrees).
/// </remarks>
public partial class WorldScene : Node3D
{
    private const float EyeHeight = 1.7f;
    private const float WalkSpeed = 2.75f;
    private const float RunSpeed = 4.5f;
    private const float Gravity = 20f;
    private const float MaxWalkSlopeDegrees = 50f;
    private const float FloorSnapMetres = 0.6f;
    private const float BodyRadius = 0.4f;
    private const float BodyHeight = 1.8f;
    private const float SpawnLift = 0.5f;
    private const float FallThroughMetres = 1.5f;
    private const float JumpSpeed = 6f;
    private const float MouseSensitivity = 0.0025f;
    private const float PitchLimit = 1.5f;
    private const float FarPlane = 3000f;
    private const float WalkFogDensity = 0.010f; // about 5 percent visibility at the 300 m walking view distance
    private const float FlyFogDensity = 0.0010f;
    private const double WalkLoadMeters = 300;
    private const double FlyLoadMeters = 1600;
    private const double UnloadMarginMeters = 100;
    private const float GroundTextureScale = 1.0f; // Terrain3D uv_scale
    private const float AutoSlope = 1.6f;
    private const int HudFontSize = 18; // the HUD is drawn at window resolution
    private const float DefaultRenderScale = 0.75f; // 3D resolution relative to the window (1.0 = full)
    private const float HudMargin = 10;
    private const float EdgeMarginMeters = 10f; // the player cannot get closer than this to the edge of the world
    private const double HudInterval = 0.25;
    private const int AutowalkWarmupFrames = 60;

    private BakedTerrainSource _source = null!; // Safe: set in _Ready before any other method runs.
    private TerrainBridge _bridge = null!; // Safe: set in _Ready.
    private RegionStreamer _streamer = null!; // Safe: set in _Ready.
    private Camera3D _camera = null!; // Safe: set in _Ready.
    private CharacterBody3D _body = null!; // Safe: set in _Ready; origin at the player's feet, the camera is its child.
    private StepClimber _steps = null!; // Safe: set in _Ready next to the body it drives.
    private MeshInstance3D _water = null!; // Safe: set in _Ready.
    private SettlementStreamer? _cities;
    private RoadsLayer? _roads;
    private WorldMap? _map;
    private SubViewport _viewport = null!; // Safe: set in _Ready by BuildViewport.
    private Node3D _world = null!; // Safe: set in _Ready; parent of every 3D node, inside the low-resolution viewport.
    private Label _hud = null!; // Safe: set in _Ready.
    private CompassBar _compass = null!; // Safe: set in _Ready with the HUD.
    private Godot.Environment _environment = null!; // Safe: set in _Ready by BuildEnvironment.
    private LaunchArguments _args = new(Array.Empty<string>());
    private float _renderScale = DefaultRenderScale;
    private float _yaw;
    private float _pitch;
    private bool _fly;
    private float _flySpeed;
    private long _frame;
    private double _hudTimer;
    private double _smoothedFrameMs = 4;
    private ulong _lastTicks;
    private string? _screenshotPath;
    private long _screenshotFrame;
    private float _lastUpdateMs;
    private double _autowalkSeconds;
    private double _autowalkElapsed;
    private readonly List<double> _frameTimesMs = new();
    private readonly List<long> _frameNumbers = new();
    private readonly List<double> _frameClockSeconds = new();
    private double _worstGroundError;
    private double _maxStepMetres;
    private int _gc0;
    private int _gc1;
    private int _gc2;

    /// <inheritdoc />
    public override void _Ready()
    {
        _args = new LaunchArguments(OS.GetCmdlineUserArgs());

        // Measurement runs (auto-walk, screenshots) switch vsync off so a hidden or unfocused window cannot stall the
        // frame loop and presentation hiccups do not show up as frame-time noise; normal play keeps vsync on.
        bool measuring = _args.Has("autowalk") || _args.Has("screenshot");
        DisplayServer.WindowSetVsyncMode(_args.Flag("vsync", !measuring)
            ? DisplayServer.VSyncMode.Enabled
            : DisplayServer.VSyncMode.Disabled);
        string bakedDirectory = _args.Text("baked", Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "baked", "terrain")));

        try
        {
            _source = BakedTerrainSource.Open(bakedDirectory);
        }
        catch (FileNotFoundException e)
        {
            GD.PushError(e.Message);
            GD.Print(e.Message);
            GetTree().Quit(1);
            return;
        }

        BuildViewport();
        TerrainManifestInfo(out int regionSize, out float spacing);
        _camera = new Camera3D { Far = FarPlane, Current = true, Fov = 75 };
        _body = BuildBody();
        _steps = new StepClimber(_body);
        _world.AddChild(_body);
        _body.AddChild(_camera);
        _camera.Position = new Vector3(0, EyeHeight, 0);
        BuildEnvironment();

        _bridge = TerrainBridge.Create(new TerrainSettings(regionSize, spacing, TerrainCollisionMode.DynamicGame,
            (int)_args.Number("collision-shape", 8), (int)_args.Number("collision-radius", 16)), _camera);
        _world.AddChild(_bridge.Node);
        if (_args.Flag("colour-map", false))
        {
            _bridge.ShowColourMapOnly(true);
        }
        else
        {
            _bridge.SetSurfaceTextures(
                new[] { SurfaceTextureGenerator.Grass(_source.Manifest.Seed), SurfaceTextureGenerator.Rock(_source.Manifest.Seed), SurfaceTextureGenerator.Water(_source.Manifest.Seed) },
                GroundTextureScale,
                AutoSlope);
        }

        _streamer = new RegionStreamer(
            _bridge,
            _source.Load,
            ViewOptions(_args.Flag("fly", false)));

        double startX = _source.Manifest.SpawnX;
        double startZ = _source.Manifest.SpawnZ;
        if (_args.Has("pos"))
        {
            string[] parts = _args.Text("pos", string.Empty).Split(',');
            if (parts.Length == 2
                && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double px)
                && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pz))
            {
                startX = px;
                startZ = pz;
            }
        }

        _yaw = Mathf.DegToRad((float)_args.Number("yaw", 0));
        _pitch = Mathf.DegToRad((float)_args.Number("pitch", 0));
        _fly = _args.Flag("fly", false);
        ApplyViewMode();
        _flySpeed = (float)_args.Number("fly-speed", 80);

        _body.Position = new Vector3((float)startX, 0, (float)startZ);
        _streamer.LoadAllBlocking(_body.Position);
        float ground = _bridge.HeightAt(_body.Position);
        float lift = _fly ? (float)_args.Number("altitude", 120) : SpawnLift;
        _body.Position = new Vector3((float)startX, (float.IsNaN(ground) ? 0 : ground) + lift, (float)startZ);
        ApplyLook();

        if (!_args.Flag("no-cities", false))
        {
            _cities = SettlementStreamer.Open(_source.Directory);
            if (_cities is not null)
            {
                _world.AddChild(_cities);
                GD.Print($"[world] {_cities.Settlements.Count} settlement(s)");
            }
        }

        WaterLayer? water = WaterLayer.Open(_source.Directory, _source.Manifest.Seed);
        if (water is not null)
        {
            _world.AddChild(water);
        }

        _roads = RoadsLayer.Open(_source.Directory);
        if (_roads is not null)
        {
            _world.AddChild(_roads);
        }

        BuildHud();
        if (_cities is not null)
        {
            _map = WorldMap.Open(_source.Directory, _source.Manifest.WorldSizeMeters, _cities.Settlements, _roads);
            if (_map is not null)
            {
                AddChild(_map);
                GetWindow().SizeChanged += _map.Rebuild;
                _map.Visible = _args.Flag("map", false);
            }
        }

        _hud.Visible = !_args.Flag("no-hud", false);
        _compass.Visible = _hud.Visible;
        _water.Visible = !_args.Flag("no-water", false);
        _steps.Enabled = !_args.Flag("no-step", false);
        if (!_args.Flag("no-capture", false) && !_args.Has("screenshot"))
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }

        _autowalkSeconds = _args.Number("autowalk", 0);
        _gc0 = GC.CollectionCount(0);
        _gc1 = GC.CollectionCount(1);
        _gc2 = GC.CollectionCount(2);
        _screenshotPath = _args.Has("screenshot") ? _args.Text("screenshot", string.Empty) : null;
        _screenshotFrame = (long)_args.Number("screenshot-after", 90);
        GD.Print($"[world] {_source.Manifest.World}: {_source.Manifest.RegionsPerAxis} x {_source.Manifest.RegionsPerAxis} regions of {_source.Manifest.RegionExtentMeters} m, start ({startX:F0}, {startZ:F0}), {_streamer.LoadedCount} region(s) loaded");
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= motion.Relative.X * MouseSensitivity;
            _pitch = Mathf.Clamp(_pitch - (motion.Relative.Y * MouseSensitivity), -PitchLimit, PitchLimit);
            ApplyLook();
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.PhysicalKeycode)
            {
                case Key.F:
                    _fly = !_fly;
                    _body.Velocity = Vector3.Zero;
                    ApplyViewMode();
                    break;
                case Key.M when _map is not null:
                    _map.Visible = !_map.Visible;
                    break;
                case Key.F1:
                    _hud.Visible = !_hud.Visible;
                    _compass.Visible = _hud.Visible;
                    break;
                case Key.Escape:
                    Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                        ? Input.MouseModeEnum.Visible
                        : Input.MouseModeEnum.Captured;
                    break;
            }
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        if (_lastTicks != 0)
        {
            double frameMs = (now - _lastTicks) / 1000.0;
            _smoothedFrameMs = (_smoothedFrameMs * 0.95) + (frameMs * 0.05);
            if (_autowalkSeconds > 0 && _frame > AutowalkWarmupFrames)
            {
                _frameTimesMs.Add(frameMs);
                _frameNumbers.Add(_frame);
                _frameClockSeconds.Add(_autowalkElapsed);
            }
        }

        _lastTicks = now;
        _frame++;

        Vector3 before = _body.Position;
        Move((float)delta);
        if (_autowalkSeconds > 0 && _frame > AutowalkWarmupFrames)
        {
            _autowalkElapsed += delta;
            _maxStepMetres = Math.Max(_maxStepMetres, (_body.Position - before).Length());
            float groundHere = _bridge.HeightAt(new Vector3(_body.Position.X, 0, _body.Position.Z));
            if (!float.IsNaN(groundHere) && !_fly)
            {
                _worstGroundError = Math.Max(_worstGroundError, Math.Abs(_body.Position.Y - groundHere));
            }

            if (_autowalkElapsed >= _autowalkSeconds)
            {
                FinishAutowalk();
                return;
            }
        }

        _compass.SetHeading(CompassMath.BearingDegrees(_yaw)); // every frame: it turns with the mouse
        _streamer.Update(_body.Position, _frame);
        _cities?.Process(_body.Position, delta, _streamer.Options.LoadDistanceMeters);
        if (_map is { Visible: true })
        {
            _map.SetPlayer(_body.Position.X, _body.Position.Z, _yaw);
        }
        if (_streamer.Events.Count > 0)
        {
            for (int i = _streamer.Events.Count - 1; i >= 0; i--)
            {
                if (_streamer.Events[i].Kind == World.StreamEventKind.Update)
                {
                    _lastUpdateMs = (float)_streamer.Events[i].Milliseconds;
                    break;
                }
            }
        }

        ClampToWorld();

        _hudTimer += delta;
        if (_hudTimer >= HudInterval && !_args.Flag("no-hud", false))
        {
            _hudTimer = 0;
            UpdateHud();
        }

        if (_screenshotPath is not null && _frame >= _screenshotFrame)
        {
            Image image = GetViewport().GetTexture().GetImage();
            image.SavePng(_screenshotPath);
            GD.Print($"[world] screenshot saved to {_screenshotPath} ({image.GetWidth()} x {image.GetHeight()})");
            GetTree().Quit();
        }
    }

    private void FinishAutowalk()
    {
        SetProcess(false);
        FrameStatsSummary stats = FrameStats.Summarise(_frameTimesMs);
        var events = _streamer.Events;
        var worst = Enumerable.Range(0, _frameTimesMs.Count)
            .OrderByDescending(i => _frameTimesMs[i])
            .Take(10)
            .Select(i => new { frame = _frameNumbers[i], atSeconds = Math.Round(_frameClockSeconds[i], 2), ms = Math.Round(_frameTimesMs[i], 2) })
            .ToList();
        var report = new
        {
            engine = Engine.GetVersionInfo()["string"].AsString(),
            manifestChecksum = _source.Manifest.Checksum,
            config = _args.All,
            walk = new
            {
                seconds = Math.Round(_autowalkElapsed, 1),
                speed = _args.Flag("autowalk-run", false) ? RunSpeed : WalkSpeed,
                end = new { x = Math.Round(_body.Position.X, 1), y = Math.Round(_body.Position.Y, 2), z = Math.Round(_body.Position.Z, 1) },
                worstHeightAboveGroundErrorMetres = Math.Round(_worstGroundError, 3),
                largestSingleFrameStepMetres = Math.Round(_maxStepMetres, 3),
            },
            frameMs = new
            {
                frames = stats.Count,
                mean = Math.Round(stats.MeanMs, 3),
                p50 = Math.Round(stats.P50Ms, 3),
                p95 = Math.Round(stats.P95Ms, 3),
                p99 = Math.Round(stats.P99Ms, 3),
                p999 = Math.Round(stats.P999Ms, 3),
                max = Math.Round(stats.MaxMs, 3),
                hitches = stats.HitchCount,
                hitchThresholdMs = Math.Round(stats.HitchThresholdMs, 2),
                worst,
            },
            streaming = new
            {
                adds = events.Count(e => e.Kind == StreamEventKind.Add),
                removes = events.Count(e => e.Kind == StreamEventKind.Remove),
                rebuilds = events.Count(e => e.Kind == StreamEventKind.Update),
                rebuildMsMax = events.Where(e => e.Kind == StreamEventKind.Update).Select(e => Math.Round(e.Milliseconds, 2)).DefaultIfEmpty(0).Max(),
                loadedAtEnd = _streamer.LoadedCount,
                discarded = _streamer.DiscardedCount,
            },
            gc = new { gen0 = GC.CollectionCount(0) - _gc0, gen1 = GC.CollectionCount(1) - _gc1, gen2 = GC.CollectionCount(2) - _gc2 },
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        GD.Print(json);
        if (_args.Has("report"))
        {
            File.WriteAllText(_args.Text("report", string.Empty), json);
        }

        GetTree().Quit();
    }

    // A movement direction is held when either of its two keys (letter or arrow) is down.
    private static bool Held(Key letter, Key arrow) => Input.IsPhysicalKeyPressed(letter) || Input.IsPhysicalKeyPressed(arrow);

    private void Move(float delta)
    {
        bool sprint = Input.IsPhysicalKeyPressed(Key.Shift) || (_autowalkSeconds > 0 && _args.Flag("autowalk-run", false));
        float forwardInput = _autowalkSeconds > 0
            ? 1
            : (Held(Key.W, Key.Up) ? 1 : 0) - (Held(Key.S, Key.Down) ? 1 : 0);
        float sideInput = (Held(Key.D, Key.Right) ? 1 : 0) - (Held(Key.A, Key.Left) ? 1 : 0);

        if (_fly)
        {
            float speed = _flySpeed * (sprint ? 5f : 1f);
            Basis basis = _camera.GlobalTransform.Basis;
            Vector3 direction = (-basis.Z * forwardInput) + (basis.X * sideInput);
            float rise = (Input.IsPhysicalKeyPressed(Key.E) || Input.IsPhysicalKeyPressed(Key.Space) ? 1 : 0)
                         - (Input.IsPhysicalKeyPressed(Key.Q) ? 1 : 0);
            _body.Position += (direction + (Vector3.Up * rise)) * speed * delta;
            return;
        }

        float ground = _bridge.HeightAt(new Vector3(_body.Position.X, 0, _body.Position.Z));
        if (float.IsNaN(ground))
        {
            return; // no terrain under the player yet: hold still until the region arrives
        }

        // Terrain3D builds collision shapes around the camera a frame or two after regions appear; until then a body
        // can fall through, so put it back on the heightmap.
        if (_body.Position.Y < ground - FallThroughMetres)
        {
            _body.Position = new Vector3(_body.Position.X, ground + SpawnLift, _body.Position.Z);
            _body.Velocity = Vector3.Zero;
        }

        float walk = sprint ? RunSpeed : WalkSpeed;
        var forward = new Vector3(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
        var right = new Vector3(Mathf.Cos(_yaw), 0, -Mathf.Sin(_yaw));
        Vector3 move = (forward * forwardInput) + (right * sideInput);
        if (move.LengthSquared() > 1)
        {
            move = move.Normalized();
        }

        Vector3 velocity = _body.Velocity;
        velocity.X = move.X * walk;
        velocity.Z = move.Z * walk;
        if (_body.IsOnFloor())
        {
            velocity.Y = Input.IsPhysicalKeyPressed(Key.Space) ? JumpSpeed : 0;
        }
        else
        {
            velocity.Y -= Gravity * delta;
        }

        _body.Velocity = velocity;
        _steps.MoveAndSlide(delta);
    }

    internal static CharacterBody3D BuildBody()
    {
        var body = new CharacterBody3D
        {
            FloorMaxAngle = Mathf.DegToRad(MaxWalkSlopeDegrees),
            FloorSnapLength = FloorSnapMetres,
            FloorStopOnSlope = true,
        };
        body.AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = BodyRadius, Height = BodyHeight },
            Position = new Vector3(0, BodyHeight / 2, 0),
        });
        return body;
    }

    // Walking keeps a small load set and short fog; flying loads further and uses thin fog so the world can be surveyed.
    private StreamingOptions ViewOptions(bool fly)
    {
        double load = _args.Number("view-distance", fly ? FlyLoadMeters : WalkLoadMeters);
        return new StreamingOptions(
            LoadDistanceMeters: load,
            UnloadDistanceMeters: load + UnloadMarginMeters,
            MaxInFlight: 3,
            MaxCommitsPerFrame: 6,
            MaxRemovalsPerFrame: 6,
            CommitBudgetMs: 4,
            UpdatePerAdd: false,
            UpdatePerRemove: false,
            BuildOnWorker: true,
            MinRegion: _source.Manifest.MinRegion,
            MaxRegion: _source.Manifest.MaxRegion);
    }

    // An invisible wall at the edge of the baked world (where there is no terrain).
    private void ClampToWorld()
    {
        float low = (_source.Manifest.MinRegion * _source.Manifest.RegionExtentMeters) + EdgeMarginMeters;
        float high = ((_source.Manifest.MaxRegion + 1) * _source.Manifest.RegionExtentMeters) - EdgeMarginMeters;
        Vector3 p = _body.Position;
        _body.Position = new Vector3(Mathf.Clamp(p.X, low, high), p.Y, Mathf.Clamp(p.Z, low, high));
    }

    private string CityHudLine()
    {
        if (_cities is null)
        {
            return string.Empty;
        }

        int near = 0;
        foreach (CityInstance city in _cities.Cities)
        {
            near += city.NearCount;
        }

        Realm.Gen.Settlements.Placement.Settlement? inside = _cities.InsideWall(_body.Position.X, _body.Position.Z);
        Realm.Gen.Settlements.Placement.Settlement? nearest = null;
        double best = double.MaxValue;
        foreach (Realm.Gen.Settlements.Placement.Settlement s in _cities.Settlements)
        {
            double d = s.DistanceTo(_body.Position.X, _body.Position.Z);
            if (d < best)
            {
                best = d;
                nearest = s;
            }
        }

        string place = inside is not null
            ? $"in {inside.Name}"
            : nearest is null ? string.Empty : $"{nearest.Name} is {best / 1000:F1} km {Compass(nearest.X - _body.Position.X, nearest.Z - _body.Position.Z)}";
        return $"{place}   ({_cities.Cities.Count} built, {_cities.BuildingCount} building, {near} detailed houses)   M map\n";
    }

    // Eight-point compass direction; north is minus Z.
    private static string Compass(double dx, double dz)
    {
        string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        double angle = Math.Atan2(dx, -dz); // 0 = north, increasing clockwise
        int index = (int)Math.Round(angle / (Math.PI / 4));
        return names[((index % 8) + 8) % 8];
    }

    private void ApplyViewMode()
    {
        _streamer.Options = ViewOptions(_fly);
        _environment.FogDensity = (float)_args.Number("fog", _fly ? FlyFogDensity : WalkFogDensity);
    }

    private void ApplyLook() => _camera.Rotation = new Vector3(_pitch, _yaw, 0);

    private void TerrainManifestInfo(out int regionSize, out float spacing)
    {
        regionSize = _source.Manifest.RegionSize;
        spacing = _source.Manifest.SpacingMeters;
    }

    // The 3D world renders into a SubViewport at a fraction of the window size and is stretched with nearest-neighbour
    // filtering (the retro look, CLAUDE.md section 6.7); the HUD is drawn at full window resolution so text stays readable.
    private void BuildViewport()
    {
        _viewport = new SubViewport { HandleInputLocally = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        _world = new Node3D();
        _viewport.AddChild(_world);
        AddChild(_viewport);

        var screen = new TextureRect
        {
            Texture = _viewport.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var layer = new CanvasLayer { Layer = -1 };
        layer.AddChild(screen);
        AddChild(layer);

        _renderScale = (float)_args.Number("render-scale", DefaultRenderScale);
        ResizeViewport();
        GetWindow().SizeChanged += ResizeViewport;
    }

    private void ResizeViewport()
    {
        Vector2I window = GetWindow().Size;
        _viewport.Size = new Vector2I(
            Math.Max(160, (int)Math.Round(window.X * _renderScale)),
            Math.Max(90, (int)Math.Round(window.Y * _renderScale)));
    }

    private void BuildEnvironment()
    {
        _world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-45, -135, 0), LightEnergy = 1.1f, ShadowEnabled = true, DirectionalShadowMaxDistance = 150f });
        var fog = new Color(0.64f, 0.74f, 0.84f);
        _environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky
                {
                    SkyMaterial = new ProceduralSkyMaterial
                    {
                        SkyTopColor = new Color(0.30f, 0.50f, 0.82f),
                        SkyHorizonColor = fog,
                        GroundHorizonColor = fog,
                        GroundBottomColor = new Color(0.30f, 0.36f, 0.42f),
                    },
                },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                FogEnabled = true,
                FogLightColor = fog,
                FogDensity = WalkFogDensity,
                FogSkyAffect = 0.6f,
            };
        _world.AddChild(new WorldEnvironment { Environment = _environment });

        _water = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(_source.Manifest.WorldSizeMeters, _source.Manifest.WorldSizeMeters) }, // exactly the world: the sea stops where the terrain does
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.08f, 0.26f, 0.50f, 0.93f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 0.12f,
                Metallic = 0.1f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        _world.AddChild(_water);
    }

    private void BuildHud()
    {
        var layer = new CanvasLayer();
        _compass = new CompassBar();
        layer.AddChild(_compass);
        _hud = new Label { Position = new Vector2(HudMargin, HudMargin + CompassBar.StripHeight + HudMargin) }; // below the compass strip
        _hud.AddThemeFontSizeOverride("font_size", HudFontSize);
        _hud.AddThemeColorOverride("font_outline_color", Colors.Black);
        _hud.AddThemeConstantOverride("outline_size", 6);
        layer.AddChild(_hud);
        AddChild(layer);
        UpdateHud();
    }

    private void UpdateHud()
    {
        _compass.SetHeading(CompassMath.BearingDegrees(_yaw));
        Vector3 p = _body.Position;
        Vector2I region = _bridge.RegionAt(p);
        float ground = _bridge.HeightAt(new Vector3(p.X, 0, p.Z));
        string groundText = float.IsNaN(ground) ? "?" : $"{ground:F0} m";
        _hud.Text =
            $"{Engine.GetFramesPerSecond()} fps   {_smoothedFrameMs:F1} ms   {(_fly ? "FLY" : "WALK")}\n" +
            $"x {p.X:F0}  z {p.Z:F0}  ground {groundText}  region ({region.X}, {region.Y})\n" +
            $"terrain: {_streamer.LoadedCount} loaded, {_streamer.PendingCount} loading, last rebuild {_lastUpdateMs:F1} ms\n" +
            CityHudLine() +
            "WASD/arrows move  Shift run  Space jump  F fly  F1 hide  Esc mouse";
    }
}
