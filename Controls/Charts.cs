using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Matsubakira.Services;

namespace Matsubakira.Controls;

/// <summary>Две кривые интонации (образец и ученик), рисуются одним OnRender с плавным «прорисовыванием».</summary>
public sealed class PitchView : FrameworkElement
{
    readonly double[] _ref, _user;
    double _progress;
    static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(PitchView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((PitchView)d)._progress = (double)e.NewValue));
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    public PitchView(double[] reference, double[] user, double height = 110)
    {
        _ref = reference; _user = user; Height = height;
        Loaded += (_, _) => BeginAnimation(ProgressProperty, Anim.Da(0, 1, 900, new CubicEase { EasingMode = EasingMode.EaseOut }));
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || _ref.Length < 2) return;
        double min = Math.Min(_ref.Min(), _user.Min()) - 1, max = Math.Max(_ref.Max(), _user.Max()) + 1;
        var grid = new Pen(U.B("Stroke"), 1); grid.Freeze();
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
                if (i == 0) c.BeginFigure(p, false, false); else c.LineTo(p, true, true);
            }
        }
        g.Freeze();
        var pen = new Pen(b, thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}

/// <summary>Питч-акцент: моры фразы с линией высокого/низкого тона (как в словарях NHK).</summary>
public sealed class AccentView : FrameworkElement
{
    readonly List<(string kana, double pitch, bool accentEnd)> _m;
    public AccentView(List<(string kana, double pitch, bool accentEnd)> moras) { _m = moras; Height = 58; }

    protected override Size MeasureOverride(Size available) => new(Math.Min(available.Width, _m.Count * 26 + 8), 58);

    protected override void OnRender(DrawingContext dc)
    {
        if (_m.Count == 0) return;
        var voiced = _m.Where(m => m.pitch > 0).Select(m => m.pitch).ToArray();
        if (voiced.Length == 0) return;
        double lo = voiced.Min(), hi = voiced.Max(), mid = (lo + hi) / 2;
        double cell = Math.Min(26, (ActualWidth - 8) / _m.Count);
        var pen = new Pen(U.B("Accent"), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; pen.Freeze();
        var tf = new Typeface(U.R<FontFamily>("JpFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Point? prev = null;
        for (int i = 0; i < _m.Count; i++)
        {
            var (kana, pitch, accentEnd) = _m[i];
            double x = 4 + i * cell + cell / 2;
            var ft = new FormattedText(kana, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, 15, U.B("Text"), 1.25);
            dc.DrawText(ft, new Point(x - ft.Width / 2, 32));
            if (pitch <= 0) { prev = null; continue; }
            bool high = pitch >= mid && hi - lo > 0.15;
            var p = new Point(x, high ? 8 : 24);
            if (prev is Point pp) dc.DrawLine(pen, pp, p);
            dc.DrawEllipse(accentEnd ? U.B("Accent") : U.B("Bg"), pen, p, 3.2, 3.2);
            prev = p;
        }
    }
}
