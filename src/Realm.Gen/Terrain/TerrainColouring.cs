using System;

namespace Realm.Gen.Terrain;

/// <summary>The palette that colours terrain by height, shared by the preview image and the baked colour maps.</summary>
/// <remarks>Determinism: pure functions. Thread-safety: stateless. The colours are placeholders, not final art.</remarks>
public static class TerrainColouring
{
    /// <summary>Red component of the colour used for rivers and lakes.</summary>
    public const byte WaterRed = 40;

    /// <summary>Green component of the colour used for rivers and lakes.</summary>
    public const byte WaterGreen = 120;

    /// <summary>Blue component of the colour used for rivers and lakes.</summary>
    public const byte WaterBlue = 255;

    /// <summary>Slope (rise over run) where bare rock starts to show through the ground colour (about 27 degrees).</summary>
    public const double RockSlopeStart = 0.5;

    /// <summary>Slope (rise over run) from which the ground is fully rock coloured (about 45 degrees).</summary>
    public const double RockSlopeFull = 1.0;

    private const double ShallowWaterDepthFraction = 1.0;
    private const double LowlandUpTo = 0.25;
    private const double HillsUpTo = 0.6;

    // Colour stops: below sea level (deep, shallow), then lowland, hills, rock, snow.
    private static readonly (double R, double G, double B) Deep = (10, 30, 90);
    private static readonly (double R, double G, double B) Shallow = (60, 130, 190);
    private static readonly (double R, double G, double B) Lowland = (70, 130, 60);
    private static readonly (double R, double G, double B) Hills = (140, 120, 80);
    private static readonly (double R, double G, double B) Rock = (120, 115, 110);
    private static readonly (double R, double G, double B) SlopeRock = (110, 105, 100);
    private static readonly (double R, double G, double B) Snow = (245, 245, 250);

    /// <summary>Colour of the ground at a height.</summary>
    /// <param name="height">Height in metres; negative is below sea level.</param>
    /// <param name="top">Height that maps to the top of the land palette (usually the world's highest point).</param>
    /// <param name="depth">Depth that maps to the deepest water colour (usually minus the world's lowest point).</param>
    /// <param name="slope">Ground slope as rise over run; steeper than <see cref="RockSlopeStart"/> blends toward bare rock. Ignored under water.</param>
    /// <returns>Red, green and blue in 0 to 255.</returns>
    public static (double R, double G, double B) Albedo(double height, double top, double depth, double slope = 0)
    {
        top = Math.Max(1.0, top);
        depth = Math.Max(1.0, depth);
        if (height < 0)
        {
            return Lerp(Shallow, Deep, Math.Clamp(-height / (depth * ShallowWaterDepthFraction), 0, 1));
        }

        // Steep ground is bare rock whatever its height.
        double rock = Math.Clamp((slope - RockSlopeStart) / (RockSlopeFull - RockSlopeStart), 0, 1);
        if (rock > 0)
        {
            return Lerp(HeightAlbedo(height, top), SlopeRock, rock * rock * (3 - (2 * rock)));
        }

        return HeightAlbedo(height, top);
    }

    private static (double R, double G, double B) HeightAlbedo(double height, double top)
    {
        double t = Math.Clamp(height / top, 0, 1);
        if (t < LowlandUpTo)
        {
            return Lerp(Lowland, Hills, t / LowlandUpTo);
        }

        if (t < HillsUpTo)
        {
            return Lerp(Hills, Rock, (t - LowlandUpTo) / (HillsUpTo - LowlandUpTo));
        }

        return Lerp(Rock, Snow, (t - HillsUpTo) / (1.0 - HillsUpTo));
    }

    /// <summary>Packs a colour as 0xAABBGGRR (opaque; red in the lowest byte), the layout of an RGBA8 image.</summary>
    /// <param name="red">Red, 0 to 255 (clamped and rounded).</param>
    /// <param name="green">Green, 0 to 255.</param>
    /// <param name="blue">Blue, 0 to 255.</param>
    /// <returns>The packed colour.</returns>
    public static uint Pack(double red, double green, double blue) =>
        0xFF000000u | ((uint)ToByte(blue) << 16) | ((uint)ToByte(green) << 8) | ToByte(red);

    /// <summary>The packed colour of rivers and lakes.</summary>
    public static uint WaterPacked => Pack(WaterRed, WaterGreen, WaterBlue);

    private static (double R, double G, double B) Lerp((double R, double G, double B) a, (double R, double G, double B) b, double t) =>
        (a.R + ((b.R - a.R) * t), a.G + ((b.G - a.G) * t), a.B + ((b.B - a.B) * t));

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
