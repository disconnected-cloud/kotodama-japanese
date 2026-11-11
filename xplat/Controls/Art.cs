using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Matsubakira.Services;
using Path = Avalonia.Controls.Shapes.Path;

namespace Matsubakira.Controls;

public enum Mood { Happy, Joy, Wink, Think, Sad, Wow }

/// <summary>
/// Собственная векторная графика приложения: маскоты (Котоха и дух-лисичка Тама) и набор цветных иконок.
/// Всё рисуется путями — чётко при любом масштабе и ничего не весит.
/// </summary>
public static class Art
{
    static IBrush C(string hex) => Solid.TryGetValue(hex, out var b) ? b : Solid[hex] = new SolidColorBrush(Color.Parse(hex)).ToImmutable();
    static IBrush Grad(string a, string b, double angle = 90)
    {
        var (s, e) = angle == 90 ? (new RelativePoint(0.5, 0, RelativeUnit.Relative), new RelativePoint(0.5, 1, RelativeUnit.Relative))
                                 : (new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 1, RelativeUnit.Relative));
        return new LinearGradientBrush { StartPoint = s, EndPoint = e, GradientStops = { new GradientStop(Color.Parse(a), 0), new GradientStop(Color.Parse(b), 1) } };
    }

    // Разобранные пути кэшируются: маскоты и иконки рисуются на многих экранах
    static readonly Dictionary<string, Geometry> Geo = new();
    static Geometry G(string data) => Geo.TryGetValue(data, out var g) ? g : Geo[data] = Geometry.Parse(data);
    static readonly Dictionary<string, IBrush> Solid = new();

    static Path P(string data, IBrush? fill, IBrush? stroke = null, double th = 0, double opacity = 1) => new()
    {
        Data = G(data), Fill = fill, Stroke = stroke, StrokeThickness = th, Opacity = opacity,
        StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
    };

    static Ellipse E(double cx, double cy, double rx, double ry, IBrush fill, double opacity = 1)
    {
        var e = new Ellipse { Width = rx * 2, Height = ry * 2, Fill = fill, Opacity = opacity };
        Canvas.SetLeft(e, cx - rx); Canvas.SetTop(e, cy - ry);
        return e;
    }

    static Control Box(Canvas c, double w, double h, double size) =>
        new Viewbox { Width = size * w / h, Height = size, Stretch = Stretch.Uniform, Child = c.Also(x => { x.Width = w; x.Height = h; }) };

    // ─────────── Котоха ───────────
    const string Hair = "#2E2547", HairLight = "#4A3D72", Skin = "#FFE6D8", SkinShade = "#F6CDBB", Line = "#22183A";

    /// <summary>Котоха — девушка-проводник в мир японского. size — высота в пикселях.</summary>
    public static Control Kotoha(double size, Mood mood = Mood.Happy)
    {
        var c = new Canvas();
        var hair = Grad(HairLight, Hair);
        // Волосы сзади
        c.Children.Add(P("M100 24 C152 24 174 60 171 108 C169 148 176 176 188 198 C162 204 146 190 141 170 L59 170 C54 190 38 204 12 198 C24 176 31 148 29 108 C26 60 48 24 100 24 Z", hair));
        // Шея и тело в матроске
        c.Children.Add(P("M87 148 L87 180 L113 180 L113 148 Z", C(SkinShade)));
        c.Children.Add(P("M40 240 C42 204 64 182 100 178 C136 182 158 204 160 240 Z", Grad("#FFFFFF", "#E9E6F4")));
        c.Children.Add(P("M58 196 C72 184 86 179 100 179 C114 179 128 184 142 196 L100 232 Z", Grad("#4A5A96", "#2F3C70")));
        c.Children.Add(P("M66 197 L100 226 L134 197", null, C("#FFFFFF"), 2, 0.85));
        c.Children.Add(P("M85 182 L100 210 L115 182 C110 180 105 179.5 100 179.5 C95 179.5 90 180 85 182 Z", C(SkinShade)));
        c.Children.Add(P("M100 214 L82 204 L85 225 Z M100 214 L118 204 L115 225 Z", Grad("#FF7FA4", "#E2457A")));
        c.Children.Add(E(100, 214, 5, 5, C("#D63A6E")));
        // Лицо
        c.Children.Add(P("M56 88 C56 128 74 158 100 166 C126 158 144 128 144 88 C144 60 124 46 100 46 C76 46 56 60 56 88 Z", Grad("#FFEDE3", Skin)));
        // Румянец
        c.Children.Add(E(70, 139, 9, 4.5, C("#FF8FAB"), mood == Mood.Sad ? 0.25 : 0.5));
        c.Children.Add(E(130, 139, 9, 4.5, C("#FF8FAB"), mood == Mood.Sad ? 0.25 : 0.5));
        // Глаза
        switch (mood)
        {
            case Mood.Joy:
                c.Children.Add(P("M67 122 C72 112 88 112 93 122", null, C(Line), 4));
                c.Children.Add(P("M133 122 C128 112 112 112 107 122", null, C(Line), 4));
                break;
            case Mood.Wink:
                Eye(c, 80, false, mood);
                c.Children.Add(P("M107 120 C112 126 128 126 133 120", null, C(Line), 4));
                break;
            default:
                Eye(c, 80, false, mood);
                Eye(c, 120, true, mood);
                break;
        }
        // Брови (видны сквозь чёлку)
        if (mood == Mood.Sad)
        {
            c.Children.Add(P("M69 104 C76 102 84 99 90 95", null, C(Line), 2.4, 0.75));
            c.Children.Add(P("M131 104 C124 102 116 99 110 95", null, C(Line), 2.4, 0.75));
        }
        // Нос и рот
        c.Children.Add(P("M100.5 131 L98.8 134.5", null, C("#D99A88"), 1.6));
        c.Children.Add(mood switch
        {
            Mood.Joy => P("M90 143 C92 156 108 156 110 143 Z", Grad("#C24A63", "#9C2F4C"), C(Line), 1.5),
            Mood.Happy => P("M92 144 C94 152 106 152 108 144 Z", Grad("#C24A63", "#9C2F4C"), C(Line), 1.4),
            Mood.Wink => P("M92 145 C96 151 104 151 108 145", null, C("#9C2F4C"), 2.6),
            Mood.Think => P("M95 147 C99 145 103 146 106 148", null, C("#9C2F4C"), 2.4),
            Mood.Sad => P("M93 149 C97 144 103 144 107 149", null, C("#9C2F4C"), 2.6),
            _ => E(100, 148, 4.5, 5.5, C("#9C2F4C")),
        });
        if (mood is Mood.Joy or Mood.Happy) c.Children.Add(P("M95 150 C98 147 102 147 105 150 C102 152 98 152 95 150 Z", C("#FF8FA0"), null, 0, mood == Mood.Joy ? 1 : 0.8));
        // Чёлка и пряди
        c.Children.Add(P("M50 102 C44 56 70 32 100 32 C130 32 156 56 150 102 C146 90 141 80 134 73 C134 85 130 93 124 99 C122 87 118 77 112 71 C110 83 104 91 96 95 C98 85 96 77 92 71 C86 81 78 89 67 95 C69 85 70 79 70 73 C62 81 56 90 50 102 Z", hair));
        c.Children.Add(P("M57 92 C49 122 51 152 60 176 C65 162 67 140 67 112 Z", hair));
        c.Children.Add(P("M143 92 C151 122 149 152 140 176 C135 162 133 140 133 112 Z", hair));
        c.Children.Add(P("M72 50 C86 42 114 42 128 50", null, C("#FFFFFF"), 4, 0.16));
        c.Children.Add(P("M60 120 C58 136 60 150 63 160", null, C("#FFFFFF"), 2, 0.1));
        // Заколка-сакура
        c.Children.Add(Sakura(139, 70, 1.05));
        if (mood == Mood.Think) c.Children.Add(P("M164 70 C164 62 176 62 176 70 C176 75 170 76 170 81 M170 87 L170 88", null, Grad("#B79CFF", "#7C5CE0"), 3.2));
        if (mood == Mood.Wow) c.Children.Add(P("M166 58 L166 76 M166 84 L166 85", null, Grad("#FFC861", "#F29A2E"), 4));
        if (mood == Mood.Joy) { c.Children.Add(Spark(28, 70, 1)); c.Children.Add(Spark(176, 120, 0.7)); }
        return Box(c, 200, 240, size);
    }

    static void Eye(Canvas c, double cx, bool right, Mood mood)
    {
        double dx = mood == Mood.Think ? 3 : 0, dy = mood == Mood.Think ? -3 : 0;
        double ry = mood == Mood.Wow ? 14 : 13;
        c.Children.Add(E(cx, 121, 11, ry + 1, C("#FFFFFF")));
        c.Children.Add(E(cx + dx, 122 + dy, 9.5, ry - 1, Grad("#3A2A6E", "#D0609E")));
        c.Children.Add(E(cx + dx, 123 + dy, 4.8, 6.5, C("#1A1030")));
        c.Children.Add(E(cx + dx - 3.5, 116 + dy, 3.4, 3.8, C("#FFFFFF")));
        c.Children.Add(E(cx + dx + 4, 128 + dy, 1.6, 1.6, C("#FFFFFF"), 0.85));
        // Ресницы
        var lash = right ? $"M{cx + 14} 112 C{cx + 8} 102 {cx - 10} 102 {cx - 15} 110" : $"M{cx - 14} 112 C{cx - 8} 102 {cx + 10} 102 {cx + 15} 110";
        c.Children.Add(P(lash, null, C(Line), 3.6));
        c.Children.Add(P(right ? $"M{cx + 14} 112 L{cx + 18} 108.5" : $"M{cx - 14} 112 L{cx - 18} 108.5", null, C(Line), 2.6));
        if (mood == Mood.Sad) c.Children.Add(E(cx + (right ? -6 : 6), 136, 2.2, 3.2, C("#8FD3FF"), 0.9));
    }

    static Control Sakura(double x, double y, double scale)
    {
        var c = new Canvas();
        for (int i = 0; i < 5; i++)
        {
            var petal = P("M0 -2 C-5 -6 -5 -13 -1.5 -14.5 L0 -12.5 L1.5 -14.5 C5 -13 5 -6 0 -2 Z", Grad("#FFE0EA", "#FF9DBA"), C("#F27CA0"), 0.8);
            petal.RenderTransform = new RotateTransform(i * 72);
            petal.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
            c.Children.Add(petal);
        }
        c.Children.Add(E(0, 0, 2.6, 2.6, C("#F0507F")));
        c.RenderTransform = new ScaleTransform(scale, scale);
        c.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
        Canvas.SetLeft(c, x); Canvas.SetTop(c, y);
        return c;
    }

    static Control Spark(double x, double y, double s)
    {
        var p = P("M0 -10 C1 -3 3 -1 10 0 C3 1 1 3 0 10 C-1 3 -3 1 -10 0 C-3 -1 -1 -3 0 -10 Z", Grad("#FFE38A", "#FFB547"));
        p.RenderTransform = new ScaleTransform(s, s);
        p.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
        Canvas.SetLeft(p, x); Canvas.SetTop(p, y);
        return p;
    }

    // ─────────── Тама — дух-лисичка ───────────
    public static Control Tama(double size, Mood mood = Mood.Happy)
    {
        var c = new Canvas();
        c.Children.Add(P("M88 92 C112 90 118 62 106 46 C104 64 98 74 84 80 Z", Grad("#FFFFFF", "#FFD3E0"), C("#EBC9D5"), 1.5));
        c.Children.Add(P("M104 52 C108 58 109 64 107 70", null, C("#FF9DBA"), 3, 0.8));
        c.Children.Add(P("M28 46 L33 12 L54 32 Z M92 46 L87 12 L66 32 Z", C("#FFFDFB"), C("#EBDCD3"), 1.5));
        c.Children.Add(P("M34 38 L36 20 L47 31 Z M86 38 L84 20 L73 31 Z", C("#FFB3C8")));
        c.Children.Add(P("M18 72 C18 42 38 28 60 28 C82 28 102 42 102 72 C102 98 84 110 60 110 C36 110 18 98 18 72 Z", Grad("#FFFFFF", "#F6EEF2"), C("#EBDCD3"), 1.5));
        c.Children.Add(P("M60 38 C55 45 57 50 60 53 C63 50 65 45 60 38 Z", Grad("#FF8FB0", "#E2457A")));
        if (mood is Mood.Joy or Mood.Wink)
        {
            c.Children.Add(P("M39 68 C42 61 50 61 53 68", null, C(Line), 3.2));
            c.Children.Add(P("M67 68 C70 61 78 61 81 68", null, C(Line), 3.2));
        }
        else
        {
            c.Children.Add(E(46, 67, 5, mood == Mood.Wow ? 7.5 : 6.5, C(Line)));
            c.Children.Add(E(74, 67, 5, mood == Mood.Wow ? 7.5 : 6.5, C(Line)));
            c.Children.Add(E(44.3, 64, 1.9, 2.1, C("#FFFFFF")));
            c.Children.Add(E(72.3, 64, 1.9, 2.1, C("#FFFFFF")));
        }
        c.Children.Add(E(36, 80, 6, 3.4, C("#FF8FAB"), 0.45));
        c.Children.Add(E(84, 80, 6, 3.4, C("#FF8FAB"), 0.45));
        c.Children.Add(mood == Mood.Sad
            ? P("M54 82 C57 78 63 78 66 82", null, C(Line), 2.2)
            : mood == Mood.Wow ? E(60, 81, 3, 3.6, C("#9C2F4C"))
            : P("M53 77 C56 82 60 80 60 77 C60 80 64 82 67 77", null, C(Line), 2.2));
        return Box(c, 120, 120, size);
    }

    /// <summary>Маскот с репликой в облачке — для пустых экранов, итогов и подсказок.</summary>
    public static Control Say(string text, Mood mood = Mood.Happy, double size = 120, bool tama = false)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("auto,*") };
        g.Children.Add(tama ? Tama(size * 0.8, mood) : Kotoha(size, mood));
        var bubble = new Border
        {
            Background = U.B("Surface2"), BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16, 16, 16, 4), Padding = new Thickness(16, 12), Margin = new Thickness(12, 0, 0, size * 0.25),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
            Child = U.T(text, "Body", 14).Also(t => t.TextWrapping = TextWrapping.Wrap),
        };
        Grid.SetColumn(bubble, 1);
        g.Children.Add(bubble);
        return g;
    }

    // ─────────── Иконки ───────────
    // Каждая иконка — цветная плитка с градиентом и белым линейным рисунком 24×24 (или знаком).
    sealed record IconDef(string A, string B, string? Path = null, string? Glyph = null, string? Fill = null);

    static readonly Dictionary<string, IconDef> Icons = new()
    {
        ["home"] = new("#FF8FB1", "#E8457C", "M4 11 L12 4 L20 11 M6.5 9.5 V19.5 H17.5 V9.5 M10 19.5 V14.5 H14 V19.5"),
        ["path"] = new("#B28BFF", "#7148E0", "M5 19.5 C9.5 19.5 9 13 13 13 C17 13 17 8.5 18 8 M18 9.5 V2.8 L22 4.4 L18 6", Fill: "M3.4 19.5 A1.6 1.6 0 1 0 6.6 19.5 A1.6 1.6 0 1 0 3.4 19.5 Z"),
        ["kana"] = new("#FF9F80", "#EC5B3C", Glyph: "あ"),
        ["words"] = new("#FFD066", "#F29A1F", "M8 4.5 H18.5 A2 2 0 0 1 20.5 6.5 V15 M5.5 8 H14.5 A2 2 0 0 1 16.5 10 V18 A2 2 0 0 1 14.5 20 H5.5 A2 2 0 0 1 3.5 18 V10 A2 2 0 0 1 5.5 8 Z M7 13 H13 M7 16.2 H11"),
        ["kanji"] = new("#FF7A7A", "#D63447", Glyph: "漢"),
        ["grammar"] = new("#5EE0C9", "#16A79A", "M4 7 H13 M4 12 H20 M4 17 H10.5", Fill: "M18 3 C18.4 5 19 5.6 21 6 C19 6.4 18.4 7 18 9 C17.6 7 17 6.4 15 6 C17 5.6 17.6 5 18 3 Z"),
        ["speech"] = new("#FF8AC8", "#D63F9B", "M9 5 A3 3 0 0 1 15 5 V11 A3 3 0 0 1 9 11 Z M5.5 10.5 A6.5 6.5 0 0 0 18.5 10.5 M12 17 V20.5 M8.5 20.5 H15.5"),
        ["dialogs"] = new("#7CC8FF", "#2F86E6", "M4.5 4 H13.5 A2 2 0 0 1 15.5 6 V11 A2 2 0 0 1 13.5 13 H8.5 L5.5 15.8 V13 H4.5 A2 2 0 0 1 2.5 11 V6 A2 2 0 0 1 4.5 4 Z M18.5 9 H19.5 A2 2 0 0 1 21.5 11 V16 A2 2 0 0 1 19.5 18 H18.5 V20.6 L15.5 18 H11.5 A2 2 0 0 1 9.5 16 V15.5"),
        ["chat"] = new("#9A9CFF", "#5550E0", "M6 8.5 H18 A3 3 0 0 1 21 11.5 V17 A3 3 0 0 1 18 20 H6 A3 3 0 0 1 3 17 V11.5 A3 3 0 0 1 6 8.5 Z M12 8.5 V5.2 M1.5 13 V15.5 M22.5 13 V15.5", Fill: "M10.6 3.8 A1.4 1.4 0 1 0 13.4 3.8 A1.4 1.4 0 1 0 10.6 3.8 Z M7.4 14 A1.6 1.6 0 1 0 10.6 14 A1.6 1.6 0 1 0 7.4 14 Z M13.4 14 A1.6 1.6 0 1 0 16.6 14 A1.6 1.6 0 1 0 13.4 14 Z"),
        ["reader"] = new("#6EE3A0", "#1FA868", "M12 6.5 C9.5 5 6 4.8 3 5.5 V19 C6 18.3 9.5 18.5 12 20 C14.5 18.5 18 18.3 21 19 V5.5 C18 4.8 14.5 5 12 6.5 Z M12 6.5 V20"),
        ["books"] = new("#6EE3A0", "#1FA868", "M4 4.5 H8 V19.5 H4 Z M9.5 4.5 H13.5 V19.5 H9.5 Z M15.2 5.6 L19 4.6 L22.6 18.4 L18.8 19.4 Z M4 8 H8 M9.5 16 H13.5"),
        ["dict"] = new("#8FA8FF", "#4762D9", "M10.5 4.5 A6 6 0 1 0 10.5 16.5 A6 6 0 1 0 10.5 4.5 Z M15 15 L20.5 20.5 M8 10.5 H13 M10.5 8 V13"),
        ["review"] = new("#FFB15C", "#F0731E", "M19.5 12 A7.5 7.5 0 0 1 6 16.5 M4.5 12 A7.5 7.5 0 0 1 18 7.5 M18.3 3.5 V7.6 H14.2 M5.7 20.5 V16.4 H9.8"),
        ["trainers"] = new("#B5E85A", "#62B11F", "M12 4 A8 8 0 1 0 12 20 A8 8 0 1 0 12 4 Z M12 8.5 A3.5 3.5 0 1 0 12 15.5 A3.5 3.5 0 1 0 12 8.5 Z M12 12 L20 4 M17 3.6 H20.4 V7"),
        ["settings"] = new("#B3BCD1", "#6C7690", "M4 7.5 H20 M4 16.5 H20", Fill: "M6.5 7.5 A2.6 2.6 0 1 0 11.7 7.5 A2.6 2.6 0 1 0 6.5 7.5 Z M12.3 16.5 A2.6 2.6 0 1 0 17.5 16.5 A2.6 2.6 0 1 0 12.3 16.5 Z"),
        ["more"] = new("#B3BCD1", "#6C7690", Fill: "M3.5 12 A2 2 0 1 0 7.5 12 A2 2 0 1 0 3.5 12 Z M10 12 A2 2 0 1 0 14 12 A2 2 0 1 0 10 12 Z M16.5 12 A2 2 0 1 0 20.5 12 A2 2 0 1 0 16.5 12 Z"),
        ["phrases"] = new("#FFC56B", "#EE7F2A", "M4 5 H20 A1.5 1.5 0 0 1 21.5 6.5 V15 A1.5 1.5 0 0 1 20 16.5 H11 L7 20 V16.5 H4 A1.5 1.5 0 0 1 2.5 15 V6.5 A1.5 1.5 0 0 1 4 5 Z M7 9 H17 M7 12.5 H13"),
        ["voice"] = new("#FF8FB1", "#C0397A", "M4 9.5 H7.5 L12 5.5 V18.5 L7.5 14.5 H4 Z M15.5 9 A4 4 0 0 1 15.5 15 M18 6.5 A7.5 7.5 0 0 1 18 17.5"),
        ["mic"] = new("#FF8AC8", "#D63F9B", "M9 5 A3 3 0 0 1 15 5 V11 A3 3 0 0 1 9 11 Z M5.5 10.5 A6.5 6.5 0 0 0 18.5 10.5 M12 17 V20.5"),
        ["palette"] = new("#C59BFF", "#8452E8", "M12 3.5 A8.5 8.5 0 0 0 12 20.5 C13.5 20.5 14 19.5 13.4 18.4 C12.7 17.2 13.5 15.5 15 15.5 H17 A3.5 3.5 0 0 0 20.5 12 A8.5 8.5 0 0 0 12 3.5 Z", Fill: "M7 11 A1.4 1.4 0 1 0 9.8 11 A1.4 1.4 0 1 0 7 11 Z M9.8 7.4 A1.4 1.4 0 1 0 12.6 7.4 A1.4 1.4 0 1 0 9.8 7.4 Z M14.2 8.2 A1.4 1.4 0 1 0 17 8.2 A1.4 1.4 0 1 0 14.2 8.2 Z"),
        ["bell"] = new("#FFD066", "#F29A1F", "M6 16.5 V11 A6 6 0 0 1 18 11 V16.5 L19.5 18 H4.5 Z M10 20.5 H14"),
        ["target"] = new("#FF8F8F", "#E04B5A", "M12 4 A8 8 0 1 0 12 20 A8 8 0 1 0 12 4 Z M12 8.5 A3.5 3.5 0 1 0 12 15.5 A3.5 3.5 0 1 0 12 8.5 Z", Fill: "M10.8 12 A1.2 1.2 0 1 0 13.2 12 A1.2 1.2 0 1 0 10.8 12 Z"),
        ["shield"] = new("#7CC8FF", "#2F86E6", "M12 3 L19.5 6 V11.5 C19.5 16 16.5 19.5 12 21 C7.5 19.5 4.5 16 4.5 11.5 V6 Z M8.5 12 L11 14.5 L15.5 9.5"),
        ["download"] = new("#6EE3A0", "#1FA868", "M12 4 V15 M7.5 10.5 L12 15 L16.5 10.5 M5 19.5 H19"),
        ["trophy"] = new("#FFD066", "#E88A12", "M7.5 4.5 H16.5 V10 A4.5 4.5 0 0 1 7.5 10 Z M7.5 6.5 H4.5 C4.5 9.5 6 11 8 11 M16.5 6.5 H19.5 C19.5 9.5 18 11 16 11 M12 14.5 V17.5 M8.5 20 H15.5 L15 17.5 H9 Z"),
        ["info"] = new("#B3BCD1", "#6C7690", "M12 3.5 A8.5 8.5 0 1 0 12 20.5 A8.5 8.5 0 1 0 12 3.5 Z M12 11 V16.5", Fill: "M10.8 7.8 A1.2 1.2 0 1 0 13.2 7.8 A1.2 1.2 0 1 0 10.8 7.8 Z"),
        ["flame"] = new("#FFB15C", "#F05A1E", "M12 3 C13 7 17.5 9 17.5 14 A5.5 5.5 0 0 1 6.5 14 C6.5 11 8.5 9.5 9.5 8 C10 10 11 11 12 11 C12 8 11 5.5 12 3 Z"),
        ["games"] = new("#FF9F80", "#EC5B3C", "M7 7.5 H17 A4.5 4.5 0 0 1 21.5 12 V14.5 A3 3 0 0 1 16.5 16.5 L15 15 H9 L7.5 16.5 A3 3 0 0 1 2.5 14.5 V12 A4.5 4.5 0 0 1 7 7.5 Z M7.5 10.5 V13.5 M6 12 H9", Fill: "M15 11 A1.1 1.1 0 1 0 17.2 11 A1.1 1.1 0 1 0 15 11 Z M17 13.4 A1.1 1.1 0 1 0 19.2 13.4 A1.1 1.1 0 1 0 17 13.4 Z"),
        ["write"] = new("#5EE0C9", "#16A79A", "M4 20 L5 15.5 L15.5 5 A2.1 2.1 0 0 1 19 8.5 L8.5 19 Z M13.5 7 L17 10.5"),
        ["storage"] = new("#8FA8FF", "#4762D9", "M4 6.5 C4 4.5 20 4.5 20 6.5 V17.5 C20 19.5 4 19.5 4 17.5 Z M4 6.5 C4 8.5 20 8.5 20 6.5 M4 12 C4 14 20 14 20 12"),
        ["sparkle"] = new("#C59BFF", "#8452E8", Fill: "M12 2.5 C12.9 8 15.5 10.8 21.5 12 C15.5 13.2 12.9 16 12 21.5 C11.1 16 8.5 13.2 2.5 12 C8.5 10.8 11.1 8 12 2.5 Z"),
    };

    public static (Color a, Color b) IconColors(string name) =>
        Icons.TryGetValue(name, out var d) ? (Color.Parse(d.A), Color.Parse(d.B)) : (Color.Parse("#C59BFF"), Color.Parse("#8452E8"));

    public static bool HasIcon(string name) => Icons.ContainsKey(name);

    /// <summary>Цветная иконка-плитка. size — сторона плитки.</summary>
    public static Control Icon(string name, double size = 32)
    {
        if (!Icons.TryGetValue(name, out var d)) d = Icons["sparkle"];
        var tile = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.3), Background = Grad(d.A, d.B, 45),
            BoxShadow = size >= 40 ? BoxShadows.Parse($"0 {size * 0.1:0} {size * 0.3:0} 0 #40000000") : default,
        };
        if (d.Glyph != null)
        {
            tile.Child = new TextBlock
            {
                Text = d.Glyph, FontFamily = U.R<FontFamily>("JpFont"), FontWeight = FontWeight.Bold, FontSize = size * 0.52, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, size * 0.04),
            };
            return tile;
        }
        var c = new Canvas { Width = 24, Height = 24 };
        // Тонкая «тень» рисунка придаёт объём
        if (d.Path != null) c.Children.Add(P(d.Path, null, C("#33000000"), 2.1).Also(p => p.RenderTransform = new TranslateTransform(0, 0.8)));
        if (d.Fill != null) c.Children.Add(P(d.Fill, Brushes.White));
        if (d.Path != null) c.Children.Add(P(d.Path, null, Brushes.White, 2.1));
        tile.Child = new Viewbox { Width = size * 0.62, Height = size * 0.62, Child = c, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return tile;
    }

    /// <summary>Только рисунок иконки нужного цвета, без плитки (для кнопок и меню).</summary>
    public static Control Glyph(string name, double size, IBrush brush)
    {
        if (!Icons.TryGetValue(name, out var d)) d = Icons["sparkle"];
        if (d.Glyph != null) return new TextBlock { Text = d.Glyph, FontFamily = U.R<FontFamily>("JpFont"), FontWeight = FontWeight.Bold, FontSize = size * 0.85, Foreground = brush, VerticalAlignment = VerticalAlignment.Center };
        var c = new Canvas { Width = 24, Height = 24 };
        if (d.Fill != null) c.Children.Add(P(d.Fill, brush));
        if (d.Path != null) c.Children.Add(P(d.Path, null, brush, 2));
        return new Viewbox { Width = size, Height = size, Child = c, VerticalAlignment = VerticalAlignment.Center };
    }
}
