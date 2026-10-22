using System;
using System.Numerics;
using Realm.Core.Geometry;
using Xunit;

namespace Realm.Core.Tests;

public class CelestialMathTests
{
    private static readonly CelestialSettings Flat = new() { LatitudeDegrees = 40, MoonInclinationDegrees = 0, MoonPhaseAtStart = 0 };

    [Fact]
    public void AtNoon_TheSunIsDueSouth_AtLatitudeComplementElevation()
    {
        CelestialMath.Sky sky = CelestialMath.Compute(0.5, 0, Flat);

        Assert.Equal(Math.Sin(50 * Math.PI / 180), (double)sky.Sun.Y, 5);
        Assert.Equal(0.0, (double)sky.Sun.X, 5);
        Assert.True(sky.Sun.Z > 0, "the sun should be to the south (positive Z) at noon");
        Assert.Equal(1.0, (double)sky.Sun.Length(), 5);
    }

    [Fact]
    public void TheSun_RisesInTheEast_AndSetsInTheWest_AndIsDownAtMidnight()
    {
        Vector3 dawn = CelestialMath.Compute(0.25, 0, Flat).Sun;
        Vector3 dusk = CelestialMath.Compute(0.75, 0, Flat).Sun;
        Vector3 midnight = CelestialMath.Compute(0.0, 0, Flat).Sun;

        Assert.Equal(1.0, (double)dawn.X, 5);
        Assert.Equal(0.0, (double)dawn.Y, 5);
        Assert.Equal(-1.0, (double)dusk.X, 5);
        Assert.Equal(0.0, (double)dusk.Y, 5);
        Assert.True(midnight.Y < -0.5);
    }

    [Fact]
    public void TheSun_RisesThroughTheMorning_AndFallsThroughTheAfternoon()
    {
        double previous = double.MinValue;
        for (double t = 0.25; t <= 0.5; t += 0.01)
        {
            double up = CelestialMath.Compute(t, 0, Flat).Sun.Y;
            Assert.True(up >= previous - 1e-9);
            previous = up;
        }

        Assert.True(CelestialMath.Compute(0.6, 0, Flat).Sun.Y < CelestialMath.Compute(0.5, 0, Flat).Sun.Y);
    }

    [Fact]
    public void TheSunPath_IsTheSameEveryDay_ThereAreNoSeasons()
    {
        Assert.Equal(CelestialMath.Compute(0.37, 0).Sun, CelestialMath.Compute(0.37, 200).Sun);
    }

    [Fact]
    public void AtNewMoon_TheMoonIsWithTheSun_AndAtFullMoon_OppositeIt()
    {
        CelestialMath.Sky newMoon = CelestialMath.Compute(0.3, 0, Flat);
        CelestialMath.Sky full = CelestialMath.Compute(0.3, 0, Flat with { MoonPhaseAtStart = 0.5 });

        Assert.Equal(0, newMoon.MoonPhase, 9);
        Assert.True(Vector3.Distance(newMoon.Sun, newMoon.Moon) < 1e-4);
        Assert.Equal(0.5, full.MoonPhase, 9);
        Assert.True(Vector3.Distance(full.Sun, -full.Moon) < 1e-4);
    }

    [Fact]
    public void ThePhase_AdvancesWithTheDays_AndWrapsAfterOneCycle()
    {
        var settings = new CelestialSettings { MoonCycleDays = 12, MoonPhaseAtStart = 0.1 };

        Assert.Equal(0.1, CelestialMath.Compute(0.5, 0, settings).MoonPhase, 9);
        Assert.Equal(0.1 + (3.0 / 12), CelestialMath.Compute(0.5, 3, settings).MoonPhase, 9);
        Assert.Equal(0.1, CelestialMath.Compute(0.5, 12, settings).MoonPhase, 9);
    }

    [Fact]
    public void Illumination_IsZeroAtNew_OneAtFull_AndSymmetric()
    {
        Assert.Equal(0, CelestialMath.Illumination(0), 9);
        Assert.Equal(1, CelestialMath.Illumination(0.5), 9);
        Assert.Equal(CelestialMath.Illumination(0.2), CelestialMath.Illumination(0.8), 9);
    }

    [Fact]
    public void Daylight_IsDarkAtNight_BrightAtNoon_AndRisesThroughDawn()
    {
        Assert.Equal(0, CelestialMath.Daylight(-0.5), 9);
        Assert.Equal(1, CelestialMath.Daylight(0.9), 9);
        Assert.InRange(CelestialMath.Daylight(0), 0.05, 0.95);
        Assert.True(CelestialMath.Daylight(0.1) > CelestialMath.Daylight(-0.05));
    }
}
