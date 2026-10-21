using System;
using System.IO;
using Godot;
using Realm.Gen.Bake;

namespace Realm.Game.World;

/// <summary>Reads baked terrain (a manifest and region tiles) from disk. Tile reads are safe on worker threads.</summary>
public sealed class BakedTerrainSource
{
    private readonly string _directory;

    private BakedTerrainSource(string directory, TerrainManifest manifest)
    {
        _directory = directory;
        Manifest = manifest;
    }

    /// <summary>The directory the terrain was read from (it also holds <c>settlements.json</c> when cities were baked).</summary>
    public string Directory => _directory;

    /// <summary>The terrain's manifest.</summary>
    public TerrainManifest Manifest { get; }

    /// <summary>Opens a baked terrain directory.</summary>
    /// <param name="directory">Directory holding <c>manifest.json</c> and the tiles.</param>
    /// <returns>The source.</returns>
    /// <exception cref="FileNotFoundException">There is no manifest; the message says how to create one.</exception>
    public static BakedTerrainSource Open(string directory)
    {
        string path = Path.Combine(directory, TerrainManifest.FileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"No baked terrain at '{directory}'. Create it with: dotnet run --project src/Realm.Cli -- bake-terrain --out {directory}");
        }

        return new BakedTerrainSource(directory, TerrainManifest.FromJson(File.ReadAllText(path)));
    }

    /// <summary>Reads the tile of a region.</summary>
    /// <param name="location">The region coordinate.</param>
    /// <returns>The region data.</returns>
    /// <exception cref="InvalidDataException">The file is damaged or does not match the manifest.</exception>
    public RegionData Load(Vector2I location)
    {
        string path = Path.Combine(_directory, RegionTile.FileName(location.X, location.Y));
        RegionTile tile = RegionTileFile.Read(path);
        if (tile.Size != Manifest.RegionSize || tile.RegionX != location.X || tile.RegionZ != location.Y)
        {
            throw new InvalidDataException($"Tile '{path}' does not match the manifest (size {tile.Size}, region ({tile.RegionX}, {tile.RegionZ})).");
        }

        return new RegionData(tile.Heights, tile.Colours);
    }
}
