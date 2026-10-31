using System;
using System.Collections.Generic;
using Godot;

namespace Realm.Game.Actors;

/// <summary>
/// A humanoid built from Kenney's "Animated Characters 3" pack (CC0, see
/// <c>assets/characters/kenney_animated_characters_3/License.txt</c>): one rigged mesh plus three separately
/// authored animation clips, merged onto the model's own <see cref="AnimationPlayer"/> at load time. Replaces an
/// earlier fully procedural rig (ADR 0030): hand-rolled bone rest/pose math kept producing subtly wrong results
/// (see the devlog), and a small CC0 asset is both less code and better looking for the same effort.
/// </summary>
/// <remarks>
/// Main thread only. Expensive relative to the capsule crowd (a skinned mesh instead of one shared draw call), so
/// callers should only use it for a small, near-the-camera pool (see <c>CrowdView</c>), not every character.
/// </remarks>
public sealed partial class HumanoidRig : Node3D
{
    private const string ModelPath = "res://assets/characters/kenney_animated_characters_3/Model/characterMedium.fbx";
    private const string AnimationDirectory = "res://assets/characters/kenney_animated_characters_3/Animations/";
    private const string SkinDirectory = "res://assets/characters/kenney_animated_characters_3/Skins/";
    private const float ModelScale = 0.6375f; // 0.75 further cut 15%
    private const float AnimationSpeedScale = 0.9f; // slows the gait's cycle rate only; the character's own walking speed is unrelated
    private static readonly string[] AnimationClips = { "idle", "run", "jump" };
    private static readonly string[] HumanSkins = { "humanMaleA.png", "humanFemaleA.png" };

    private AnimationPlayer? _player;
    private readonly List<MeshInstance3D> _meshes = new();
    private readonly Dictionary<string, string> _playbackName = new();
    private string _playing = string.Empty;

    /// <summary>Loads the model, merges the animation clips onto it, and picks a skin.</summary>
    /// <param name="skinSeed">Picks which of the pack's human skins to wear (any int; only parity matters).</param>
    public HumanoidRig(int skinSeed = 0)
    {
        Name = "Humanoid";

        var modelScene = GD.Load<PackedScene>(ModelPath);
        Node3D model = modelScene.Instantiate<Node3D>();
        model.Scale = Vector3.One * ModelScale;
        AddChild(model);

        // characterMedium.fbx is just the mesh and skeleton - no AnimationPlayer of its own (the animations only
        // exist in the separate clip files below), so it needs one created for it. A fresh AnimationPlayer's
        // default RootNode is its own parent, which is exactly model here, matching the clip files' own layout
        // (each clip's AnimationPlayer sits next to its own Skeleton3D under its own scene root) closely enough
        // for the merged tracks to resolve.
        _player = new AnimationPlayer { Name = "AnimationPlayer", SpeedScale = AnimationSpeedScale };
        model.AddChild(_player);
        FindMeshInstances(model, _meshes);
        MergeAnimationClips();
        ApplySkin(HumanSkins[Math.Abs(skinSeed) % HumanSkins.Length]);
    }

    /// <summary>Hints the same role colour <c>CrowdView</c> uses for the capsule crowd, without overpowering the skin texture.</summary>
    /// <param name="colour">The role colour; multiplies the skin texture, blended most of the way toward white first.</param>
    public void SetColour(Color colour)
    {
        Color tint = colour.Lerp(Colors.White, 0.55f);
        foreach (MeshInstance3D mesh in _meshes)
        {
            if (mesh.MaterialOverride is StandardMaterial3D material)
            {
                material.AlbedoColor = tint;
            }
        }
    }

    /// <summary>Poses the rig for this frame: facing, and whichever clip (idle or run) fits the current state.</summary>
    /// <param name="deltaSeconds">Unused; kept for parity with the previous procedural rig's signature.</param>
    /// <param name="walking">Whether the character is walking (the run clip, played in place) or standing (idle).</param>
    /// <param name="facingYaw">The yaw the character faces, radians (same convention as <c>NpcSnapshot.Heading</c>).</param>
    public void Animate(double deltaSeconds, bool walking, float facingYaw)
    {
        _ = deltaSeconds;
        // The imported model faces the opposite way to our heading convention (it walked backwards without this).
        Rotation = new Vector3(0, facingYaw + Mathf.Pi, 0);
        string wanted = walking ? "run" : "idle";
        if (wanted == _playing || _player is null || !_playbackName.TryGetValue(wanted, out string? playbackName))
        {
            return;
        }

        _player.Play(playbackName);
        _playing = wanted;
    }

    // Each animation FBX imports as its own little scene (a skeleton and an AnimationPlayer with a default library
    // holding two animations: the real cycle, and a stray single-frame "Targeting Pose" the exporter also wrote -
    // the longest one in the library is always the real cycle, so that's what picks it out, rather than assuming
    // either name or list order. Grafting that whole library onto the model's own player, under our own key, makes
    // the clip playable as "key/animation-name" without hard-coding the FBX authoring tool's exact animation name.
    private void MergeAnimationClips()
    {
        if (_player is null)
        {
            return;
        }

        foreach (string clip in AnimationClips)
        {
            var clipScene = GD.Load<PackedScene>($"{AnimationDirectory}{clip}.fbx");
            Node clipRoot = clipScene.Instantiate();
            AnimationLibrary? library = FindAnimationPlayer(clipRoot)?.GetAnimationLibrary("");
            StringName? longest = null;
            double longestLength = -1;
            if (library is not null)
            {
                foreach (StringName name in library.GetAnimationList())
                {
                    double length = library.GetAnimation(name).Length;
                    if (length > longestLength)
                    {
                        longestLength = length;
                        longest = name;
                    }
                }
            }

            if (library is not null && longest is not null)
            {
                // FBX imports default to not looping, so idle and run would play once (well under a second) and
                // then freeze on their last frame - which looks like a correct pose, just a static one.
                library.GetAnimation(longest).LoopMode = Animation.LoopModeEnum.Linear;
                _player.AddAnimationLibrary(clip, library);
                _playbackName[clip] = $"{clip}/{longest}";
            }

            clipRoot.QueueFree();
        }
    }

    private void ApplySkin(string fileName)
    {
        var texture = GD.Load<Texture2D>($"{SkinDirectory}{fileName}");
        foreach (MeshInstance3D mesh in _meshes)
        {
            mesh.MaterialOverride = new StandardMaterial3D { AlbedoTexture = texture, Roughness = 1f };
        }
    }

    private static AnimationPlayer? FindAnimationPlayer(Node node)
    {
        if (node is AnimationPlayer player)
        {
            return player;
        }

        foreach (Node child in node.GetChildren())
        {
            AnimationPlayer? found = FindAnimationPlayer(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static void FindMeshInstances(Node node, List<MeshInstance3D> into)
    {
        if (node is MeshInstance3D mesh)
        {
            into.Add(mesh);
        }

        foreach (Node child in node.GetChildren())
        {
            FindMeshInstances(child, into);
        }
    }
}
