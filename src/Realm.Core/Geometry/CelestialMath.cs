using System;
using System.Numerics;

namespace Realm.Core.Geometry;

/// <summary>
/// Where the sun and the moon are. World axes follow the game: X east, Y up, Z south (north is toward smaller Z). The sun lies on the
/// celestial equator (no seasons) and turns once a day about the polar axis; the moon lags behind it by its phase.
/// </summary>
/// <remarks>Determinism: pure functions of the time and the settings. Thread-safety: stateless.</remarks>
public static class CelestialMath
{
    private const double Tau = 2 * Math.PI;
    private const double NoonFraction = 0.5;
    private const double DegreesToRadians = Math.PI / 180.0;

    /// <summary>The state of the sky at one moment.</summary>
    /// <param name="Sun">Unit vector from the viewer toward the sun.</param>
    /// <param name="Moon">Unit vector from the viewer toward the moon.</param>
    /// <param name="MoonPhase">0 new, 0.5 full, back to 1 (new again).</param>
    /// <param name="HourAngleRadians">The sun's hour angle: 0 at noon, growing through the afternoon; the star field turns with it.</param>
    public readonly record struct Sky(Vector3 Sun, Vector3 Moon, double MoonPhase, double HourAngleRadians);

    /// <summary>Computes the sky.</summary>
    /// <param name="timeOfDay">Fraction of the day from midnight: 0.25 is dawn, 0.5 noon, 0.75 dusk.</param>
    /// <param name="day">Day count including the fraction (continuous), used for the moon's cycle.</param>
    /// <param name="settings">The sky's fixed facts; defaults when null.</param>
    /// <returns>The sun and moon directions and the moon's phase.</returns>
    public static Sky Compute(double timeOfDay, double day, CelestialSettings? settings = null)
    {
        settings ??= new CelestialSettings();
        double latitude = settings.LatitudeDegrees * DegreesToRadians;
        double hour = (timeOfDay - NoonFraction) * Tau;
        double phase = Wrap(settings.MoonPhaseAtStart + (day / settings.MoonCycleDays));
        double moonHour = hour - (phase * Tau);
        double inclination = settings.MoonInclinationDegrees * DegreesToRadians * Math.Sin(Tau * day / settings.MoonInclinationPeriodDays);
        return new Sky(Direction(hour, 0, latitude), Direction(moonHour, inclination, latitude), phase, hour);
    }

    /// <summary>The sine of the elevation: 1 straight overhead, 0 on the horizon, negative below it.</summary>
    /// <param name="direction">A unit direction.</param>
    /// <returns>The height of the direction above the horizon plane.</returns>
    public static double SineElevation(Vector3 direction) => direction.Y;

    /// <summary>How much of the moon's face the sun lights, seen from the viewer.</summary>
    /// <param name="phase">The moon phase from <see cref="Compute"/>.</param>
    /// <returns>0 at new moon, 1 at full moon.</returns>
    public static double Illumination(double phase) => 0.5 * (1 - Math.Cos(Tau * phase));

    /// <summary>How bright the day is, smoothly: 0 at night, 1 in full daylight, rising through dawn and dusk.</summary>
    /// <param name="sineSunElevation">The sine of the sun's elevation.</param>
    /// <returns>A value from 0 to 1.</returns>
    public static double Daylight(double sineSunElevation) => SmoothStep(-0.12, 0.25, sineSunElevation);

    /// <summary>Hermite smooth step.</summary>
    /// <param name="edge0">Value below which the result is 0.</param>
    /// <param name="edge1">Value above which the result is 1.</param>
    /// <param name="x">The value.</param>
    /// <returns>A smooth value from 0 to 1.</returns>
    public static double SmoothStep(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - (2 * t));
    }

    // Standard horizontal coordinates from hour angle, declination and latitude, converted to the game's axes (east, up, south).
    private static Vector3 Direction(double hourAngle, double declination, double latitude)
    {
        double east = -Math.Cos(declination) * Math.Sin(hourAngle);
        double north = (Math.Sin(declination) * Math.Cos(latitude)) - (Math.Cos(declination) * Math.Sin(latitude) * Math.Cos(hourAngle));
        double up = (Math.Sin(declination) * Math.Sin(latitude)) + (Math.Cos(declination) * Math.Cos(latitude) * Math.Cos(hourAngle));
        return Vector3.Normalize(new Vector3((float)east, (float)up, (float)-north));
    }

    private static double Wrap(double value) => value - Math.Floor(value);
}
