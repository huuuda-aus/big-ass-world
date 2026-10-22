using System;
using Realm.Core.Geometry;
using Xunit;

namespace Realm.Core.Tests;

public class CompassMathTests
{
    [Theory]
    [InlineData(0, 0)]                  // yaw 0 looks north
    [InlineData(90, 270)]               // yaw 90 degrees looks west
    [InlineData(-90, 90)]               // the other way is east
    [InlineData(180, 180)]              // south
    [InlineData(-180, 180)]
    [InlineData(270, 90)]
    [InlineData(450, 270)]
    public void TheBearing_IsMinusTheYaw_WrappedToACircle(double yawDegrees, double bearing) =>
        Assert.Equal(bearing, CompassMath.BearingDegrees(yawDegrees * Math.PI / 180), 6);

    [Theory]
    [InlineData(-10, 350)]
    [InlineData(360, 0)]
    [InlineData(725, 5)]
    public void Normalize_WrapsToZeroUpTo360(double degrees, double expected) => Assert.Equal(expected, CompassMath.Normalize(degrees), 9);

    [Theory]
    [InlineData(350, 10, 20)]   // 10 degrees is 20 to the right of 350
    [InlineData(10, 350, -20)]
    [InlineData(0, 180, 180)]
    [InlineData(90, 90, 0)]
    [InlineData(300, 120, 180)]
    public void Offset_IsTheShortSignedWayRound(double from, double to, double expected) =>
        Assert.Equal(expected, Math.Abs(CompassMath.Offset(from, to)) == 180 ? 180 : CompassMath.Offset(from, to), 9);

    [Theory]
    [InlineData(0, "N")]
    [InlineData(44, "NE")]
    [InlineData(90, "E")]
    [InlineData(135, "SE")]
    [InlineData(180, "S")]
    [InlineData(225, "SW")]
    [InlineData(270, "W")]
    [InlineData(315, "NW")]
    [InlineData(359, "N")]
    public void PointName_IsTheNearestOfEight(double bearing, string name) => Assert.Equal(name, CompassMath.PointName(bearing));

    [Fact]
    public void TheGamesForwardVector_PointsAlongTheBearing()
    {
        // The walker's forward vector for a yaw is (-sin yaw, -cos yaw) in (east, south). A bearing is atan2(east, north).
        foreach (double yawDegrees in new[] { 0.0, 30, 90, 135, 200, 270, -45 })
        {
            double yaw = yawDegrees * Math.PI / 180;
            double east = -Math.Sin(yaw);
            double north = Math.Cos(yaw); // minus the southward component
            Assert.Equal(CompassMath.Normalize(Math.Atan2(east, north) * 180 / Math.PI), CompassMath.BearingDegrees(yaw), 6);
        }
    }
}
