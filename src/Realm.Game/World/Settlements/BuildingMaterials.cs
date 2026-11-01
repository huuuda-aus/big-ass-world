using System;
using Godot;

namespace Realm.Game.World.Settlements;

/// <summary>
/// Loads the small set of real textures buildings use (CC0, Kenney "Retro Textures Fantasy", see
/// <c>assets/buildings/kenney_retro-textures-fantasy/License.txt</c>) into one <see cref="Texture2DArray"/>, and
/// the shader material that samples it, so a whole building still costs one draw call.
/// <see cref="Realm.Gen.Props.PropMaterial"/> names which layer a triangle wants;
/// <see cref="Realm.Gen.Props.PropMaterial.None"/> triangles get the flat vertex colour instead, exactly as every
/// prop mesh rendered before texturing existed.
/// </summary>
public static class BuildingMaterials
{
    private const string AssetDir = "res://assets/buildings/kenney_retro-textures-fantasy/";

    // Layer order matches Realm.Gen.Props.PropMaterial minus one (None has no layer): WallPlaster, Mud,
    // RoofThatch, RoofTileRed, RoofTileGrey, Wood, Stone. Tile size is the material's own repeat size in metres
    // (how far a 0..1 UV span covers), chosen so every material reads at the same apparent grain, about 64 px/m.
    private static readonly (string File, float TileX, float TileY)[] Layers =
    {
        ("wall_brick_sand_center.png", 1.0f, 2.0f),
        ("floor_ground_dirt.png", 1.0f, 1.0f),
        ("roof_thatch_center.png", 1.0f, 1.0f),
        ("roof_clay_red_center.png", 1.0f, 1.0f),
        ("roof_clay_grey_center.png", 1.0f, 1.0f),
        ("floor_wood_planks.png", 0.5f, 0.5f),
        ("wall_stone.png", 1.0f, 2.0f),
    };

    /// <summary>Tile size in metres (U, V) for a texture array layer (0-based, as in <see cref="Layers"/>).</summary>
    /// <param name="layer">Layer index.</param>
    /// <returns>How many metres a full 0..1 UV span covers in each direction.</returns>
    public static (float TileX, float TileY) TileSize(int layer) => (Layers[layer].TileX, Layers[layer].TileY);

    // See Realm.Gen.Props.PropMaterial for what UV2.x -1 versus a layer index means.
    private const string ShaderCode = @"
shader_type spatial;
render_mode blend_mix;

uniform sampler2DArray tex_array : source_color, filter_nearest, repeat_enable;

varying float v_layer;

void vertex() {
	v_layer = UV2.x;
}

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}

void fragment() {
	vec3 vcol = srgb_to_linear(COLOR.rgb);
	ALBEDO = v_layer < 0.0 ? vcol : texture(tex_array, vec3(UV, v_layer)).rgb * vcol;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
}
";

    private static readonly Lazy<ShaderMaterial> LazyMaterial = new(Build);

    /// <summary>The one material every textured building mesh uses.</summary>
    public static ShaderMaterial Textured => LazyMaterial.Value;

    private static ShaderMaterial Build()
    {
        const Image.Format format = Image.Format.Rgba8;
        var raw = new Image[Layers.Length];
        int width = -1;
        int height = 0;
        for (int i = 0; i < Layers.Length; i++)
        {
            var image = new Image();
            Error err = image.Load(AssetDir + Layers[i].File);
            if (err != Error.Ok)
            {
                throw new InvalidOperationException($"could not load building texture '{Layers[i].File}': {err}");
            }

            image.Convert(format);
            if (width < 0)
            {
                width = image.GetWidth();
            }
            else if (image.GetWidth() != width)
            {
                throw new InvalidOperationException($"building texture '{Layers[i].File}' is {image.GetWidth()} px wide, expected {width} (every layer of a texture array must share a width)");
            }

            height = Math.Max(height, image.GetHeight());
            raw[i] = image;
        }

        var images = new Godot.Collections.Array<Image>();
        foreach (Image image in raw)
        {
            images.Add(Pad(image, height, format));
        }

        var array = new Texture2DArray();
        array.CreateFromImages(images);

        var material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        material.SetShaderParameter("tex_array", array);
        return material;
    }

    // Tiles a seamless image downward to fill a taller canvas (every source here tiles vertically too, so
    // repeating it loses no seam); every layer of a Texture2DArray must share one height.
    private static Image Pad(Image source, int height, Image.Format format)
    {
        if (source.GetHeight() == height)
        {
            return source;
        }

        Image padded = Image.CreateEmpty(source.GetWidth(), height, false, format);
        for (int y = 0; y < height; y += source.GetHeight())
        {
            padded.BlitRect(source, new Rect2I(0, 0, source.GetWidth(), source.GetHeight()), new Vector2I(0, y));
        }

        return padded;
    }
}
