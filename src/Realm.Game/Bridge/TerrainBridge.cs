using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Godot;
using Realm.Gen.Textures;

namespace Realm.Game.Bridge;

/// <summary>
/// The single place that talks to the Terrain3D GDExtension (CLAUDE.md section 5.2). Terrain3D registers its
/// classes at runtime, so C# reaches it through Godot's dynamic API (<c>Call</c>, <c>Get</c>, <c>Set</c>); every
/// such call lives here, behind typed methods with version and range checks. No other file calls Terrain3D.
/// </summary>
/// <remarks>
/// Targets Terrain3D 1.0.2 (see docs/reference/versions.md). Method and property names were taken from the
/// extension's own class listing (<c>ClassDB</c>) on 2026-10-20, not from memory. Main thread only.
/// </remarks>
public sealed class TerrainBridge
{
    /// <summary>The Terrain3D version this bridge was written against.</summary>
    public const string RequiredVersion = "1.0.2";

    /// <summary>Terrain3D keeps a fixed grid of this many region slots per axis (<c>Terrain3DData.REGION_MAP_SIZE</c>).</summary>
    public const int RegionSlotsPerAxis = 32;

    private const string TerrainClass = "Terrain3D";
    private const string RegionClass = "Terrain3DRegion";
    private const int AllMapTypes = 3; // Terrain3DRegion.TYPE_MAX
    private const int NoError = 0;
    private const int NoWorldBackground = 0; // Terrain3DMaterial.WorldBackground.NONE

    private readonly GodotObject _data;

    private TerrainBridge(Node3D terrain, GodotObject data, TerrainSettings settings)
    {
        Node = terrain;
        _data = data;
        Settings = settings;
        RegionExtentMeters = settings.RegionSize * settings.VertexSpacing;
    }

    /// <summary>The Terrain3D node; add it to the scene tree.</summary>
    public Node3D Node { get; }

    /// <summary>The settings this terrain was created with.</summary>
    public TerrainSettings Settings { get; }

    /// <summary>Width of one region in metres (<c>region size x vertex spacing</c>).</summary>
    public float RegionExtentMeters { get; }

    /// <summary>Lowest valid region coordinate on each axis.</summary>
    public static int MinRegionCoordinate => -RegionSlotsPerAxis / 2;

    /// <summary>Highest valid region coordinate on each axis.</summary>
    public static int MaxRegionCoordinate => (RegionSlotsPerAxis / 2) - 1;

    /// <summary>Creates a Terrain3D node.</summary>
    /// <param name="settings">Region size, spacing and collision.</param>
    /// <param name="camera">Camera that drives level of detail and collision.</param>
    /// <returns>The bridge.</returns>
    /// <exception cref="InvalidOperationException">Terrain3D is not loaded, or its version differs from <see cref="RequiredVersion"/>.</exception>
    public static TerrainBridge Create(TerrainSettings settings, Camera3D camera)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(camera);

        if (!ClassDB.ClassExists(TerrainClass))
        {
            throw new InvalidOperationException(
                "The Terrain3D extension is not loaded. Check that addons/terrain_3d exists and the project has been imported.");
        }

        Node3D terrain = ClassDB.Instantiate(TerrainClass).As<Node3D>();
        string version = terrain.Get("version").AsString();
        if (version != RequiredVersion)
        {
            terrain.Free();
            throw new InvalidOperationException(
                $"Terrain3D {version} is installed but Realm requires {RequiredVersion}. See docs/reference/versions.md before upgrading.");
        }

        terrain.Set("region_size", settings.RegionSize);
        terrain.Set("vertex_spacing", settings.VertexSpacing);
        terrain.Set("collision_shape_size", settings.CollisionShapeSize);
        terrain.Set("collision_radius", settings.CollisionRadius);
        terrain.Set("collision_mode", (int)settings.Collision);
        terrain.Call("set_camera", camera);

        // Draw nothing beyond the last region: the default background extends the world edge as a stretched curtain.
        terrain.Call("get_material").AsGodotObject().Call("set_world_background", NoWorldBackground);

        GodotObject data = terrain.Call("get_data").AsGodotObject();
        return new TerrainBridge(terrain, data, settings);
    }

    /// <summary>Finds the region a world position falls in.</summary>
    /// <param name="world">World position in metres.</param>
    /// <returns>The region coordinate.</returns>
    public Vector2I RegionAt(Vector3 world) =>
        new((int)Math.Floor(world.X / RegionExtentMeters), (int)Math.Floor(world.Z / RegionExtentMeters));

    /// <summary>Checks whether a region coordinate fits in Terrain3D's fixed region grid.</summary>
    /// <param name="location">The region coordinate.</param>
    /// <returns><c>true</c> when it can hold a region.</returns>
    public static bool IsValidRegion(Vector2I location) =>
        location.X >= MinRegionCoordinate && location.X <= MaxRegionCoordinate
        && location.Y >= MinRegionCoordinate && location.Y <= MaxRegionCoordinate;

    /// <summary>Total main-thread milliseconds spent building images and region objects in <see cref="PrepareRegion"/> (profiling).</summary>
    public double TotalBuildMs { get; private set; }

    /// <summary>Total main-thread milliseconds spent inside Terrain3D's <c>add_region</c> call (profiling).</summary>
    public double TotalAddCallMs { get; private set; }

    /// <summary>Total main-thread milliseconds spent inside Terrain3D's <c>remove_regionl</c> call (profiling).</summary>
    public double TotalRemoveCallMs { get; private set; }

    /// <summary>Number of regions Terrain3D currently holds.</summary>
    public int RegionCount => _data.Call("get_region_count").AsInt32();

    /// <summary>Checks whether a region exists.</summary>
    /// <param name="location">The region coordinate.</param>
    /// <returns><c>true</c> when it is loaded.</returns>
    public bool HasRegion(Vector2I location) => _data.Call("has_region", location).AsBool();

    /// <summary>
    /// Builds the Terrain3D region object for a height array without adding it. Intended for worker threads: it only
    /// creates new, unshared Godot objects. Pass the result to <see cref="AddPreparedRegion"/> on the main thread.
    /// </summary>
    /// <param name="location">The region coordinate; must satisfy <see cref="IsValidRegion"/>.</param>
    /// <param name="heights">Heights in metres, row by row, exactly <c>RegionSize x RegionSize</c> values.</param>
    /// <param name="colours">Optional colour per vertex, packed 0xAABBGGRR (the RGBA8 byte order), same layout as the heights.</param>
    /// <returns>The prepared region.</returns>
    /// <exception cref="ArgumentException">The location or array size is wrong.</exception>
    public PreparedRegion PrepareRegion(Vector2I location, ReadOnlySpan<float> heights, ReadOnlySpan<uint> colours = default)
    {
        if (!IsValidRegion(location))
        {
            throw new ArgumentException($"Region {location} is outside the {RegionSlotsPerAxis} x {RegionSlotsPerAxis} region grid.", nameof(location));
        }

        int size = Settings.RegionSize;
        if (heights.Length != size * size)
        {
            throw new ArgumentException($"Expected {size * size} heights but got {heights.Length}.", nameof(heights));
        }

        var build = Stopwatch.StartNew();
        var bytes = new byte[heights.Length * sizeof(float)];
        MemoryMarshal.AsBytes(heights).CopyTo(bytes);
        Image image = Image.CreateFromData(size, size, false, Image.Format.Rf, bytes);

        GodotObject region = ClassDB.Instantiate(RegionClass).AsGodotObject();
        region.Call("set_region_size", size);
        region.Call("set_vertex_spacing", Settings.VertexSpacing);
        region.Call("set_location", location);
        region.Call("set_height_map", image);
        if (!colours.IsEmpty)
        {
            if (colours.Length != size * size)
            {
                throw new ArgumentException($"Expected {size * size} colours but got {colours.Length}.", nameof(colours));
            }

            var colourBytes = new byte[colours.Length * sizeof(uint)];
            MemoryMarshal.AsBytes(colours).CopyTo(colourBytes);
            region.Call("set_color_map", Image.CreateFromData(size, size, false, Image.Format.Rgba8, colourBytes));
        }

        region.Call("sanitize_maps"); // fills any control and colour maps still missing
        build.Stop();
        return new PreparedRegion(location, region, build.Elapsed.TotalMilliseconds);
    }

    /// <summary>Adds a prepared region to the terrain. Main thread only.</summary>
    /// <param name="prepared">A region from <see cref="PrepareRegion"/>.</param>
    /// <param name="update">Whether Terrain3D rebuilds its shared textures now; false lets you batch with <see cref="UpdateAll"/>.</param>
    /// <exception cref="InvalidOperationException">Terrain3D rejected the region.</exception>
    public void AddPreparedRegion(PreparedRegion prepared, bool update)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        TotalBuildMs += prepared.BuildMs;

        var call = Stopwatch.StartNew();
        long error = _data.Call("add_region", prepared.Region, update).AsInt64();
        call.Stop();
        TotalAddCallMs += call.Elapsed.TotalMilliseconds;
        if (error != NoError)
        {
            throw new InvalidOperationException($"Terrain3D could not add region {prepared.Location} (error {error}).");
        }
    }

    /// <summary>Builds and adds a region in one step on the calling thread. Main thread only.</summary>
    /// <param name="location">The region coordinate.</param>
    /// <param name="heights">Heights in metres, <c>RegionSize x RegionSize</c> values.</param>
    /// <param name="update">Whether Terrain3D rebuilds its shared textures now.</param>
    public void AddRegion(Vector2I location, ReadOnlySpan<float> heights, bool update) =>
        AddPreparedRegion(PrepareRegion(location, heights), update);

    /// <summary>Builds and adds a region with a colour map in one step on the calling thread. Main thread only.</summary>
    /// <param name="location">The region coordinate.</param>
    /// <param name="heights">Heights in metres, <c>RegionSize x RegionSize</c> values.</param>
    /// <param name="colours">Colour per vertex packed 0xAABBGGRR.</param>
    /// <param name="update">Whether Terrain3D rebuilds its shared textures now.</param>
    public void AddRegion(Vector2I location, ReadOnlySpan<float> heights, ReadOnlySpan<uint> colours, bool update) =>
        AddPreparedRegion(PrepareRegion(location, heights, colours), update);

    /// <summary>Removes a region.</summary>
    /// <param name="location">The region coordinate.</param>
    /// <param name="update">Whether Terrain3D rebuilds its shared textures now.</param>
    public void RemoveRegion(Vector2I location, bool update)
    {
        var call = Stopwatch.StartNew();
        _data.Call("remove_regionl", location, update);
        call.Stop();
        TotalRemoveCallMs += call.Elapsed.TotalMilliseconds;
    }

    /// <summary>Rebuilds Terrain3D's shared textures for all regions; use after batching adds or removes with update false.</summary>
    public void UpdateAll() => _data.Call("update_maps", AllMapTypes, true, false);

    /// <summary>Shows the baked colour map directly instead of texture-based shading (used until real terrain textures exist).</summary>
    /// <param name="enabled">Whether to show the colour map as the surface.</param>
    public void ShowColourMapOnly(bool enabled) => Node.Set("show_colormap", enabled);

    /// <summary>
    /// Installs the ground textures and switches on Terrain3D's auto-shader, which paints the first texture on flat
    /// ground and the second on slopes. Pixel filtering is nearest-neighbour (retro look, CLAUDE.md section 6.7).
    /// </summary>
    /// <param name="textures">At least two textures: the flat-ground texture first, the slope texture second.</param>
    /// <param name="uvScale">Texture repeat; larger tiles more finely (Terrain3D's <c>uv_scale</c>).</param>
    /// <param name="slopeBias">Auto-shader slope sensitivity (Terrain3D <c>auto_slope</c>, 0 to 10); higher shows rock sooner.</param>
    /// <exception cref="ArgumentException">Fewer than two textures.</exception>
    public void SetSurfaceTextures(IReadOnlyList<SurfaceTexture> textures, float uvScale, float slopeBias)
    {
        ArgumentNullException.ThrowIfNull(textures);
        if (textures.Count < 2)
        {
            throw new ArgumentException("The auto-shader needs a flat-ground texture and a slope texture.", nameof(textures));
        }

        GodotObject assets = ClassDB.Instantiate("Terrain3DAssets").AsGodotObject();
        for (int id = 0; id < textures.Count; id++)
        {
            SurfaceTexture texture = textures[id];
            GodotObject asset = ClassDB.Instantiate("Terrain3DTextureAsset").AsGodotObject();
            asset.Call("set_name", texture.Name);
            asset.Call("set_id", id);
            asset.Call("set_albedo_texture", ToTexture(texture.Size, texture.AlbedoHeight));
            asset.Call("set_normal_texture", ToTexture(texture.Size, texture.NormalRoughness));
            asset.Call("set_uv_scale", uvScale);
            assets.Call("set_texture", id, asset);
        }

        Node.Set("assets", assets);
        assets.Call("update_texture_list");

        GodotObject material = Node.Call("get_material").AsGodotObject();
        material.Call("set_auto_shader", true);
        material.Call("set_texture_filtering", NearestFiltering);
        material.Call("set_shader_param", "auto_base_texture", 0);
        material.Call("set_shader_param", "auto_overlay_texture", 1);
        material.Call("set_shader_param", "auto_slope", slopeBias);
        material.Call("set_show_checkered", false);
        Node.Set("show_colormap", false);
    }

    private const int NearestFiltering = 1; // Terrain3DMaterial.TextureFiltering.NEAREST

    private static ImageTexture ToTexture(int size, byte[] rgba)
    {
        Image image = Image.CreateFromData(size, size, false, Image.Format.Rgba8, rgba);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Reads the terrain height at a world position.</summary>
    /// <param name="world">World position in metres.</param>
    /// <returns>The height, or NaN when no region covers the position.</returns>
    public float HeightAt(Vector3 world) => _data.Call("get_height", world).AsSingle();
}
