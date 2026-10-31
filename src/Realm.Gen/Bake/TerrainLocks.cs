using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Realm.Gen.Bake;

/// <summary>
/// Which baked terrain regions a hand edit has locked, so a later <see cref="TerrainBaker.Bake"/> keeps their tile
/// instead of overwriting it (CLAUDE.md section 4.4, generate-bake-edit). Stored as <c>locks.json</c> next to
/// <c>manifest.json</c> and the region tiles, so it travels with the baked terrain it protects.
/// </summary>
/// <remarks>Determinism: locking has no effect on what a region would generate to, only on whether that result is used. Thread-safety: immutable.</remarks>
public sealed record TerrainLocks
{
    /// <summary>The lock file name.</summary>
    public const string FileName = "locks.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The locked regions.</summary>
    public IReadOnlyList<RegionLock> Regions { get; init; } = Array.Empty<RegionLock>();

    /// <summary>No regions locked.</summary>
    public static TerrainLocks Empty { get; } = new();

    /// <summary>Whether a region is locked.</summary>
    /// <param name="regionX">Region coordinate, east.</param>
    /// <param name="regionZ">Region coordinate, south.</param>
    /// <returns><c>true</c> when locked.</returns>
    public bool Contains(int regionX, int regionZ) => Regions.Any(r => r.RegionX == regionX && r.RegionZ == regionZ);

    /// <summary>Locks a region, replacing any existing lock on it (so re-locking updates the note).</summary>
    /// <param name="regionX">Region coordinate, east.</param>
    /// <param name="regionZ">Region coordinate, south.</param>
    /// <param name="note">Free-text note.</param>
    /// <returns>The updated locks.</returns>
    public TerrainLocks WithLock(int regionX, int regionZ, string note = "") =>
        this with
        {
            Regions = Regions.Where(r => r.RegionX != regionX || r.RegionZ != regionZ)
                .Append(new RegionLock { RegionX = regionX, RegionZ = regionZ, Note = note })
                .ToArray(),
        };

    /// <summary>Unlocks a region; a no-op if it was not locked.</summary>
    /// <param name="regionX">Region coordinate, east.</param>
    /// <param name="regionZ">Region coordinate, south.</param>
    /// <returns>The updated locks.</returns>
    public TerrainLocks WithoutLock(int regionX, int regionZ) =>
        this with { Regions = Regions.Where(r => r.RegionX != regionX || r.RegionZ != regionZ).ToArray() };

    /// <summary>Serialises to JSON.</summary>
    /// <returns>The JSON text.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Parses locks.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The locks.</returns>
    /// <exception cref="JsonException">The text is not valid locks.</exception>
    public static TerrainLocks FromJson(string json) =>
        JsonSerializer.Deserialize<TerrainLocks>(json, Json) ?? throw new JsonException("The locks file is empty.");

    /// <summary>Reads <c>locks.json</c> from a baked terrain directory, or <see cref="Empty"/> when there is none.</summary>
    /// <param name="directory">The baked terrain directory.</param>
    /// <returns>The locks.</returns>
    /// <exception cref="JsonException">The file exists but is not valid locks.</exception>
    public static TerrainLocks Load(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        string path = Path.Combine(directory, FileName);
        return File.Exists(path) ? FromJson(File.ReadAllText(path)) : Empty;
    }

    /// <summary>Writes <c>locks.json</c> to a directory atomically (temp file, then move; CLAUDE.md section 8.3).</summary>
    /// <param name="directory">The baked terrain directory; created when missing.</param>
    public void Save(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, FileName);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, ToJson());
        File.Move(temporary, path, overwrite: true);
    }
}
