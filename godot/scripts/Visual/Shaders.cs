using Godot;

namespace FactorySim.Client;

/// <summary>Shader sources and the shared materials built from them.</summary>
public static class Shaders
{
    /// <summary>Belt surface: ribs that scroll along UV.y (path distance in tiles) at the belt speed.</summary>
    public const string BeltDeck = """
        shader_type spatial;
        render_mode cull_back;
        uniform vec3 base_color : source_color = vec3(0.075, 0.08, 0.09);
        uniform vec3 rib_color : source_color = vec3(0.17, 0.18, 0.2);
        uniform float speed = 2.0;
        uniform float ribs = 8.0;
        uniform float width = 0.57;
        void fragment() {
            float v = fract((UV.y - TIME * speed) * ribs);
            float rib = smoothstep(0.0, 0.1, v) * (1.0 - smoothstep(0.32, 0.42, v));
            float edge = smoothstep(0.0, 0.06, UV.x) * (1.0 - smoothstep(width - 0.06, width, UV.x));
            ALBEDO = mix(base_color, rib_color, rib) * mix(0.55, 1.0, edge);
            ROUGHNESS = 0.6 + 0.25 * rib;
            SPECULAR = 0.25;
        }
        """;

    /// <summary>Grass with large and small noise variation; the plot is tinted and shows a build grid on demand.</summary>
    public const string Ground = """
        shader_type spatial;
        uniform vec3 grass_a : source_color = vec3(0.30, 0.55, 0.22);
        uniform vec3 grass_b : source_color = vec3(0.43, 0.69, 0.30);
        uniform vec3 plot_tint : source_color = vec3(1.06, 1.06, 1.0);
        uniform vec4 plot_rect = vec4(0.0, 0.0, 32.0, 32.0);
        uniform float grid_strength = 0.0;
        uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
        varying vec3 wpos;
        void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
        void fragment() {
            float n = texture(noise_tex, wpos.xz * 0.012).r;
            float d = texture(noise_tex, wpos.xz * 0.19 + vec2(0.37, 0.11)).r;
            vec3 c = mix(grass_a, grass_b, smoothstep(0.3, 0.7, n));
            c *= 0.86 + 0.26 * d;
            vec2 p = wpos.xz;
            float inside = step(plot_rect.x, p.x) * step(plot_rect.y, p.y) * step(p.x, plot_rect.z) * step(p.y, plot_rect.w);
            c = mix(c, c * plot_tint, inside);
            vec2 g = abs(fract(p) - 0.5);
            float line = smoothstep(0.475, 0.5, max(g.x, g.y));
            c = mix(c, vec3(0.95, 1.0, 0.9), line * grid_strength * inside * 0.22);
            ALBEDO = c;
            ROUGHNESS = 0.96;
            SPECULAR = 0.12;
        }
        """;

    /// <summary>Translucent grid plane marking the active build layer above/below ground.</summary>
    public const string LayerGrid = """
        shader_type spatial;
        render_mode unshaded, cull_disabled, blend_mix, depth_draw_never, shadows_disabled;
        uniform vec4 fill : source_color = vec4(0.30, 0.75, 1.0, 0.07);
        uniform vec4 line : source_color = vec4(0.55, 0.88, 1.0, 0.45);
        varying vec3 wpos;
        void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
        void fragment() {
            vec2 g = abs(fract(wpos.xz) - 0.5);
            float l = smoothstep(0.455, 0.5, max(g.x, g.y));
            vec4 c = mix(fill, line, l);
            ALBEDO = c.rgb;
            ALPHA = c.a;
        }
        """;

    private static ShaderMaterial? _deck;

    /// <summary>Shared belt deck material; its speed is updated from the belt-speed stat.</summary>
    public static ShaderMaterial Deck => _deck ??= new ShaderMaterial { Shader = new Shader { Code = BeltDeck } };
}
