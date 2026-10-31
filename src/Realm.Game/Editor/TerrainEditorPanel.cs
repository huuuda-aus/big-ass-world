using Godot;
using Realm.Gen.Bake;

namespace Realm.Game.Editor;

/// <summary>
/// The terrain editor's dock UI: pick a region, pick a brush, load, sculpt, save or revert. Built in code (no
/// scene file) so the whole plugin is three readable C# files. Holds no editing logic itself; it only reads and
/// writes the controls, and <see cref="TerrainEditorPlugin"/> reacts to its button presses.
/// </summary>
public sealed partial class TerrainEditorPanel : VBoxContainer
{
    private SpinBox _regionX = null!;
    private SpinBox _regionZ = null!;
    private OptionButton _mode = null!;
    private SpinBox _radius = null!;
    private SpinBox _strength = null!;
    private CheckButton _sculptEnabled = null!;
    private Label _lockStatus = null!;
    private Label _status = null!;

    /// <summary>Raised when "Load" is pressed, with the chosen region coordinate.</summary>
    [Signal]
    public delegate void LoadRequestedEventHandler(int regionX, int regionZ);

    /// <summary>Raised when "Save" is pressed.</summary>
    [Signal]
    public delegate void SaveRequestedEventHandler();

    /// <summary>Raised when "Revert" is pressed.</summary>
    [Signal]
    public delegate void RevertRequestedEventHandler();

    /// <summary>Raised when "Rebuild preview" is pressed.</summary>
    [Signal]
    public delegate void RebuildPreviewRequestedEventHandler();

    /// <summary>Raised when "Unlock region" is pressed.</summary>
    [Signal]
    public delegate void UnlockRequestedEventHandler();

    /// <summary>Whether sculpting is armed: while on, left-drag in the 3D viewport paints the loaded region.</summary>
    public bool SculptEnabled => _sculptEnabled.ButtonPressed;

    /// <summary>The brush mode currently selected.</summary>
    public BrushMode Mode => (BrushMode)_mode.Selected;

    /// <summary>The brush radius in vertices, from the dock's spin box.</summary>
    public double RadiusVertices => _radius.Value;

    /// <summary>
    /// The brush strength from the dock's spin box: metres per dab for <see cref="BrushMode.Raise"/> and
    /// <see cref="BrushMode.Lower"/>, a 0 to 1 blend rate for <see cref="BrushMode.Smooth"/> and <see cref="BrushMode.Flatten"/>.
    /// </summary>
    public double Strength => _strength.Value;

    /// <inheritdoc />
    public override void _Ready()
    {
        AddChild(new Label { Text = "Terrain editor" });

        var regionRow = new HBoxContainer();
        regionRow.AddChild(new Label { Text = "Region X" });
        _regionX = new SpinBox { MinValue = -512, MaxValue = 512, Value = 0 };
        regionRow.AddChild(_regionX);
        regionRow.AddChild(new Label { Text = "Z" });
        _regionZ = new SpinBox { MinValue = -512, MaxValue = 512, Value = 0 };
        regionRow.AddChild(_regionZ);
        AddChild(regionRow);

        var loadButton = new Button { Text = "Load region" };
        loadButton.Pressed += () => EmitSignal(SignalName.LoadRequested, (int)_regionX.Value, (int)_regionZ.Value);
        AddChild(loadButton);

        AddChild(new HSeparator());

        _mode = new OptionButton();
        _mode.AddItem("Raise", (int)BrushMode.Raise);
        _mode.AddItem("Lower", (int)BrushMode.Lower);
        _mode.AddItem("Smooth", (int)BrushMode.Smooth);
        _mode.AddItem("Flatten", (int)BrushMode.Flatten);
        AddChild(_mode);

        var radiusRow = new HBoxContainer();
        radiusRow.AddChild(new Label { Text = "Radius (vertices)" });
        _radius = new SpinBox { MinValue = 0.5, MaxValue = 64, Step = 0.5, Value = 6 };
        radiusRow.AddChild(_radius);
        AddChild(radiusRow);

        var strengthRow = new HBoxContainer();
        strengthRow.AddChild(new Label { Text = "Strength" });
        _strength = new SpinBox { MinValue = 0.01, MaxValue = 50, Step = 0.01, Value = 1 };
        strengthRow.AddChild(_strength);
        AddChild(strengthRow);

        _sculptEnabled = new CheckButton { Text = "Sculpt (left-drag in the 3D view)" };
        AddChild(_sculptEnabled);

        AddChild(new HSeparator());

        var buttonRow = new HBoxContainer();
        var saveButton = new Button { Text = "Save" };
        saveButton.Pressed += () => EmitSignal(SignalName.SaveRequested);
        buttonRow.AddChild(saveButton);
        var revertButton = new Button { Text = "Revert" };
        revertButton.Pressed += () => EmitSignal(SignalName.RevertRequested);
        buttonRow.AddChild(revertButton);
        var rebuildButton = new Button { Text = "Rebuild preview" };
        rebuildButton.Pressed += () => EmitSignal(SignalName.RebuildPreviewRequested);
        buttonRow.AddChild(rebuildButton);
        AddChild(buttonRow);

        var lockRow = new HBoxContainer();
        _lockStatus = new Label { Text = "Not locked." };
        lockRow.AddChild(_lockStatus);
        var unlockButton = new Button { Text = "Unlock region" };
        unlockButton.Pressed += () => EmitSignal(SignalName.UnlockRequested);
        lockRow.AddChild(unlockButton);
        AddChild(lockRow);

        _status = new Label { Text = "No region loaded." };
        AddChild(_status);
    }

    /// <summary>Sets the status line shown at the bottom of the dock.</summary>
    /// <param name="text">The text to show.</param>
    public void SetStatus(string text) => _status.Text = text;

    /// <summary>
    /// Sets the lock indicator. "Save" locks a region automatically (CLAUDE.md section 4.4); this only shows
    /// whether that has happened, it never changes the lock itself.
    /// </summary>
    /// <param name="locked">Whether the loaded region is currently locked.</param>
    public void SetLockStatus(bool locked) =>
        _lockStatus.Text = locked ? "Locked (bake-terrain will keep this tile)." : "Not locked.";

    /// <summary>The region coordinate currently in the dock's spin boxes (the one "Load region" would open).</summary>
    public (int X, int Z) SelectedRegion => ((int)_regionX.Value, (int)_regionZ.Value);
}
