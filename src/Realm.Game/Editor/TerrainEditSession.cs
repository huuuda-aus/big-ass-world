using System;
using System.IO;
using Godot;
using Realm.Game.World;
using Realm.Gen.Bake;

namespace Realm.Game.Editor;

/// <summary>
/// Holds one baked region tile open for hand-editing (CLAUDE.md section 4.4) and writes it back atomically. Brush
/// math lives in the Godot-free <see cref="TerrainTileEditor"/>; this class only does file I/O and tracks which
/// region and manifest are open, so it has no logic worth unit-testing on its own.
/// </summary>
/// <remarks>
/// Known limitation: does not update the manifest's <see cref="TerrainManifest.MinHeight"/> and
/// <see cref="TerrainManifest.MaxHeight"/> after an edit that exceeds them (see known-issues.md).
/// </remarks>
public sealed class TerrainEditSession
{
    private readonly string _directory;

    private TerrainEditSession(string directory, TerrainManifest manifest, TerrainLocks locks, Vector2I location, RegionTile tile)
    {
        _directory = directory;
        Manifest = manifest;
        Locks = locks;
        Location = location;
        Tile = tile;
    }

    /// <summary>The terrain's manifest, as it was when the region was opened or last saved.</summary>
    public TerrainManifest Manifest { get; private set; }

    /// <summary>Which regions of this terrain are locked against a later bake overwriting them.</summary>
    public TerrainLocks Locks { get; private set; }

    /// <summary>Whether <see cref="Location"/> is currently locked.</summary>
    public bool IsLocked => Locks.Contains(Location.X, Location.Y);

    /// <summary>The region coordinate being edited.</summary>
    public Vector2I Location { get; }

    /// <summary>The region's current heights and colours, including any unsaved brush strokes.</summary>
    public RegionTile Tile { get; private set; }

    /// <summary>Whether <see cref="Tile"/> has changes not yet written to disk.</summary>
    public bool Dirty { get; private set; }

    /// <summary>Opens one region of a baked terrain directory for editing.</summary>
    /// <param name="directory">Directory holding <c>manifest.json</c> and the region tiles.</param>
    /// <param name="location">The region coordinate; must be within the manifest's range.</param>
    /// <returns>The session.</returns>
    /// <exception cref="FileNotFoundException">There is no baked terrain at <paramref name="directory"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="location"/> is outside the manifest's region range.</exception>
    public static TerrainEditSession Open(string directory, Vector2I location)
    {
        BakedTerrainSource source = BakedTerrainSource.Open(directory);
        TerrainManifest manifest = source.Manifest;
        if (location.X < manifest.MinRegion || location.X > manifest.MaxRegion
            || location.Y < manifest.MinRegion || location.Y > manifest.MaxRegion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(location), location, $"Regions run from {manifest.MinRegion} to {manifest.MaxRegion} on each axis.");
        }

        string path = Path.Combine(directory, RegionTile.FileName(location.X, location.Y));
        return new TerrainEditSession(directory, manifest, TerrainLocks.Load(directory), location, RegionTileFile.Read(path));
    }

    /// <summary>Applies one brush dab, using the manifest's height range for the colour palette.</summary>
    /// <param name="stroke">Where and how the brush pushes, in this region's own vertex grid.</param>
    public void ApplyStroke(BrushStroke stroke)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        Tile = TerrainTileEditor.ApplyBrush(Tile, stroke, Manifest.MaxHeight, -Manifest.MinHeight);
        Dirty = true;
    }

    /// <summary>Discards unsaved changes and reloads the region from disk.</summary>
    public void Revert()
    {
        string path = Path.Combine(_directory, RegionTile.FileName(Location.X, Location.Y));
        Tile = RegionTileFile.Read(path);
        Dirty = false;
    }

    /// <summary>
    /// Writes the region tile (temp file, then move; CLAUDE.md section 8.3), folds its new checksum into
    /// <c>manifest.json</c> without re-reading every other tile in full (<see cref="RegionTileFile.ReadChecksum"/>),
    /// and locks the region (CLAUDE.md section 4.4) so a later <c>bake-terrain</c> keeps this edit instead of
    /// silently overwriting it. Call <see cref="Unlock"/> afterwards if that protection is not wanted.
    /// </summary>
    public void Save()
    {
        RegionTileFile.Write(_directory, Tile);
        SetLocked(true, $"terrain editor, {DateTimeOffset.UtcNow:yyyy-MM-dd}");

        int perAxis = Manifest.RegionsPerAxis;
        var checksums = new ulong[perAxis * perAxis];
        int index = 0;
        for (int z = Manifest.MinRegion; z <= Manifest.MaxRegion; z++)
        {
            for (int x = Manifest.MinRegion; x <= Manifest.MaxRegion; x++)
            {
                string path = Path.Combine(_directory, RegionTile.FileName(x, z));
                checksums[index++] = RegionTileFile.ReadChecksum(path);
            }
        }

        Manifest = Manifest with { Checksum = TerrainManifestChecksum.Combine(checksums) };
        string manifestPath = Path.Combine(_directory, TerrainManifest.FileName);
        string temporary = manifestPath + ".tmp";
        File.WriteAllText(temporary, Manifest.ToJson());
        File.Move(temporary, manifestPath, overwrite: true);
        Dirty = false;
    }

    /// <summary>Removes the lock on <see cref="Location"/>, so a later bake is free to overwrite it again.</summary>
    public void Unlock() => SetLocked(false);

    private void SetLocked(bool locked, string note = "")
    {
        Locks = locked ? Locks.WithLock(Location.X, Location.Y, note) : Locks.WithoutLock(Location.X, Location.Y);
        Locks.Save(_directory);
    }
}
