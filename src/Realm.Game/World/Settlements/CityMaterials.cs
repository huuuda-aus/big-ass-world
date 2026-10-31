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

    // Real fountain ripples are concentric rings expanding from where the falling water actually strikes the
    // surface, not a travelling chop: two ring trains from that one point (slightly different ring spacing and
    // speed, so they interfere and never look perfectly regular), each fading out with distance the way a real
    // ripple loses energy as it spreads. A fresnel term brightens the rim the way a wet, reflective surface does
    // at a glancing angle. `ripple_origin` is the drop point in the mesh's own local space (the landmark's plan
    // position; the two basin tiers share one water mesh and both catch water on the same central axis, so one
    // origin serves both). `wave_scale` and `wave_strength` are per-material so a small fountain basin and a
    // (future) larger pond are not forced to the same ripple size. All of this perturbs only the normal, not the
    // geometry, so it costs nothing extra on the basin's low vertex count: a handful of trig calls per fragment on
    // a small on-screen shape, lit by the scene's own lights.
    private const string RippleShader = @"
shader_type spatial;
render_mode blend_mix, cull_disabled, specular_schlick_ggx, diffuse_lambert;

uniform vec4 albedo : source_color = vec4(0.29, 0.53, 0.68, 0.78);
uniform vec2 ripple_origin = vec2(0.0, 0.0);
uniform float wave_scale = 18.0;    // higher = shorter rings (roughly 2*PI / wave_scale metres between crests)
uniform float wave_speed = 3.2;     // how fast the rings travel outward
uniform float wave_strength = 0.18; // normal perturbation amount
uniform float wave_falloff = 0.35;  // how fast rings fade with distance from the drop point

varying vec3 local_pos;

void vertex() {
    local_pos = VERTEX;
}

// One ring train: height h = sin(dist*scale - t*speed) * exp(-dist*falloff), and its radial derivative, turned
// into an x/z gradient via the unit direction away from the origin (so the bump always points outward/inward,
// never sideways across the ring the way a directional wave would).
void ring(float dist, vec2 dir, float t, float scale, float speed, float falloff, float amp, inout float dhx, inout float dhz) {
    float decay = exp(-dist * falloff);
    float phase = dist * scale - t * speed;
    float dh = amp * (scale * cos(phase) * decay - sin(phase) * falloff * decay);
    dhx += dh * dir.x;
    dhz += dh * dir.y;
}

void fragment() {
    vec2 p = local_pos.xz - ripple_origin;
    float dist = max(length(p), 0.001);
    vec2 dir = p / dist;
    float t = TIME * wave_speed;
    float dhx = 0.0;
    float dhz = 0.0;
    ring(dist, dir, t, wave_scale, 1.0, wave_falloff, 1.0, dhx, dhz);
    ring(dist, dir, t, wave_scale * 1.7, 1.35, wave_falloff * 1.3, 0.55, dhx, dhz);
    NORMAL = normalize(vec3(-dhx * wave_strength, 1.0, -dhz * wave_strength));
    float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
    ALBEDO = mix(albedo.rgb, vec3(1.0), fresnel * 0.5);
    ALPHA = clamp(albedo.a + fresnel * 0.2, 0.0, 1.0);
    ROUGHNESS = 0.08;
    SPECULAR = 0.9;
}
";

    private static readonly Shader RippleWaterShader = new() { Code = RippleShader };

    /// <summary>
    /// Translucent water with concentric ripples expanding from where falling water strikes it, sized to one water
    /// body: a small basin gets tight, fast rings; a larger one (ponds, eventually) gets proportionally wider,
    /// slower ones, instead of every water surface sharing one ripple size (a fragment-only effect: cheap
    /// regardless of the basin's vertex count).
    /// </summary>
    /// <param name="radiusMeters">The water body's own radius; drives the ripple wavelength, amplitude and fade.</param>
    /// <param name="originLocal">Where the water actually lands, in the mesh's own local X/Z (plan) coordinates.</param>
    /// <returns>A new material (not shared: each water body gets its own uniforms).</returns>
    public static ShaderMaterial WaterFor(float radiusMeters, Vector2 originLocal)
    {
        float radius = Mathf.Max(0.2f, radiusMeters);
        var material = new ShaderMaterial { Shader = RippleWaterShader };
        material.SetShaderParameter("ripple_origin", originLocal);
        // Roughly 3-6 ring crests across the radius, whatever the basin's size; amplitude eases up a little, and
        // rings fade out over a shorter distance, on a calmer, larger surface (a big pond is not as constantly
        // agitated as a fountain jet's own small basin).
        material.SetShaderParameter("wave_scale", Mathf.Clamp(14f / radius, 4f, 30f));
        material.SetShaderParameter("wave_strength", Mathf.Clamp(0.22f / Mathf.Sqrt(radius), 0.06f, 0.22f));
        material.SetShaderParameter("wave_falloff", Mathf.Clamp(1.2f / radius, 0.15f, 1.5f));
        return material;
    }

    /// <summary>Glowing flames of a pyre.</summary>
    public static StandardMaterial3D Fire { get; } = new()
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
}
