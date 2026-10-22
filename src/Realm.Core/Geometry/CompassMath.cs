using System;

namespace Realm.Core.Geometry;

/// <summary>
/// Compass maths for the game's view direction. The game's yaw is 0 when looking north (toward smaller Z) and grows to the west
/// (yaw 90 degrees looks west), so a compass bearing, which grows clockwise from north through east, is minus the yaw.
/// </summary>
/// <remarks>Determinism: pure functions. Thread-safety: stateless.</remarks>
public static class CompassMath
{
    private static readonly string[] Names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    /// <summary>The compass bearing for a yaw.</summary>
    /// <param name="yawRadians">The view yaw: 0 looks north, positive turns toward the west.</param>
    /// <returns>Degrees clockwise from north, in 0 up to (not including) 360.</returns>
    public static double BearingDegrees(double yawRadians) => Normalize(-yawRadians * 180.0 / Math.PI);

    /// <summary>Wraps an angle to 0 up to (not including) 360 degrees.</summary>
    /// <param name="degrees">Any angle in degrees.</param>
    /// <returns>The equivalent angle in 0 to 360.</returns>
    public static double Normalize(double degrees)
    {
        double wrapped = degrees % 360.0;
        return wrapped < 0 ? wrapped + 360.0 : wrapped;
    }

    /// <summary>The signed difference from one bearing to another, the short way round.</summary>
    /// <param name="fromDegrees">The bearing the view looks along.</param>
    /// <param name="toDegrees">The bearing of a mark.</param>
    /// <returns>Degrees from -180 up to 180: positive when the mark lies to the right (clockwise) of the view.</returns>
    public static double Offset(double fromDegrees, double toDegrees)
    {
        double d = Normalize(toDegrees - fromDegrees);
        return d > 180.0 ? d - 360.0 : d;
    }

    /// <summary>The name of the nearest of the eight compass points.</summary>
    /// <param name="bearingDegrees">A bearing in degrees.</param>
    /// <returns><c>N</c>, <c>NE</c>, <c>E</c>, <c>SE</c>, <c>S</c>, <c>SW</c>, <c>W</c> or <c>NW</c>.</returns>
    public static string PointName(double bearingDegrees) => Names[(int)Math.Round(Normalize(bearingDegrees) / 45.0) % Names.Length];
}
