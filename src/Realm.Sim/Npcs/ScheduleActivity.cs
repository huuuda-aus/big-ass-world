namespace Realm.Sim.Npcs;

/// <summary>What a character does during a block of its day.</summary>
public enum ScheduleActivity
{
    /// <summary>At home, inside.</summary>
    Home = 0,

    /// <summary>At the workplace: inside a service building, or standing at a stall or a post.</summary>
    Work = 1,

    /// <summary>Out and about: standing at one place in the settlement now and then moving to another.</summary>
    About = 2,

    /// <summary>In the inn.</summary>
    Inn = 3,

    /// <summary>Visiting buildings near home or the square.</summary>
    Errand = 4,

    /// <summary>Walking a patrol between the gates and the square, stopping at each.</summary>
    Patrol = 5,

    /// <summary>Out in the fields beyond the gate, out of sight.</summary>
    Fields = 6,
}

/// <summary>One block of a character's day. A block that ends before it starts wraps past midnight.</summary>
/// <param name="FromMinute">Minute of the day the block starts, 0 up to 1,440.</param>
/// <param name="ToMinute">Minute of the day the block ends.</param>
/// <param name="Activity">What the character does.</param>
public readonly record struct ScheduleBlock(int FromMinute, int ToMinute, ScheduleActivity Activity);
