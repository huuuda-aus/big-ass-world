using System;

namespace Realm.Gen.Terrain.Water;

/// <summary>How fast river water runs, from the fall of its surface and the size of the river.</summary>
/// <remarks>Determinism: pure. Thread-safety: pure.</remarks>
public static class RiverFlow
{
    /// <summary>Slowest flow speed, metres per second (a lazy lowland drift).</summary>
    public const double MinimumSpeed = 0.4;

    /// <summary>Fastest flow speed, metres per second (a mountain torrent).</summary>
    public const double MaximumSpeed = 7;

    private const double SpeedPerRootSlope = 8;
    private const double SizeGainPerLogFlow = 0.05;

    /// <summary>Flow speed of the water for a stretch of the given fall and size.</summary>
    /// <param name="slope">Fall of the surface, metres per metre; steeper means faster.</param>
    /// <param name="flowKm2">Catchment flow; bigger rivers run a little faster.</param>
    /// <returns>Metres per second, between <see cref="MinimumSpeed"/> and <see cref="MaximumSpeed"/>.</returns>
    public static double Speed(double slope, double flowKm2)
    {
        double fall = Math.Sqrt(Math.Max(0, slope));
        double size = 1 + (SizeGainPerLogFlow * Math.Log(1 + Math.Max(0, flowKm2)));
        return Math.Clamp((MinimumSpeed + (SpeedPerRootSlope * fall)) * size, MinimumSpeed, MaximumSpeed);
    }
}
