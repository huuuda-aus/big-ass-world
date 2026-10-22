using Godot;

namespace Realm.Game.World;

/// <summary>
/// Lets a walking <see cref="CharacterBody3D"/> climb steps and ledges up to <see cref="StepHeightMetres"/> without jumping. Godot's
/// body slides along a ledge instead of climbing it; when the horizontal move is blocked by an upright surface but would be free with
/// the body lifted by the step height, this lifts it and the floor snap in <c>MoveAndSlide</c> lays it down on top of the ledge.
/// </summary>
/// <remarks>Main thread only. Allocates nothing per frame.</remarks>
internal sealed class StepClimber
{
    /// <summary>Highest ledge climbed automatically, in metres.</summary>
    public const float StepHeightMetres = 0.35f;

    private const float WallMaxNormalY = 0.75f; // contact normals steeper than this (about 41 degrees from vertical) are a ledge or wall; the capsule's rounded bottom touches a low riser at a tilted normal
    private const float BlockedShare = 0.3f; // less than this share of the wanted move counts as blocked
    private const float ProbeMetres = 0.25f; // how far ahead the lifted body must be free: the capsule's rounded bottom slips over a ledge by about this much, so it rejects ledges above the step height (0.40 m and up)
    private const float ClearanceMetres = 0.01f; // extra lift so a ledge of exactly the step height clears the contact margin
    private const float MinMotionSquared = 1e-8f;

    private readonly CharacterBody3D _body;
    private readonly KinematicCollision3D _probe = new();

    /// <summary>Creates a climber for a body.</summary>
    /// <param name="body">The walking body.</param>
    public StepClimber(CharacterBody3D body) => _body = body;

    /// <summary>Whether stepping is on (<c>--no-step</c> turns it off for comparison).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Moves the body like <c>MoveAndSlide</c>, then, if it stood on the floor and made almost no headway against an upright ledge it
    /// could step onto, lifts it so the next move carries it up. Ground that merely rises (a slope) is not blocked, so it never triggers.
    /// </summary>
    /// <param name="delta">Seconds since the last physics frame.</param>
    public void MoveAndSlide(float delta)
    {
        Vector3 wanted = new Vector3(_body.Velocity.X, 0, _body.Velocity.Z) * delta;
        Vector3 before = _body.GlobalPosition;
        bool onFloor = _body.IsOnFloor();
        _body.MoveAndSlide();
        if (!Enabled || !onFloor || wanted.LengthSquared() < MinMotionSquared)
        {
            return;
        }

        Vector3 moved = _body.GlobalPosition - before;
        moved.Y = 0;
        if (moved.Length() > wanted.Length() * BlockedShare)
        {
            return; // it got where it was going
        }

        TryLift(wanted.Normalized());
    }

    private void TryLift(Vector3 direction)
    {
        float lift = StepHeightMetres + ClearanceMetres;
        Transform3D at = _body.GlobalTransform;
        if (!_body.TestMove(at, direction * ProbeMetres, _probe) || _probe.GetNormal().Y > WallMaxNormalY)
        {
            return; // nothing upright in the way
        }

        // The lifted body must have room above and must clear the ledge for a little way: the capsule's rounded bottom would otherwise
        // slip over a ledge somewhat taller than the step height.
        if (_body.TestMove(at, Vector3.Up * lift) || _body.TestMove(at.Translated(Vector3.Up * lift), direction * ProbeMetres))
        {
            return;
        }

        _body.Position += Vector3.Up * lift;
    }
}
