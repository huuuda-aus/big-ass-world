using System;
using Godot;

namespace Realm.Game.World.Settlements;

/// <summary>
/// The materials that light houses at night, and the one global they share: how dark it is (<see cref="SetNight"/>, set by
/// <see cref="DayNightCycle"/> each frame). The state of an individual house (is anyone home, how bright is its lamp) is an
/// instance parameter on its own meshes.
/// </summary>
/// <remarks>Main thread only. Ported from the CityGen viewer's indoor shader, lamp gain and window aura.</remarks>
public static class CityLighting
{
    /// <summary>Name of the global shader parameter holding the darkness, 0 by day to 1 at night.</summary>
    public const string NightParameter = "realm_night_light";

    private const int AuraTextureSize = 128;
    private const float AuraInner = 0.3f;
    private const float AuraMiddle = 0.65f;

    private static bool _globalsAdded;
    private static ShaderMaterial? _interior;
    private static StandardMaterial3D? _glass;
    private static ShaderMaterial? _glow;
    private static ShaderMaterial? _aura;

    private const string ColourFunctions = @"
vec3 to_linear(vec3 c) {
    return mix(pow((c + vec3(0.055)) / vec3(1.055), vec3(2.4)), c / vec3(12.92), lessThan(c, vec3(0.04045)));
}
";

    // Walls, floors and ceilings of rooms: vertex colour is the surface, its alpha the share of the sky's ambient light that reaches the
    // room, UV.x says the room has a lamp and UV.y is the height of its floor. The ambient light is scaled by the room's fill, shaded by
    // form (skirting, fade up the wall, ceiling a little darker); a lit room adds light of its own that does not depend on the sky.
    private const string InteriorShader = @"
shader_type spatial;
render_mode cull_back;

global uniform float realm_night_light;
instance uniform float lamp_level = 0.0;
instance uniform float lamp_gain = 3.0;
uniform float storey = 3.0;
uniform float lamp_strength = 0.85;

varying float v_fill;
varying float v_lamp;
varying float v_rise;
varying vec3 v_world_normal;
" + ColourFunctions + @"
void vertex() {
    v_fill = COLOR.a;
    v_lamp = UV.x;
    v_rise = VERTEX.y - UV.y;
    v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
    ALBEDO = to_linear(COLOR.rgb);
    ROUGHNESS = 1.0;
    float wall = 1.0 - smoothstep(0.35, 0.7, abs(v_world_normal.y));
    float h = clamp(v_rise / max(storey, 0.001), 0.0, 1.0);
    float skirting = v_rise < 0.12 ? 0.55 : 1.0;
    float fade = mix(0.78, 1.0, smoothstep(0.0, 0.4, h)) * (1.0 - 0.18 * smoothstep(0.78, 1.0, h));
    float facing = 0.74 + 0.26 * (0.5 + 0.5 * dot(normalize(v_world_normal.xz + vec2(1e-5)), vec2(0.6, 0.8)));
    float ceiling_shade = h > 0.5 ? 0.82 : 1.0;
    float shade = mix(ceiling_shade, skirting * fade * facing, wall);
    AO = v_fill * shade;
    AO_LIGHT_AFFECT = 0.0;
    float lamp = v_lamp * lamp_level * lamp_gain * smoothstep(0.2, 0.7, realm_night_light);
    EMISSION = ALBEDO * vec3(1.0, 0.78, 0.5) * lamp * lamp_strength * shade;
}
";

    // The painted glow over a far house's windows: fades in with the dark, only while someone is home.
    private const string GlowShader = @"
shader_type spatial;
render_mode unshaded, cull_back;

global uniform float realm_night_light;
instance uniform float lamp_level = 0.0;
" + ColourFunctions + @"
void fragment() {
    ALBEDO = to_linear(COLOR.rgb);
    ALPHA = smoothstep(0.2, 0.7, realm_night_light) * lamp_level;
}
";

    // The light a lit window throws on the street and the walls round it: additive, depth-tested, pulled toward the camera a little so
    // it does not fight the surface it lies on.
    private const string AuraShader = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled;

global uniform float realm_night_light;
instance uniform float lamp_level = 0.0;
uniform sampler2D falloff : source_color, filter_linear;

void vertex() {
    vec4 view = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
    view.xyz -= normalize(view.xyz) * (0.04 + 0.003 * length(view.xyz));
    POSITION = PROJECTION_MATRIX * view;
}

void fragment() {
    float amount = smoothstep(0.2, 0.7, realm_night_light) * lamp_level;
    ALBEDO = COLOR.rgb * texture(falloff, UV).r * amount;
}
";

    /// <summary>Darkness, 0 by day to 1 at night; as set by <see cref="SetNight"/>.</summary>
    public static float Night { get; private set; }

    /// <summary>Creates the global shader parameter; call once before the first night material is used.</summary>
    public static void EnsureGlobals()
    {
        if (_globalsAdded)
        {
            return;
        }

        RenderingServer.GlobalShaderParameterAdd(NightParameter, RenderingServer.GlobalShaderParameterType.Float, 0f);
        _globalsAdded = true;
    }

    /// <summary>Sets how dark it is for every house shader.</summary>
    /// <param name="night">0 by day to 1 at night.</param>
    public static void SetNight(float night)
    {
        EnsureGlobals();
        Night = Math.Clamp(night, 0f, 1f);
        RenderingServer.GlobalShaderParameterSet(NightParameter, Night);
    }

    /// <summary>Surfaces inside a house (see the shader notes above).</summary>
    public static ShaderMaterial Interior => _interior ??= Make(InteriorShader);

    /// <summary>Window panes: a pale blue, translucent, two-sided, not writing depth.</summary>
    public static StandardMaterial3D Glass => _glass ??= new StandardMaterial3D
    {
        AlbedoColor = new Color(0.44f, 0.63f, 0.75f, 0.35f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        Roughness = 0.15f,
    };

    /// <summary>Glowing panes painted on a far house.</summary>
    public static ShaderMaterial Glow => _glow ??= Make(GlowShader);

    /// <summary>The glow round a lit window.</summary>
    public static ShaderMaterial Aura
    {
        get
        {
            if (_aura is null)
            {
                _aura = Make(AuraShader);
                _aura.SetShaderParameter("falloff", FalloffTexture());
            }

            return _aura;
        }
    }

    private static ShaderMaterial Make(string code)
    {
        EnsureGlobals();
        return new ShaderMaterial { Shader = new Shader { Code = code } };
    }

    // Soft round falloff: bright in the middle, fading to nothing at the edge.
    private static ImageTexture FalloffTexture()
    {
        var image = Image.CreateEmpty(AuraTextureSize, AuraTextureSize, false, Image.Format.R8);
        float half = AuraTextureSize / 2f;
        for (int y = 0; y < AuraTextureSize; y++)
        {
            for (int x = 0; x < AuraTextureSize; x++)
            {
                float d = Math.Min(1f, MathF.Sqrt(((x + 0.5f - half) * (x + 0.5f - half)) + ((y + 0.5f - half) * (y + 0.5f - half))) / half);
                float v = d < AuraInner ? Mix(1f, 0.55f, d / AuraInner)
                    : d < AuraMiddle ? Mix(0.55f, 0.15f, (d - AuraInner) / (AuraMiddle - AuraInner))
                    : Mix(0.15f, 0f, (d - AuraMiddle) / (1f - AuraMiddle));
                image.SetPixel(x, y, new Color(v, v, v));
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static float Mix(float a, float b, float t) => a + ((b - a) * t);
}
