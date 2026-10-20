using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Realm.Gen.Bake;

/// <summary>
/// Describes a baked terrain: the world and region geometry, which generators and seed produced it, and where to
/// start. Stored as <c>manifest.json</c> next to the region tiles; the game reads it before streaming.
/// </summary>
/// <remarks>Thread-safety: immutable.</remarks>
public sealed record TerrainManifest
{
    /// <summary>The manifest file name.</summary>
    public const string FileName = "manifest.json";

    /// <summary>Current manifest format version.</summary>
    public const int CurrentFormat = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Manifest format version.</summary>
    public int Format { get; init; } = CurrentFormat;

    /// <summary>Content ID of the world map the terrain was baked from.</summary>
    public string World { get; init; } = string.Empty;

    /// <summary>World side length in metres.</summary>
    public int WorldSizeMeters { get; init; }

    /// <summary>Metres between vertices.</summary>
    public int SpacingMeters { get; init; }

    /// <summary>Vertices per region side.</summary>
    public int RegionSize { get; init; }

    /// <summary>Width of a region in metres.</summary>
    public int RegionExtentMeters { get; init; }

    /// <summary>Regions along each side of the world.</summary>
    public int RegionsPerAxis { get; init; }

    /// <summary>Lowest region coordinate on each axis (region (0, 0) starts at the world origin).</summary>
    public int MinRegion { get; init; }

    /// <summary>Highest region coordinate on each axis.</summary>
    public int MaxRegion { get; init; }

    /// <summary>World seed.</summary>
    public ulong Seed { get; init; }

    /// <summary>Generators that produced the terrain, as <c>id vN</c>.</summary>
    public IReadOnlyList<string> Generators { get; init; } = Array.Empty<string>();

    /// <summary>Lowest terrain height in metres.</summary>
    public double MinHeight { get; init; }

    /// <summary>Highest terrain height in metres.</summary>
    public double MaxHeight { get; init; }

    /// <summary>Number of rivers baked into the colour maps.</summary>
    public int RiverCount { get; init; }

    /// <summary>Number of lakes baked into the colour maps.</summary>
    public int LakeCount { get; init; }

    /// <summary>Where the player starts: east coordinate in metres from the world centre.</summary>
    public double SpawnX { get; init; }

    /// <summary>Where the player starts: south coordinate in metres from the world centre.</summary>
    public double SpawnZ { get; init; }

    /// <summary>Fingerprint of all tile checksums in region order, as 16 hexadecimal digits.</summary>
    public string Checksum { get; init; } = string.Empty;

    /// <summary>Value equality: two manifests are equal when every field is, comparing <see cref="Generators"/> element by element.</summary>
    /// <param name="other">The manifest to compare with.</param>
    /// <returns><c>true</c> when all fields are equal.</returns>
    public bool Equals(TerrainManifest? other) =>
        other is not null
        && Format == other.Format && World == other.World && WorldSizeMeters == other.WorldSizeMeters
        && SpacingMeters == other.SpacingMeters && RegionSize == other.RegionSize
        && RegionExtentMeters == other.RegionExtentMeters && RegionsPerAxis == other.RegionsPerAxis
        && MinRegion == other.MinRegion && MaxRegion == other.MaxRegion && Seed == other.Seed
        && Generators.SequenceEqual(other.Generators)
        && MinHeight == other.MinHeight && MaxHeight == other.MaxHeight
        && RiverCount == other.RiverCount && LakeCount == other.LakeCount
        && SpawnX == other.SpawnX && SpawnZ == other.SpawnZ && Checksum == other.Checksum;

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(World);
        hash.Add(WorldSizeMeters);
        hash.Add(SpacingMeters);
        hash.Add(RegionSize);
        hash.Add(Seed);
        hash.Add(Checksum);
        foreach (string generator in Generators)
        {
            hash.Add(generator);
        }

        return hash.ToHashCode();
    }

    /// <summary>Serialises to JSON.</summary>
    /// <returns>The JSON text.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Parses a manifest.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="JsonException">The text is not a valid manifest.</exception>
    public static TerrainManifest FromJson(string json) =>
        JsonSerializer.Deserialize<TerrainManifest>(json, Json) ?? throw new JsonException("The manifest is empty.");
}
