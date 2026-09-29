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
        instance uniform float belt_speed = 1.0;
        void fragment() {
            float v = fract((UV.y - TIME * speed * belt_speed) * ribs);
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
        uniform vec3 buy_tint : source_color = vec3(1.0, 0.78, 0.25);
        uniform vec3 rim_tint : source_color = vec3(0.25, 0.62, 1.0);
        // One texel per plot: r = owned, g = can be bought right now.
        uniform sampler2D plots : filter_nearest, repeat_disable;
        uniform vec4 map_rect = vec4(0.0, 0.0, 75.0, 75.0);
        uniform vec2 plot_grid = vec2(5.0, 5.0);
        uniform float plot_size = 15.0;
        uniform vec2 hover_plot = vec2(-1.0, -1.0);
        uniform float land_mode = 0.0;
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
            float on_map = step(map_rect.x, p.x) * step(map_rect.y, p.y) * step(p.x, map_rect.z) * step(p.y, map_rect.w);
            vec2 pc = (p - map_rect.xy) / plot_size;
            vec2 pid = clamp(floor(pc), vec2(0.0), plot_grid - 1.0);
            vec4 land = texture(plots, (pid + 0.5) / plot_grid);
            float owned = on_map * land.r;
            float locked = on_map * (1.0 - land.r);
            float buyable = on_map * land.g * (1.0 - land.r);

            // Land you do not own yet is dull and grey; yours is a little brighter.
            float luma = dot(c, vec3(0.30, 0.59, 0.11));
            c = mix(c, vec3(luma) * 0.78, locked * 0.55);
            c = mix(c, c * plot_tint, owned);
            // Buy mode: plots you can buy are gold, the one under the cursor glows.
            c = mix(c, buy_tint * (0.75 + 0.25 * d), buyable * land_mode * 0.42);
            float hovered = on_map * step(abs(pid.x - hover_plot.x), 0.5) * step(abs(pid.y - hover_plot.y), 0.5);
            c = mix(c, c * 1.25 + vec3(0.10, 0.08, 0.02), hovered * land_mode * 0.7);

            // Plot borders: faint always, clear in buy mode.
            vec2 f = fract(pc);
            float edge = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y)) * plot_size;
            float border = (1.0 - smoothstep(0.06, 0.16, edge)) * on_map;
            c = mix(c, vec3(0.94, 0.97, 0.86), border * mix(0.10, 0.55, land_mode));

            // The outermost ring of cells is where depots and export terminals work.
            float to_edge = min(min(p.x - map_rect.x, map_rect.z - p.x), min(p.y - map_rect.y, map_rect.w - p.y));
            float rim = on_map * (1.0 - smoothstep(0.9, 1.0, to_edge));
            c = mix(c, rim_tint, rim * mix(0.22, 0.42, owned));

            vec2 g = abs(fract(p) - 0.5);
            float line = smoothstep(0.475, 0.5, max(g.x, g.y));
            c = mix(c, vec3(0.95, 1.0, 0.9), line * grid_strength * owned * 0.22);
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

    /// <summary>
    /// Highlight drawn over a building (selection, hover, delete, upgrade): a glow on edges
    /// that face away from the camera plus a light fill, so the building keeps its look
    /// instead of turning into a see-through ghost.
    /// </summary>
    public const string Highlight = """
        shader_type spatial;
        render_mode unshaded, blend_mix, depth_draw_never, cull_back, shadows_disabled;
        uniform vec4 tint : source_color = vec4(0.31, 0.76, 1.0, 1.0);
        uniform float fill = 0.08;
        uniform float rim = 0.9;
        void fragment() {
            float f = 1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0);
            ALBEDO = tint.rgb;
            ALPHA = clamp(fill + pow(f, 2.0) * rim, 0.0, 1.0);
        }
        """;

    /// <summary>
    /// The box-select / box-delete area: a light wash with a crisp border (and brighter corners), so it
    /// reads as a marked area rather than a flat plate. <c>size</c> is the box in cells, to keep the
    /// border the same width however big the box is.
    /// </summary>
    public const string AreaBox = """
        shader_type spatial;
        render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled, cull_disabled, shadows_disabled;
        uniform vec4 tint : source_color = vec4(0.2, 0.72, 1.0, 1.0);
        uniform vec2 size = vec2(1.0);
        uniform float fill = 0.06;
        uniform float border = 0.08;
        void fragment() {
            vec2 d = min(UV, 1.0 - UV) * size;          // distance to the nearest edges, in cells
            float edge = 1.0 - smoothstep(border, border + 0.02, min(d.x, d.y));
            float corner = 1.0 - smoothstep(0.35, 0.37, max(d.x, d.y));
            float inner = 1.0 - smoothstep(0.0, 0.5, min(d.x, d.y));
            ALBEDO = mix(tint.rgb, vec3(1.0), edge * corner * 0.35);
            ALPHA = clamp(fill + inner * 0.1 + edge * (0.8 + corner * 0.2), 0.0, 1.0);
        }
        """;

    public static ShaderMaterial AreaBoxMaterial(Color tint)
    {
        var m = new ShaderMaterial { Shader = _areaBox ??= new Shader { Code = AreaBox } };
        m.SetShaderParameter("tint", tint);
        return m;
    }

    private static Shader? _areaBox;

    public static ShaderMaterial HighlightMaterial(Color tint, float fill, float rim)
    {
        var m = new ShaderMaterial { Shader = _highlight ??= new Shader { Code = Highlight } };
        m.SetShaderParameter("tint", tint);
        m.SetShaderParameter("fill", fill);
        m.SetShaderParameter("rim", rim);
        return m;
    }

    private static Shader? _highlight;
    private static ShaderMaterial? _deck;

    /// <summary>Shared belt deck material; each belt sets its own "belt_speed" instance parameter from its level.</summary>
    public static ShaderMaterial Deck => _deck ??= new ShaderMaterial { Shader = new Shader { Code = BeltDeck } };
}
