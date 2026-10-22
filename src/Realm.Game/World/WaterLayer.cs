using System.IO;
using Godot;
using Realm.Gen.Terrain.Water;
using Realm.Gen.Textures;

namespace Realm.Game.World;

/// <summary>
/// The water surfaces of the world: river ribbons that fall from the mountains to the sea and flat lake sheets, baked into
/// <c>water.bin</c> as square tiles. A shader scrolls a ripple texture along each river at a speed that comes from its slope, and
/// adds foam where the water is steep or near the banks.
/// </summary>
/// <remarks>Main thread only. All tiles are built at start; each is its own mesh so the engine can cull them.</remarks>
public sealed partial class WaterLayer : Node3D
{
    private const string ShaderCode = @"
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, specular_schlick_ggx;

uniform sampler2D ripple_tex : source_color, filter_nearest, repeat_enable;
uniform float tile_meters = 6.0;
uniform float min_speed = 0.4;
uniform float max_speed = 7.0;
uniform vec3 lowland_colour : source_color = vec3(0.10, 0.26, 0.28);
uniform vec3 highland_colour : source_color = vec3(0.16, 0.38, 0.52);
uniform vec3 foam_colour : source_color = vec3(0.92, 0.95, 0.97);

varying vec4 info;

void vertex() {
    info = COLOR;
}

void fragment() {
    float speed = mix(min_speed, max_speed, info.r);
    float turbulence = info.b;
    // UV.y runs along the river (metres from the source), so moving the lookup backwards makes the pattern travel downstream.
    vec2 slide = vec2(0.0, -TIME * speed);
    vec4 a = texture(ripple_tex, (UV + slide) / tile_meters);
    vec4 b = texture(ripple_tex, (UV * 1.7 + slide * 1.35 + vec2(3.1, 1.7)) / tile_meters);
    vec3 base = mix(lowland_colour, highland_colour, info.g);
    vec3 colour = base * (0.55 + (a.rgb + b.rgb));
    float height = 0.5 * (a.a + b.a);
    float foam = smoothstep(0.72 - 0.35 * turbulence, 1.0, height) * (1.3 * turbulence + 0.35 * (1.0 - info.a));
    ALBEDO = mix(colour, foam_colour, clamp(foam, 0.0, 1.0));
    vec2 tilt = (a.xy - b.xy) * (0.5 + turbulence);
    NORMAL = normalize((VIEW_MATRIX * vec4(normalize(vec3(tilt.x, 1.0, tilt.y)), 0.0)).xyz);
    ROUGHNESS = mix(0.4, 0.7, turbulence);
    SPECULAR = 0.25;
    ALPHA = mix(0.55, 0.94, smoothstep(0.0, 0.3, info.a));
}
";

    private WaterLayer()
    {
        Name = "Water";
    }

    /// <summary>Number of water tiles built.</summary>
    public int TileCount { get; private set; }

    /// <summary>Reads <c>water.bin</c> next to the terrain and builds the meshes.</summary>
    /// <param name="directory">The baked terrain directory.</param>
    /// <param name="seed">World seed, for the ripple texture.</param>
    /// <returns>The layer, or null when no water file was baked.</returns>
    public static WaterLayer? Open(string directory, ulong seed)
    {
        string path = Path.Combine(directory, WaterFile.FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        System.Collections.Generic.IReadOnlyList<WaterTile> tiles;
        try
        {
            tiles = WaterFile.FromBytes(File.ReadAllBytes(path));
        }
        catch (InvalidDataException e)
        {
            GD.PushError($"[water] {e.Message}");
            return null;
        }

        var layer = new WaterLayer();
        ShaderMaterial material = Material(seed);
        foreach (WaterTile tile in tiles)
        {
            layer.AddChild(new MeshInstance3D { Name = $"Water_{tile.TileX}_{tile.TileZ}", Mesh = ToMesh(tile), MaterialOverride = material });
        }

        layer.TileCount = tiles.Count;
        return layer;
    }

    private static ShaderMaterial Material(ulong seed)
    {
        SurfaceTexture ripple = SurfaceTextureGenerator.Water(seed);
        Image image = Image.CreateFromData(ripple.Size, ripple.Size, false, Image.Format.Rgba8, ripple.AlbedoHeight);
        image.GenerateMipmaps();
        var material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        material.SetShaderParameter("ripple_tex", ImageTexture.CreateFromImage(image));
        material.SetShaderParameter("min_speed", (float)RiverFlow.MinimumSpeed);
        material.SetShaderParameter("max_speed", (float)RiverFlow.MaximumSpeed);
        return material;
    }

    private static ArrayMesh ToMesh(WaterTile tile)
    {
        int n = tile.VertexCount;
        var vertices = new Vector3[n];
        var uvs = new Vector2[n];
        var colours = new Color[n];
        for (int i = 0; i < n; i++)
        {
            vertices[i] = new Vector3(tile.Positions[i * 3], tile.Positions[(i * 3) + 1], tile.Positions[(i * 3) + 2]);
            uvs[i] = new Vector2(tile.Uvs[i * 2], tile.Uvs[(i * 2) + 1]);
            colours[i] = new Color(tile.Colours[i * 4], tile.Colours[(i * 4) + 1], tile.Colours[(i * 4) + 2], tile.Colours[(i * 4) + 3]);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        arrays[(int)Mesh.ArrayType.Index] = tile.Indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
