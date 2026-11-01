using Realm.Gen.Settlements.Geometry;

namespace Realm.Gen.Settlements.Model;

/// <summary>
/// A ladder from the ground to a watchtower platform. Climbed only through a "use ladder" interaction at either
/// end (an instant transition, not physical climbing): the game places an interaction point at
/// (<see cref="Position"/>, <see cref="BottomY"/>) and another at (<see cref="TopPosition"/>, <see cref="TopY"/>).
/// </summary>
/// <param name="Position">Plan position of the foot of the ladder (and of the ladder prop itself, which rises
/// straight up through the floor's hole).</param>
/// <param name="BottomY">Height of the foot of the ladder above the city origin, in metres.</param>
/// <param name="TopY">Height of the top of the ladder (the platform it reaches) above the city origin, in metres.</param>
/// <param name="TopPosition">Plan position to land at on the platform: offset from <see cref="Position"/> onto solid
/// floor, since the ladder itself rises through the floor's hole, where there is no floor to land on.</param>
public readonly record struct Ladder(Vec2 Position, double BottomY, double TopY, Vec2 TopPosition);
