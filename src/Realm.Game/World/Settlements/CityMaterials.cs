using Godot;

namespace Realm.Game.World.Settlements;

/// <summary>The few materials every city mesh uses. Meshes carry their colours in vertex colours.</summary>
public static class CityMaterials
{
    /// <summary>Lit, vertex-coloured surface for walls, roofs, floors and props.</summary>
    public static StandardMaterial3D Solid { get; } = new()
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        Roughness = 1f,
    };

    /// <summary>Translucent water for fountains.</summary>
    public static StandardMaterial3D Water { get; } = new()
    {
        AlbedoColor = new Color(0.29f, 0.53f, 0.68f, 0.8f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.2f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// <summary>Glowing flames of a pyre.</summary>
    public static StandardMaterial3D Fire { get; } = new()
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
}
