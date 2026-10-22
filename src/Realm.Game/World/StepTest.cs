using System.Collections.Generic;
using Godot;

namespace Realm.Game.World;

/// <summary>
/// A physics check for step climbing: the walker (built exactly like the game's) walks at a staircase and the scene reports how high
/// it got. Run with <c>godot --headless --path src/Realm.Game res://scenes/step_test.tscn</c>; the exit code is 1 when a case fails.
/// </summary>
public sealed partial class StepTest : Node3D
{
    private const float WalkSpeed = 2.75f;
    private const float Gravity = 20f;
    private const float RunSeconds = 6f;
    private const float StepWidth = 1.8f;
    private const int StepCount = 6;

    private sealed record Case(string Name, float Riser, float Tread, bool ShouldClimb, float DoorWidth = 0);

    private readonly List<Case> _cases = new()
    {
        new("riser 0.10", 0.10f, 0.4f, true),
        new("riser 0.20", 0.20f, 0.4f, true),
        new("riser 0.26 (shipped steps)", 0.26f, 0.4f, true),
        new("riser 0.30", 0.30f, 0.4f, true),
        new("riser 0.35", 0.35f, 0.4f, true),
        new("riser 0.26 tread 0.25", 0.26f, 0.25f, true),
        new("riser 0.35 tread 0.25", 0.35f, 0.25f, true),
        new("riser 0.26 into a 1.1 m door", 0.26f, 0.4f, true, 1.1f),
        new("riser 0.35 into a 1.1 m door", 0.35f, 0.4f, true, 1.1f),
        new("riser 0.40 (too tall)", 0.40f, 0.4f, false),
        new("riser 0.45 (too tall)", 0.45f, 0.4f, false),
        new("riser 0.60 (too tall)", 0.60f, 0.4f, false),
    };

    private int _case = -1;
    private int _frames;
    private float _maxY;
    private float _maxX;
    private CharacterBody3D? _body;
    private StepClimber? _steps;
    private Node3D? _rig;
    private bool _failed;

    /// <inheritdoc />
    public override void _Ready() => NextCase();

    /// <inheritdoc />
    public override void _PhysicsProcess(double delta)
    {
        if (_body is null || _steps is null)
        {
            return;
        }

        _frames++;
        float dt = (float)delta;
        Vector3 velocity = _body.Velocity;
        velocity.X = WalkSpeed;
        velocity.Z = 0;
        velocity.Y = _body.IsOnFloor() ? 0 : velocity.Y - (Gravity * dt);
        _body.Velocity = velocity;
        _steps.MoveAndSlide(dt);
        _maxY = Mathf.Max(_maxY, _body.Position.Y);
        _maxX = Mathf.Max(_maxX, _body.Position.X);
        if (_frames * dt >= RunSeconds)
        {
            Finish();
        }
    }

    private void Finish()
    {
        Case c = _cases[_case];
        float top = StepCount * c.Riser;
        bool climbed = _maxY >= top - 0.05f && (c.DoorWidth <= 0 || _maxX > StepCount * c.Tread + 1f);
        bool ok = climbed == c.ShouldClimb;
        _failed |= !ok;
        GD.Print($"[step-test] {(ok ? "PASS" : "FAIL")} {c.Name}: top {top:F2} m, reached {_maxY:F2} m, x {_maxX:F2}, {(climbed ? "climbed" : "blocked")}");
        NextCase();
    }

    private void NextCase()
    {
        _rig?.QueueFree();
        _case++;
        if (_case >= _cases.Count)
        {
            GD.Print(_failed ? "[step-test] FAILED" : "[step-test] all passed");
            GetTree().Quit(_failed ? 1 : 0);
            return;
        }

        Case c = _cases[_case];
        _rig = new Node3D();
        AddChild(_rig);

        // Flat ground at y = 0, then a staircase from x = 0, then (for door cases) a floor at the top with walls either side.
        AddBox(new Vector3(-20, -1, -10), new Vector3(60, 0, 10));
        for (int i = 0; i < StepCount; i++)
        {
            AddBox(new Vector3(i * c.Tread, -1, -StepWidth / 2), new Vector3((i + 1) * c.Tread, (i + 1) * c.Riser, StepWidth / 2));
        }

        float top = StepCount * c.Riser;
        float end = StepCount * c.Tread;
        AddBox(new Vector3(end, -1, -StepWidth / 2), new Vector3(end + 6, top, StepWidth / 2));
        if (c.DoorWidth > 0)
        {
            // A wall across the top with a door in it, as a house front has: the steps lead through the opening.
            float half = c.DoorWidth / 2;
            AddBox(new Vector3(end - 0.3f, top, -5), new Vector3(end, top + 3, -half));
            AddBox(new Vector3(end - 0.3f, top, half), new Vector3(end, top + 3, 5));
            AddBox(new Vector3(end - 0.3f, top + 2.2f, -half), new Vector3(end, top + 3, half));
        }

        _body = WorldScene.BuildBody();
        _body.Position = new Vector3(-2, 0.05f, 0);
        _rig.AddChild(_body);
        _steps = new StepClimber(_body);
        _frames = 0;
        _maxY = 0;
        _maxX = -2;
    }

    private void AddBox(Vector3 min, Vector3 max)
    {
        var body = new StaticBody3D { Position = (min + max) / 2 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = max - min } });
        _rig!.AddChild(body);
    }
}
