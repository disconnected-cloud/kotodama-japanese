using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RadioButton = Matsubakira.Compat.RadioButton;
using CheckBox = Matsubakira.Compat.CheckBox;
using ToggleButton = Matsubakira.Compat.ToggleButton;
using Matsubakira.Services;

namespace Matsubakira.Controls;

/// <summary>Две кривые интонации (образец и ученик), рисуются одним OnRender с плавным «прорисовыванием».</summary>
public sealed class PitchView : Control
{
    readonly double[] _ref, _user;
    double _progress;

    public PitchView(double[] reference, double[] user, double height = 110)
    {
        _ref = reference; _user = user; Height = height;
        Loaded += (_, _) => Anim.Run((this, "progress"), 0, 1, 900, v => { _progress = v; InvalidateVisual(); });
    }

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || _ref.Length < 2) return;
        double min = Math.Min(_ref.Min(), _user.Min()) - 1, max = Math.Max(_ref.Max(), _user.Max()) + 1;
        var grid = new Pen(U.B("Stroke"), 1);
        for (int i = 1; i < 4; i++) dc.DrawLine(grid, new Point(0, h * i / 4), new Point(w, h * i / 4));
        Draw(dc, _ref, U.B("Accent"), min, max, w, h, 3);
        Draw(dc, _user, U.B("Info"), min, max, w, h, 2.4);
    }

    void Draw(DrawingContext dc, double[] v, Brush b, double min, double max, double w, double h, double thick)
    {
        int count = Math.Max(2, (int)(v.Length * _progress));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            for (int i = 0; i < count; i++)
            {
                var p = new Point(i * w / (v.Length - 1), h - (v[i] - min) / (max - min) * h);
                if (i == 0) c.BeginFigure(p, false); else c.LineTo(p);
            }
            c.EndFigure(false);
        }
        var pen = new Pen(b, thick) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, pen, g);
    }
}

/// <summary>Питч-акцент: моры фразы с линией высокого/низкого тона (как в словарях NHK).</summary>
public sealed class AccentView : Control
{
    readonly List<(string kana, double pitch, bool accentEnd)> _m;
    readonly double _k;   // масштаб: 1 — обычный, 0.7 — компактный (строки словаря)
    public AccentView(List<(string kana, double pitch, bool accentEnd)> moras, bool compact = false) { _m = moras; _k = compact ? 0.7 : 1; Height = 58 * _k; }

    protected override Size MeasureOverride(Size available) => new(Math.Min(available.Width, _m.Count * 26 * _k + 8), 58 * _k);

    public override void Render(DrawingContext dc)
    {
        if (_m.Count == 0) return;
        var voiced = _m.Where(m => m.pitch >= 0).Select(m => m.pitch).ToArray();
        if (voiced.Length == 0) return;
        double lo = voiced.Min(), hi = voiced.Max(), mid = (lo + hi) / 2;
        double cell = Math.Min(26 * _k + (_m.Any(x => x.kana.Length > 1) ? 6 : 0), (Bounds.Width - 8) / _m.Count);
        var pen = new Pen(U.B("Accent"), 2 * _k) { LineCap = PenLineCap.Round };
        var tf = new Typeface(U.R<FontFamily>("JpFont"));
        Point? prev = null;
        for (int i = 0; i < _m.Count; i++)
        {
            var (kana, pitch, accentEnd) = _m[i];
            double x = 4 + i * cell + cell / 2;
            bool particle = kana.StartsWith("(");
            var ft = new FormattedText(particle ? kana.Trim('(', ')') : kana, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, 15 * _k, U.B(particle ? "Faint" : "Text"));
            dc.DrawText(ft, new Point(x - ft.Width / 2, 32 * _k));
            if (pitch < 0) { prev = null; continue; }
            bool high = pitch >= mid && hi - lo > 0.15;
            var p = new Point(x, (high ? 8 : 24) * _k);
            if (prev is Point pp) dc.DrawLine(pen, pp, p);
            if (particle) dc.DrawEllipse(null, pen, p, 2.6 * _k, 2.6 * _k);
            else dc.DrawEllipse(accentEnd ? U.B("Accent") : U.B("Bg"), pen, p, 3.2 * _k, 3.2 * _k);
            prev = p;
        }
    }
}
