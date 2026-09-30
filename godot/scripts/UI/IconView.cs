using Godot;

namespace FactorySim.Client;

public enum Icon
{
    Select,
    Build,
    Delete,
    Move,
    Copy,
    Undo,
    Redo,
    LayerUp,
    LayerDown,
    Eye,
    Upgrades,
    Stats,
    Game,
    Help,
    Close,
    Rotate,
    Progress,
    Lock,
    Star,
    Coin,
    Gauge,
    Clock,
    Search,
    Auto,
    Orders,
    Research,
    Chain,
    Graph,
    Bell,
}

/// <summary>Crisp vector icons drawn in code on a 24×24 grid (no image assets needed).</summary>
public partial class IconView : Control
{
    private Icon _icon;
    private Color _color = UiTheme.Text;

    public Icon Icon
    {
        get => _icon;
        set { _icon = value; QueueRedraw(); }
    }

    public Color Color
    {
        get => _color;
        set { _color = value; QueueRedraw(); }
    }

    public IconView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(22, 22);
    }

    public override void _Draw()
    {
        float s = Mathf.Min(Size.X, Size.Y) / 24f;
        var o = (Size - new Vector2(24, 24) * s) / 2;
        Vector2 P(float x, float y) => o + new Vector2(x, y) * s;
        void L(float x0, float y0, float x1, float y1, float w = 2f) => DrawLine(P(x0, y0), P(x1, y1), _color, w * s, true);
        void Poly(params float[] xy)
        {
            var pts = new Vector2[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = P(xy[2 * i], xy[2 * i + 1]);
            DrawColoredPolygon(pts, _color);
        }
        void Outline(float w, params float[] xy)
        {
            var pts = new Vector2[xy.Length / 2 + 1];
            for (int i = 0; i < xy.Length / 2; i++) pts[i] = P(xy[2 * i], xy[2 * i + 1]);
            pts[^1] = pts[0];
            DrawPolyline(pts, _color, w * s, true);
        }
        void Arc(float cx, float cy, float r, float a0, float a1, float w = 2f) => DrawArc(P(cx, cy), r * s, a0, a1, 24, _color, w * s, true);

        switch (_icon)
        {
            case Icon.Select:
                Poly(6, 3, 6, 19.5f, 10.2f, 15.6f, 13.2f, 21.5f, 15.8f, 20.3f, 12.9f, 14.5f, 18.5f, 14.3f);
                break;
            case Icon.Build:
                L(5, 19.5f, 13, 11.5f, 2.8f);
                Poly(12.4f, 4.6f, 19.4f, 11.6f, 16.6f, 14.4f, 9.6f, 7.4f);
                break;
            case Icon.Delete:
                L(4.5f, 7, 19.5f, 7);
                L(10, 4.5f, 14, 4.5f);
                Outline(2, 6.5f, 8, 7.6f, 20, 16.4f, 20, 17.5f, 8);
                L(10.3f, 10.5f, 10.5f, 17.5f, 1.6f);
                L(13.7f, 10.5f, 13.5f, 17.5f, 1.6f);
                break;
            case Icon.Move:
                L(12, 4, 12, 20);
                L(4, 12, 20, 12);
                Poly(12, 2, 15, 6, 9, 6);
                Poly(12, 22, 9, 18, 15, 18);
                Poly(2, 12, 6, 9, 6, 15);
                Poly(22, 12, 18, 15, 18, 9);
                break;
            case Icon.Copy:
                Outline(2, 9, 9, 20, 9, 20, 20, 9, 20);
                Outline(2, 4, 4, 15, 4, 15, 6.5f, 6.5f, 6.5f, 6.5f, 15, 4, 15);
                break;
            case Icon.Undo:
                Arc(13, 14, 6, Mathf.Pi, Mathf.Pi * 2.5f);
                Poly(3.6f, 12, 10.4f, 12, 7, 16.8f);
                break;
            case Icon.Redo:
                Arc(11, 14, 6, -Mathf.Pi * 0.5f, Mathf.Pi);
                Poly(13.6f, 12, 20.4f, 12, 17, 16.8f);
                break;
            case Icon.LayerUp:
            case Icon.LayerDown:
                Outline(1.8f, 12, 11, 20, 15, 12, 19, 4, 15);
                Outline(1.4f, 4, 11.5f, 12, 7.5f, 20, 11.5f);
                if (_icon == Icon.LayerUp) Poly(12, 1.5f, 16, 6, 8, 6);
                else Poly(12, 6.5f, 8, 2, 16, 2);
                break;
            case Icon.Eye:
                Arc(12, 20, 11, -Mathf.Pi * 0.82f, -Mathf.Pi * 0.18f);
                Arc(12, 4, 11, Mathf.Pi * 0.18f, Mathf.Pi * 0.82f);
                DrawCircle(P(12, 12), 3.2f * s, _color);
                break;
            case Icon.Upgrades:
                Arc(12, 12, 9, 0, Mathf.Tau);
                Poly(12, 6, 16.5f, 11, 7.5f, 11);
                L(12, 10.5f, 12, 17.5f, 2.6f);
                break;
            case Icon.Stats:
                DrawRect(new Rect2(P(4, 13), new Vector2(4, 7) * s), _color);
                DrawRect(new Rect2(P(10, 8), new Vector2(4, 12) * s), _color);
                DrawRect(new Rect2(P(16, 4), new Vector2(4, 16) * s), _color);
                break;
            case Icon.Game:
                Arc(12, 12, 5.5f, 0, Mathf.Tau, 2.4f);
                for (int i = 0; i < 8; i++)
                {
                    float a = Mathf.Tau * i / 8;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    L(12 + d.X * 7, 12 + d.Y * 7, 12 + d.X * 10, 12 + d.Y * 10, 3f);
                }
                break;
            case Icon.Help:
                Arc(12, 12, 9.5f, 0, Mathf.Tau, 1.8f);
                Arc(12, 9.5f, 3.2f, -Mathf.Pi, Mathf.Pi * 0.35f, 2.2f);
                L(13.5f, 12.4f, 12, 14.2f, 2.2f);
                DrawCircle(P(12, 17.5f), 1.4f * s, _color);
                break;
            case Icon.Close:
                L(6, 6, 18, 18, 2.4f);
                L(18, 6, 6, 18, 2.4f);
                break;
            case Icon.Rotate:
                Arc(12, 12, 7, -Mathf.Pi * 0.35f, Mathf.Pi * 1.3f);
                Poly(17.5f, 3.5f, 19.5f, 9.5f, 13.2f, 8.2f);
                break;
            case Icon.Progress:
                Poly(3, 20.5f, 3, 16, 8, 16, 8, 11.5f, 13, 11.5f, 13, 7, 18, 7, 18, 20.5f);
                L(18, 7, 18, 1.8f, 1.6f);
                Poly(18.6f, 1.8f, 22.5f, 3.4f, 18.6f, 5);
                break;
            case Icon.Star:
            {
                var pts = new float[20];
                for (int i = 0; i < 10; i++)
                {
                    float a = -Mathf.Pi / 2 + i * Mathf.Pi / 5;
                    float r = i % 2 == 0 ? 10f : 4.3f;
                    pts[2 * i] = 12 + Mathf.Cos(a) * r;
                    pts[2 * i + 1] = 12.6f + Mathf.Sin(a) * r;
                }
                Poly(pts);
                break;
            }
            case Icon.Coin:
                Arc(12, 12, 9.5f, 0, Mathf.Tau, 1.8f);
                L(12, 4.5f, 12, 19.5f, 1.6f);
                Arc(12, 9.3f, 3.1f, Mathf.Pi * 0.45f, Mathf.Pi * 1.95f, 2f);
                Arc(12, 14.7f, 3.1f, -Mathf.Pi * 0.55f, Mathf.Pi * 0.95f, 2f);
                break;
            case Icon.Gauge:
                Arc(12, 14, 9, Mathf.Pi * 0.8f, Mathf.Pi * 2.2f, 2.4f);
                for (int i = 0; i <= 4; i++)
                {
                    float a = Mathf.Pi * (0.8f + 0.35f * i);
                    L(12 + Mathf.Cos(a) * 5.5f, 14 + Mathf.Sin(a) * 5.5f, 12 + Mathf.Cos(a) * 7, 14 + Mathf.Sin(a) * 7, 1.4f);
                }
                L(12, 14, 16.5f, 8.5f, 2.2f);
                DrawCircle(P(12, 14), 1.9f * s, _color);
                break;
            case Icon.Clock:
                Arc(12, 12, 9.5f, 0, Mathf.Tau, 2f);
                L(12, 12, 12, 6.5f, 2f);
                L(12, 12, 16, 14, 2f);
                break;
            case Icon.Search:
                Arc(10, 10, 6.5f, 0, Mathf.Tau, 2.6f);
                L(14.8f, 14.8f, 20.5f, 20.5f, 3.2f);
                break;
            case Icon.Orders:
                Outline(2, 5.5f, 4.5f, 18.5f, 4.5f, 18.5f, 21, 5.5f, 21);
                DrawRect(new Rect2(P(9, 2.5f), new Vector2(6, 4) * s), _color);
                L(8.5f, 10, 15.5f, 10, 1.8f);
                L(8.5f, 13.5f, 15.5f, 13.5f, 1.8f);
                L(8.5f, 17, 13, 17, 1.8f);
                break;
            case Icon.Auto:
                Arc(12, 12, 7.5f, -Mathf.Pi * 0.2f, Mathf.Pi * 0.75f, 2.2f);
                Arc(12, 12, 7.5f, Mathf.Pi * 0.8f, Mathf.Pi * 1.75f, 2.2f);
                Poly(19.5f, 6.5f, 19.8f, 12.2f, 14.6f, 9.9f);
                Poly(4.5f, 17.5f, 4.2f, 11.8f, 9.4f, 14.1f);
                break;
            case Icon.Chain:
                // A product over its two ingredients, joined like a family tree.
                Outline(2, 9, 3, 15, 3, 15, 8, 9, 8);
                Outline(2, 3, 16, 9, 16, 9, 21, 3, 21);
                Outline(2, 15, 16, 21, 16, 21, 21, 15, 21);
                L(12, 8, 12, 12, 1.8f);
                L(6, 12, 18, 12, 1.8f);
                L(6, 12, 6, 16, 1.8f);
                L(18, 12, 18, 16, 1.8f);
                break;
            case Icon.Bell:
                // A bell: a dome over a flared rim, a clapper under it.
                Poly(12, 3.5f, 15.5f, 5.5f, 17, 10, 17.5f, 15, 20, 17.5f, 4, 17.5f, 6.5f, 15, 7, 10, 8.5f, 5.5f);
                L(10, 20.5f, 14, 20.5f, 2.2f);
                break;
            case Icon.Graph:
                // Axes with a rising line across them.
                L(4, 3, 4, 20, 1.8f);
                L(4, 20, 21, 20, 1.8f);
                L(6, 16, 10.5f, 11, 1.8f);
                L(10.5f, 11, 14, 14, 1.8f);
                L(14, 14, 20, 6, 1.8f);
                break;
            case Icon.Research:
                // A flask with liquid in it.
                L(8.5f, 3, 15.5f, 3, 1.8f);
                Outline(2, 10, 3.5f, 10, 9.5f, 4.5f, 19.5f, 5.8f, 21, 18.2f, 21, 19.5f, 19.5f, 14, 9.5f, 14, 3.5f);
                Poly(7.4f, 15.2f, 16.6f, 15.2f, 18.6f, 19.2f, 17.6f, 20.2f, 6.4f, 20.2f, 5.4f, 19.2f);
                break;
            case Icon.Lock:
                Arc(12, 10, 4.2f, Mathf.Pi, Mathf.Tau, 2.2f);
                L(7.8f, 10, 7.8f, 12, 2.2f);
                L(16.2f, 10, 16.2f, 12, 2.2f);
                DrawRect(new Rect2(P(5.5f, 11.5f), new Vector2(13, 9.5f) * s), _color);
                break;
        }
    }
}
