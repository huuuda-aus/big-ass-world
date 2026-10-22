using System;

namespace Realm.Gen.Weather;

/// <summary>
/// How cloudy each part of the world is: two coarse maps, <em>cover</em> (how much of the sky is cloud) and <em>thickness</em> (how deep
/// and dark the cloud is, from thin fair-weather puffs to storm grey). The sky shader draws the clouds from them, so the weather differs
/// from place to place.
/// </summary>
/// <remarks>Determinism: pure data. Thread-safety: immutable after construction.</remarks>
public sealed class CloudClimate
{
    /// <summary>Creates a climate.</summary>
    /// <param name="worldSizeMeters">World side length in metres.</param>
    /// <param name="side">Cells per side.</param>
    /// <param name="cover">Cover per cell, 0 to 255, row by row from the north-west.</param>
    /// <param name="thickness">Thickness per cell, 0 to 255.</param>
    /// <exception cref="ArgumentException">An array has the wrong length.</exception>
    public CloudClimate(int worldSizeMeters, int side, byte[] cover, byte[] thickness)
    {
        ArgumentNullException.ThrowIfNull(cover);
        ArgumentNullException.ThrowIfNull(thickness);
        if (side < 2 || cover.Length != side * side || thickness.Length != side * side)
        {
            throw new ArgumentException($"Both maps must be {side} x {side} cells.");
        }

        WorldSizeMeters = worldSizeMeters;
        Side = side;
        Cover = cover;
        Thickness = thickness;
    }

    /// <summary>A uniform climate, for worlds without a baked one.</summary>
    /// <param name="worldSizeMeters">World side length in metres.</param>
    /// <param name="cover">Cover, 0 to 1.</param>
    /// <param name="thickness">Thickness, 0 to 1.</param>
    /// <returns>A small climate with the same values everywhere.</returns>
    public static CloudClimate Uniform(int worldSizeMeters, double cover, double thickness)
    {
        const int Cells = 4;
        var c = new byte[Cells * Cells];
        var t = new byte[Cells * Cells];
        Array.Fill(c, (byte)Math.Round(Math.Clamp(cover, 0, 1) * 255));
        Array.Fill(t, (byte)Math.Round(Math.Clamp(thickness, 0, 1) * 255));
        return new CloudClimate(worldSizeMeters, Cells, c, t);
    }

    /// <summary>World side length in metres.</summary>
    public int WorldSizeMeters { get; }

    /// <summary>Cells per side.</summary>
    public int Side { get; }

    /// <summary>Cover per cell, 0 to 255, row by row from the north-west.</summary>
    public byte[] Cover { get; }

    /// <summary>Thickness per cell, 0 to 255.</summary>
    public byte[] Thickness { get; }

    /// <summary>Cover at a world position, interpolated.</summary>
    /// <param name="x">World east in metres.</param>
    /// <param name="z">World south in metres.</param>
    /// <returns>0 clear to 1 overcast.</returns>
    public double CoverAt(double x, double z) => Sample(Cover, x, z);

    /// <summary>Thickness at a world position, interpolated.</summary>
    /// <param name="x">World east in metres.</param>
    /// <param name="z">World south in metres.</param>
    /// <returns>0 thin to 1 storm grey.</returns>
    public double ThicknessAt(double x, double z) => Sample(Thickness, x, z);

    private double Sample(byte[] map, double x, double z)
    {
        double u = Math.Clamp(((x / WorldSizeMeters) + 0.5) * Side - 0.5, 0, Side - 1);
        double v = Math.Clamp(((z / WorldSizeMeters) + 0.5) * Side - 0.5, 0, Side - 1);
        int u0 = Math.Min((int)Math.Floor(u), Side - 2);
        int v0 = Math.Min((int)Math.Floor(v), Side - 2);
        double tu = u - u0;
        double tv = v - v0;
        double a = map[(v0 * Side) + u0];
        double b = map[(v0 * Side) + u0 + 1];
        double c = map[((v0 + 1) * Side) + u0];
        double d = map[((v0 + 1) * Side) + u0 + 1];
        double top = a + (tu * (b - a));
        double bottom = c + (tu * (d - c));
        return (top + (tv * (bottom - top))) / 255.0;
    }
}
