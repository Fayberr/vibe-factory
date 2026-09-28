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
    public static readonly Color Primary = new("#5aa9f2");
    public static readonly Color Line = new(1, 1, 1, 0.09f);

    private static FontVariation? _bold;

    /// <summary>Emboldened default font for titles and numbers.</summary>
    public static FontVariation Bold => _bold ??= new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = 0.75f };

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

        // Windows: sections run edge to edge, separated by thin lines.
        t.SetTypeVariation("HudWindowPanel", "PanelContainer");
        var window = Box(PanelBg, 12, 0, 0);
        window.BorderColor = new Color(1, 1, 1, 0.08f);
        window.SetBorderWidthAll(1);
        window.ShadowColor = new Color(0, 0, 0, 0.4f);
        window.ShadowSize = 14;
        window.ShadowOffset = new Vector2(0, 4);
        t.SetStylebox("panel", "HudWindowPanel", window);

        // Big filled call to action (Upgrade).
        t.SetTypeVariation("PrimaryButton", "Button");
        t.SetStylebox("normal", "PrimaryButton", Box(Primary, 0, 14, 10));
        t.SetStylebox("hover", "PrimaryButton", Box(Primary.Lightened(0.12f), 0, 14, 10));
        t.SetStylebox("pressed", "PrimaryButton", Box(Primary.Darkened(0.15f), 0, 14, 10));
        t.SetStylebox("hover_pressed", "PrimaryButton", Box(Primary.Darkened(0.15f), 0, 14, 10));
        t.SetStylebox("disabled", "PrimaryButton", Box(new Color(Primary, 0.35f), 0, 14, 10));
        t.SetColor("font_color", "PrimaryButton", Colors.White);
        t.SetColor("font_disabled_color", "PrimaryButton", new Color(1, 1, 1, 0.6f));
        t.SetFont("font", "PrimaryButton", Bold);
        t.SetFontSize("font_size", "PrimaryButton", 17);

        // Text-only secondary action (Delete, Deselect).
        t.SetTypeVariation("FlatButton", "Button");
        t.SetStylebox("normal", "FlatButton", Box(new Color(1, 1, 1, 0.02f), 0, 14, 10));
        t.SetStylebox("hover", "FlatButton", Box(new Color(1, 1, 1, 0.08f), 0, 14, 10));
        t.SetStylebox("pressed", "FlatButton", Box(new Color(1, 1, 1, 0.12f), 0, 14, 10));
        t.SetStylebox("disabled", "FlatButton", Box(new Color(1, 1, 1, 0.02f), 0, 14, 10));
        t.SetColor("font_color", "FlatButton", Primary);
        t.SetColor("font_hover_color", "FlatButton", Primary.Lightened(0.2f));
        t.SetFont("font", "FlatButton", Bold);
        t.SetFontSize("font_size", "FlatButton", 16);

        // Selectable picture tiles (what a machine produces).
        t.SetTypeVariation("Tile", "Button");
        t.SetStylebox("normal", "Tile", Box(new Color(1, 1, 1, 0.025f), 0, 4, 4));
        t.SetStylebox("hover", "Tile", Box(new Color(1, 1, 1, 0.08f), 0, 4, 4));
        var picked = Box(new Color(Primary, 0.12f), 0, 4, 4);
        picked.BorderColor = Primary;
        picked.SetBorderWidthAll(2);
        t.SetStylebox("pressed", "Tile", picked);
        t.SetStylebox("hover_pressed", "Tile", picked);
        t.SetColor("font_pressed_color", "Tile", Primary);
        t.SetColor("font_hover_pressed_color", "Tile", Primary);

        t.SetStylebox("background", "ProgressBar", Box(new Color(1, 1, 1, 0.08f), 4, 0, 0));
        t.SetStylebox("fill", "ProgressBar", Box(Accent, 4, 0, 0));
        t.SetColor("font_color", "ProgressBar", new Color(0, 0, 0, 0));

        var line = new StyleBoxLine { Color = Line, Thickness = 1 };
        t.SetStylebox("separator", "HSeparator", line);
        t.SetStylebox("separator", "VSeparator", new StyleBoxLine { Color = Line, Thickness = 1, Vertical = true });
        t.SetConstant("separation", "HSeparator", 1);
        t.SetConstant("separation", "VSeparator", 1);
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
