using System;
using System.IO;
using Godot;
using Realm.Gen.Bake;

namespace Realm.Game.Editor;

/// <summary>
/// Godot editor plugin for hand-sculpting baked terrain regions (CLAUDE.md section 6.1, "basic terrain editor").
/// The dock ( <see cref="TerrainEditorPanel"/>) picks a region and a brush; this class turns mouse drags in the 3D
/// viewport into <see cref="BrushStroke"/>s, applies them through <see cref="TerrainEditSession"/>, and shows the
/// edited region as a plain vertex-coloured mesh (the same look the game shows before real terrain textures exist).
/// </summary>
/// <remarks>
/// Lives inside <c>Realm.Game</c>, not a separate <c>Realm.Editor</c> project (see ADR 0028): Godot's .NET build
/// compiles exactly one project per Godot project, so editor-plugin code that needs <c>TerrainBridge</c>-adjacent
/// Godot types has to live in the same assembly as the game. The Godot-free brush and raycast math it calls
/// (<see cref="TerrainTileEditor"/>, <see cref="RegionTileRaycast"/>) lives in <c>Realm.Gen.Bake</c> and is unit-tested there.
/// Known limitations: no undo, one region at a time, no live preview while dragging (the mesh only rebuilds on
/// mouse-up or "Rebuild preview", to avoid rebuilding a ~65,000-vertex mesh every frame).
/// </remarks>
[Tool]
public partial class TerrainEditorPlugin : EditorPlugin
{
    private TerrainEditorPanel _panel = null!;
    private TerrainEditSession? _session;
    private MeshInstance3D? _previewMesh;
    private bool _dragging;
    private double _flattenHeight;

    /// <inheritdoc />
    public override void _EnterTree()
    {
        _panel = new TerrainEditorPanel { Name = "Terrain Editor" };
        // LeftUl tabs alongside Scene/Import, so the dock is visible immediately; LeftBr/Ur etc. open a new,
        // often near-zero-width column that needs a manual drag to reveal.
        AddControlToDock(DockSlot.LeftUl, _panel);
        _panel.LoadRequested += OnLoadRequested;
        _panel.SaveRequested += OnSaveRequested;
        _panel.RevertRequested += OnRevertRequested;
        _panel.RebuildPreviewRequested += () => RebuildPreview();
        _panel.UnlockRequested += OnUnlockRequested;
        SetInputEventForwardingAlwaysEnabled();
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        RemoveControlFromDocks(_panel);
        _panel.QueueFree();
        _previewMesh?.QueueFree();
        _previewMesh = null;
        _session = null;
    }

    /// <inheritdoc />
    public override bool _Handles(GodotObject @object) => true; // always claim the 3D viewport; see SetInputEventForwardingAlwaysEnabled above.

    /// <inheritdoc />
    /// <remarks>The base method's generated signature returns the raw <c>int</c> behind <see cref="EditorPlugin.AfterGuiInput"/>, not the enum itself.</remarks>
    public override int _Forward3DGuiInput(Camera3D viewportCamera, InputEvent @event)
    {
        if (_session is null || !_panel.SculptEnabled)
        {
            return (int)AfterGuiInput.Pass;
        }

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouseButton)
        {
            if (mouseButton.Pressed)
            {
                _dragging = TryApplyDab(viewportCamera, mouseButton.Position, captureFlattenAnchor: true);
                return (int)(_dragging ? AfterGuiInput.Stop : AfterGuiInput.Pass);
            }

            if (_dragging)
            {
                _dragging = false;
                RebuildPreview();
                return (int)AfterGuiInput.Stop;
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion && _dragging)
        {
            TryApplyDab(viewportCamera, mouseMotion.Position, captureFlattenAnchor: false);
            return (int)AfterGuiInput.Stop;
        }

        return (int)AfterGuiInput.Pass;
    }

    private void OnLoadRequested(int regionX, int regionZ)
    {
        try
        {
            _session = TerrainEditSession.Open(DefaultTerrainDirectory(), new Vector2I(regionX, regionZ));
            RebuildPreview();
            _panel.SetLockStatus(_session.IsLocked);
            _panel.SetStatus($"Loaded region ({regionX}, {regionZ}){(_session.IsLocked ? ", locked" : string.Empty)}.");
        }
        catch (Exception e) when (e is FileNotFoundException or ArgumentOutOfRangeException or InvalidDataException)
        {
            _session = null;
            _panel.SetStatus($"Load failed: {e.Message}");
        }
    }

    private void OnSaveRequested()
    {
        if (_session is null)
        {
            _panel.SetStatus("Load a region first.");
            return;
        }

        try
        {
            _session.Save();
            _panel.SetLockStatus(_session.IsLocked);
            _panel.SetStatus($"Saved and locked region ({_session.Location.X}, {_session.Location.Y}).");
        }
        catch (IOException e)
        {
            _panel.SetStatus($"Save failed: {e.Message}");
        }
    }

    private void OnRevertRequested()
    {
        if (_session is null)
        {
            return;
        }

        _session.Revert();
        RebuildPreview();
        _panel.SetStatus("Reverted to the saved tile.");
    }

    private void OnUnlockRequested()
    {
        if (_session is null)
        {
            _panel.SetStatus("Load a region first.");
            return;
        }

        _session.Unlock();
        _panel.SetLockStatus(_session.IsLocked);
        _panel.SetStatus($"Unlocked region ({_session.Location.X}, {_session.Location.Y}); the next bake-terrain can overwrite it.");
    }

    private bool TryApplyDab(Camera3D camera, Vector2 screenPosition, bool captureFlattenAnchor)
    {
        if (_session is null || _previewMesh is null)
        {
            return false;
        }

        Vector3 worldOrigin = camera.ProjectRayOrigin(screenPosition);
        Vector3 worldDirection = camera.ProjectRayNormal(screenPosition);
        Vector3 localOrigin = worldOrigin - _previewMesh.GlobalPosition;

        if (!RegionTileRaycast.TryIntersect(
                _session.Tile,
                new System.Numerics.Vector3(localOrigin.X, localOrigin.Y, localOrigin.Z),
                new System.Numerics.Vector3(worldDirection.X, worldDirection.Y, worldDirection.Z),
                camera.Far,
                out System.Numerics.Vector3 hit))
        {
            return false;
        }

        (double vertexX, double vertexZ) = RegionTileRaycast.ToVertexCoordinates(_session.Tile, hit.X, hit.Z);
        if (captureFlattenAnchor && _panel.Mode == BrushMode.Flatten)
        {
            _flattenHeight = hit.Y;
        }

        _session.ApplyStroke(new BrushStroke
        {
            CenterX = vertexX,
            CenterZ = vertexZ,
            RadiusVertices = _panel.RadiusVertices,
            Strength = _panel.Strength,
            Mode = _panel.Mode,
            FlattenHeight = _flattenHeight,
        });
        return true;
    }

    private void RebuildPreview()
    {
        if (_session is null)
        {
            return;
        }

        Node? root = EditorInterface.Singleton.GetEditedSceneRoot();
        if (root is null)
        {
            _panel.SetStatus("Open a scene in the editor first.");
            return;
        }

        if (_previewMesh is null)
        {
            _previewMesh = new MeshInstance3D
            {
                Name = "TerrainEditorPreview",
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    VertexColorUseAsAlbedo = true,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
            };
            root.AddChild(_previewMesh); // not owned: an editor aid only, never saved into the scene.
        }

        (double westX, double northZ) = RegionWorldOrigin(_session.Manifest, _session.Location);
        _previewMesh.Position = new Vector3((float)westX, 0, (float)northZ);
        _previewMesh.Mesh = BuildPreviewMesh(_session.Tile);
    }

    // Matches TerrainBaker.BuildTile's own west/north corner formula, so the preview sits exactly where bake-terrain put the region.
    private static (double WestX, double NorthZ) RegionWorldOrigin(TerrainManifest manifest, Vector2I location)
    {
        double half = manifest.WorldSizeMeters / 2.0;
        int originVertexX = (location.X + (manifest.RegionsPerAxis / 2)) * manifest.RegionSize;
        int originVertexZ = (location.Y + (manifest.RegionsPerAxis / 2)) * manifest.RegionSize;
        return (-half + (originVertexX * (double)manifest.SpacingMeters), -half + (originVertexZ * (double)manifest.SpacingMeters));
    }

    private static Mesh BuildPreviewMesh(RegionTile tile)
    {
        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
        int size = tile.Size;
        for (int iz = 0; iz < size - 1; iz++)
        {
            for (int ix = 0; ix < size - 1; ix++)
            {
                AddQuad(surfaceTool, tile, ix, iz);
            }
        }

        return surfaceTool.Commit();
    }

    private static void AddQuad(SurfaceTool surfaceTool, RegionTile tile, int ix, int iz)
    {
        AddVertex(surfaceTool, tile, ix, iz);
        AddVertex(surfaceTool, tile, ix, iz + 1);
        AddVertex(surfaceTool, tile, ix + 1, iz);

        AddVertex(surfaceTool, tile, ix + 1, iz);
        AddVertex(surfaceTool, tile, ix, iz + 1);
        AddVertex(surfaceTool, tile, ix + 1, iz + 1);
    }

    private static void AddVertex(SurfaceTool surfaceTool, RegionTile tile, int ix, int iz)
    {
        int size = tile.Size;
        int i = (iz * size) + ix;
        surfaceTool.SetColor(UnpackColour(tile.Colours[i]));
        surfaceTool.AddVertex(new Vector3(ix * tile.SpacingMeters, tile.Heights[i], iz * tile.SpacingMeters));
    }

    // 0xAABBGGRR: red in the lowest byte (RegionTile's documented layout).
    private static Color UnpackColour(uint packed) => new(
        (packed & 0xFFu) / 255f,
        ((packed >> 8) & 0xFFu) / 255f,
        ((packed >> 16) & 0xFFu) / 255f,
        ((packed >> 24) & 0xFFu) / 255f);

    private static string DefaultTerrainDirectory() =>
        Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "baked", "terrain"));
}
