using Godot;

namespace FactorySim.Client;

/// <summary>Dark, rounded, slightly translucent HUD theme with one accent colour.</summary>
public static class UiTheme
{
    public static readonly Color Text = new("#e9eef5");
    public static readonly Color Muted = new("#93a0b1");
    public static readonly Color Accent = new("#4fb6ff");
    public static readonly Color Money = new("#6be38f");
    public static readonly Color PanelBg = new(0.07f, 0.085f, 0.11f, 0.9f);
    public static readonly Color Key = new(1, 1, 1, 0.12f);

    public static Theme Create()
    {
        var t = new Theme { DefaultFontSize = 15 };

        var panel = Box(PanelBg, 14, 12, 10);
        panel.BorderColor = new Color(1, 1, 1, 0.07f);
        panel.SetBorderWidthAll(1);
        panel.ShadowColor = new Color(0, 0, 0, 0.35f);
        panel.ShadowSize = 12;
        panel.ShadowOffset = new Vector2(0, 3);
        t.SetStylebox("panel", "PanelContainer", panel);
        t.SetStylebox("panel", "TooltipPanel", Box(new Color(0.05f, 0.06f, 0.08f, 0.97f), 8, 10, 7));
        t.SetColor("font_color", "TooltipLabel", Text);
        t.SetColor("font_color", "Label", Text);

        var normal = Box(new Color(1, 1, 1, 0.045f), 9, 10, 6);
        var hover = Box(new Color(1, 1, 1, 0.11f), 9, 10, 6);
        var pressed = Box(new Color(Accent, 0.28f), 9, 10, 6);
        pressed.BorderColor = Accent;
        pressed.SetBorderWidthAll(1);
        var disabled = Box(new Color(1, 1, 1, 0.02f), 9, 10, 6);
        foreach (var type in new[] { "Button", "CheckButton" })
        {
            t.SetStylebox("normal", type, normal);
            t.SetStylebox("hover", type, hover);
            t.SetStylebox("pressed", type, pressed);
            t.SetStylebox("hover_pressed", type, pressed);
            t.SetStylebox("disabled", type, disabled);
            t.SetStylebox("focus", type, new StyleBoxEmpty());
            t.SetColor("font_color", type, Text);
            t.SetColor("font_hover_color", type, Colors.White);
            t.SetColor("font_pressed_color", type, Colors.White);
            t.SetColor("font_hover_pressed_color", type, Colors.White);
            t.SetColor("font_disabled_color", type, new Color(Text, 0.35f));
            t.SetColor("icon_normal_color", type, Text);
            t.SetColor("icon_hover_color", type, Colors.White);
            t.SetColor("icon_pressed_color", type, Colors.White);
        }

        t.SetStylebox("background", "ProgressBar", Box(new Color(1, 1, 1, 0.08f), 4, 0, 0));
        t.SetStylebox("fill", "ProgressBar", Box(Accent, 4, 0, 0));
        t.SetColor("font_color", "ProgressBar", new Color(0, 0, 0, 0));

        var line = new StyleBoxLine { Color = new Color(1, 1, 1, 0.08f), Thickness = 1 };
        t.SetStylebox("separator", "HSeparator", line);
        t.SetStylebox("separator", "VSeparator", new StyleBoxLine { Color = new Color(1, 1, 1, 0.1f), Thickness = 1, Vertical = true });
        t.SetConstant("separation", "HBoxContainer", 6);
        t.SetConstant("separation", "VBoxContainer", 6);
        t.SetConstant("h_separation", "GridContainer", 8);
        t.SetConstant("v_separation", "GridContainer", 8);
        return t;
    }

    public static StyleBoxFlat Box(Color bg, int radius, int padX, int padY)
    {
        var s = new StyleBoxFlat { BgColor = bg };
        s.SetCornerRadiusAll(radius);
        s.ContentMarginLeft = s.ContentMarginRight = padX;
        s.ContentMarginTop = s.ContentMarginBottom = padY;
        s.AntiAliasing = true;
        return s;
    }
}
