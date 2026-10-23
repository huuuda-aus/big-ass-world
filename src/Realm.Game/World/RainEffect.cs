using System;
using Godot;

namespace Realm.Game.World;

/// <summary>Falling rain around the viewer: streaks that stay in the world as the viewer moves, thicker as the rain gets heavier.</summary>
/// <remarks>Main thread only. A child of the camera. The number of drops changes in steps so the particle system is not restarted every frame.</remarks>
public sealed partial class RainEffect : GpuParticles3D
{
    private const int MaxDrops = 3200;
    private const int MinDrops = 200;
    private const double StepSize = 0.1;
    private const float Height = 14f;
    private const float Reach = 22f;

    private double _applied = -1;

    /// <summary>Creates the effect (not raining).</summary>
    public RainEffect()
    {
        Position = new Vector3(0, Height, 0);
        Amount = MinDrops;
        Lifetime = 1.3;
        Preprocess = 1.3;
        Emitting = false;
        LocalCoords = false;
        VisibilityAabb = new Aabb(new Vector3(-Reach, -Height * 2, -Reach), new Vector3(Reach * 2, Height * 2.2f, Reach * 2));
        CastShadow = ShadowCastingSetting.Off;
        ProcessMaterial = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(Reach, 0.5f, Reach),
            Direction = new Vector3(0.12f, -1f, 0.05f),
            Spread = 1.5f,
            InitialVelocityMin = 17f,
            InitialVelocityMax = 21f,
            Gravity = Vector3.Zero,
        };
        DrawPass1 = new BoxMesh
        {
            Size = new Vector3(0.015f, 0.6f, 0.015f),
            Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(0.72f, 0.82f, 0.95f, 0.5f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
        };
    }

    /// <summary>Sets how hard it rains.</summary>
    /// <param name="intensity">0 (dry) to 1 (downpour).</param>
    public void SetIntensity(double intensity)
    {
        double stepped = Math.Round(Math.Clamp(intensity, 0, 1) / StepSize) * StepSize;
        if (stepped == _applied)
        {
            return;
        }

        _applied = stepped;
        Emitting = stepped > 0;
        Amount = MinDrops + (int)((MaxDrops - MinDrops) * stepped);
    }
}
