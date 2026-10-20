using Godot;
using Realm.Core.Random;
using Realm.Gen.Terrain;

namespace Realm.Game.Spike;

/// <summary>
/// Procedural terrain heights for the streaming spike: the same fractal noise the generators use, evaluated per
/// region, so adjacent regions join seamlessly and the spike measures streaming rather than content.
/// </summary>
/// <remarks>Thread-safe: the noise is immutable, so regions can be generated on several threads at once.</remarks>
public sealed class SpikeTerrainSource
{
    private const double BaseHeightMeters = 100.0;
    private const double AmplitudeMeters = 140.0;
    private const double WavelengthMeters = 1800.0;
    private const int Octaves = 6;
    private const double Lacunarity = 2.0;
    private const double Gain = 0.5;

    private readonly GradientNoise _noise;
    private readonly int _regionSize;
    private readonly float _spacing;

    /// <summary>Creates a source.</summary>
    /// <param name="seed">World seed.</param>
    /// <param name="regionSize">Vertices per region side.</param>
    /// <param name="spacing">Metres between vertices.</param>
    public SpikeTerrainSource(ulong seed, int regionSize, float spacing)
    {
        _noise = new GradientNoise(Rng.ForKey(seed, "spike", "noise"));
        _regionSize = regionSize;
        _spacing = spacing;
    }

    /// <summary>Generates the heights of one region.</summary>
    /// <param name="location">Region coordinate; region (0, 0) starts at the world origin and extends east and south.</param>
    /// <returns>Heights in metres, <c>regionSize x regionSize</c> values, row by row.</returns>
    public float[] Generate(Vector2I location)
    {
        var heights = new float[_regionSize * _regionSize];
        for (int iz = 0; iz < _regionSize; iz++)
        {
            double z = ((location.Y * _regionSize) + iz) * (double)_spacing;
            for (int ix = 0; ix < _regionSize; ix++)
            {
                double x = ((location.X * _regionSize) + ix) * (double)_spacing;
                double n = _noise.Fractal(x / WavelengthMeters, z / WavelengthMeters, Octaves, Lacunarity, Gain);
                heights[(iz * _regionSize) + ix] = (float)(BaseHeightMeters + (AmplitudeMeters * n));
            }
        }

        return heights;
    }
}
