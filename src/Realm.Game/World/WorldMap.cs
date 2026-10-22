using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Game.World.Settlements;
using Realm.Gen.Settlements.Placement;

namespace Realm.Game.World;

/// <summary>
/// A full-screen map of the whole world (toggle with M): the baked overview image, the roads, the cities and towns with their names,
/// and the player. North (minus Z) is at the top.
/// </summary>
/// <remarks>Main thread only.</remarks>
public sealed partial class WorldMap : CanvasLayer
{
    private const float ScreenShare = 0.92f;
    private const int NameFontSize = 14;
    private static readonly Color RoadColour = new(0.55f, 0.36f, 0.16f);

    private readonly double _worldSize;
    private readonly IReadOnlyList<Settlement> _settlements;
    private readonly IReadOnlyList<IReadOnlyList<(double X, double Z)>> _roads;
    private readonly Control _root = new() { Name = "MapRoot" };
    private readonly TextureRect _image = new() { Name = "MapImage", TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
    private readonly Control _overlay = new() { Name = "Overlay", MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly Polygon2D _player = new() { Name = "Player", Color = new Color(1f, 0.2f, 0.2f), Polygon = new[] { new Vector2(0, -9), new Vector2(6, 7), new Vector2(0, 3), new Vector2(-6, 7) } };
    private float _mapSize;

    private WorldMap(string imagePath, double worldSize, IReadOnlyList<Settlement> settlements, IReadOnlyList<IReadOnlyList<(double X, double Z)>> roads)
    {
        _worldSize = worldSize;
        _settlements = settlements;
        _roads = roads;
        Layer = 5;
        Visible = false;
        Name = "WorldMap";

        var backdrop = new ColorRect { Color = new Color(0.04f, 0.05f, 0.08f, 0.92f) };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(backdrop);
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _image.Texture = ImageTexture.CreateFromImage(Image.LoadFromFile(imagePath));
        _image.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _image.StretchMode = TextureRect.StretchModeEnum.Scale;
        _root.AddChild(_image);
        _image.AddChild(_overlay);
        AddChild(_root);
    }

    /// <inheritdoc />
    public override void _Ready() => Rebuild();

    /// <summary>Opens the map from the baked directory.</summary>
    /// <param name="directory">The baked terrain directory (it holds <c>map.png</c>).</param>
    /// <param name="worldSizeMeters">World side length in metres.</param>
    /// <param name="settlements">The settlements to mark.</param>
    /// <param name="roads">The roads to draw, or null.</param>
    /// <returns>The map, or null when no map image was baked.</returns>
    public static WorldMap? Open(string directory, double worldSizeMeters, IReadOnlyList<Settlement> settlements, RoadsLayer? roads)
    {
        string path = Path.Combine(directory, "map.png");
        if (!File.Exists(path))
        {
            return null;
        }

        return new WorldMap(path, worldSizeMeters, settlements, roads?.Lines() ?? Array.Empty<IReadOnlyList<(double X, double Z)>>());
    }

    /// <summary>Lays the map out for the current window size; call when the window changes.</summary>
    public void Rebuild()
    {
        Vector2I window = GetWindow().Size;
        _mapSize = Math.Min(window.X, window.Y) * ScreenShare;
        _image.Size = new Vector2(_mapSize, _mapSize);
        _image.Position = new Vector2((window.X - _mapSize) / 2, (window.Y - _mapSize) / 2);
        foreach (Node child in _overlay.GetChildren())
        {
            child.QueueFree();
        }

        foreach (IReadOnlyList<(double X, double Z)> road in _roads)
        {
            var line = new Line2D { Width = 2f, DefaultColor = RoadColour, JointMode = Line2D.LineJointMode.Round };
            foreach ((double x, double z) in road)
            {
                line.AddPoint(ToMap(x, z));
            }

            _overlay.AddChild(line);
        }

        foreach (Settlement s in _settlements)
        {
            float size = s.Tier switch { SettlementTier.City => 12, SettlementTier.Town => 8, _ => 5 };
            Vector2 at = ToMap(s.X, s.Z);
            _overlay.AddChild(new ColorRect { Position = at - new Vector2(size / 2, size / 2), Size = new Vector2(size, size), Color = s.Tier == SettlementTier.City ? new Color(1f, 0.95f, 0.5f) : Colors.White });
            var label = new Label { Text = s.Name, Position = at + new Vector2(size / 2 + 3, -NameFontSize * 0.8f) };
            label.AddThemeFontSizeOverride("font_size", NameFontSize);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 5);
            _overlay.AddChild(label);
        }

        _player.GetParent()?.RemoveChild(_player);
        _overlay.AddChild(_player);
    }

    /// <summary>Moves the player marker.</summary>
    /// <param name="x">World X in metres.</param>
    /// <param name="z">World Z in metres.</param>
    /// <param name="yawRadians">The player's yaw (0 faces north, minus Z).</param>
    public void SetPlayer(double x, double z, float yawRadians)
    {
        _player.Position = ToMap(x, z);
        _player.Rotation = -yawRadians;
    }

    private Vector2 ToMap(double x, double z) =>
        new((float)(((x / _worldSize) + 0.5) * _mapSize), (float)(((z / _worldSize) + 0.5) * _mapSize));
}
