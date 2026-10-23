using System;

namespace Realm.Sim.Weather;

/// <summary>
/// When and how hard it rains at a place, from how cloudy the region is and how thick its cloud is. Wet, high regions rain often, dry ones
/// hardly ever; the cover swells and fades over the days and showers come and go within a day.
/// </summary>
/// <remarks>Determinism: a pure function of its arguments. Thread-safety: stateless.</remarks>
public static class RainModel
{
    private const double ThicknessBase = 0.55;
    private const double ThicknessGain = 0.45;
    private const double StartsAt = 0.70;
    private const double FullAt = 0.92;
    private const double ShowerPeriodRadiansPerDay = 11.3;
    private const double ShowerSwing = 0.06;

    /// <summary>The rain intensity at a place.</summary>
    /// <param name="cover">Cloud cover of the region, 0 to 1.</param>
    /// <param name="thickness">Cloud thickness of the region, 0 to 1.</param>
    /// <param name="coverBias">The slow swing of the weather over the days (added to the cover).</param>
    /// <param name="days">The game time in days (continuous), for the showers.</param>
    /// <returns>0 when dry up to 1 in a downpour.</returns>
    public static double Intensity(double cover, double thickness, double coverBias, double days)
    {
        double effective = Math.Clamp(cover + coverBias, 0, 1) * (ThicknessBase + (ThicknessGain * Math.Clamp(thickness, 0, 1)));
        effective += ShowerSwing * Math.Sin(days * ShowerPeriodRadiansPerDay);
        double t = Math.Clamp((effective - StartsAt) / (FullAt - StartsAt), 0, 1);
        return t * t * (3 - (2 * t));
    }
}
