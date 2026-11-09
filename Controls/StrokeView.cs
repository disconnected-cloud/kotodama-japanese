using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Matsubakira.Services;

namespace Matsubakira.Controls;

/// <summary>
/// Анимация порядка черт кандзи. Контуры — KanjiVG (CC BY-SA 3.0), сетка 109×109.
/// Каждая черта «прорисовывается» через анимацию StrokeDashOffset — это дёшево:
/// анимируется одно свойство пера, геометрия заморожена и не пересчитывается.
/// </summary>
public sealed class StrokeView : Grid
{
    const double Thick = 3.6;
    readonly List<(Path ink, double len, TextBlock num)> _strokes = new();
    readonly Canvas _canvas = new() { Width = 109, Height = 109 };
    public int Count => _strokes.Count;
    public bool HasData { get; }

    public StrokeView(string ch, double size = 220, bool plain = false, Brush? ink = null, double thick = Thick)
    {
        Width = Height = size;
        _thick = thick;
        if (!plain) AddCell(size);
        var paths = Db.Strokes(ch);
        HasData = paths is { Length: > 0 };
        if (!HasData)
        {
            Children.Add(U.Jp(ch, size * 0.6, ink, true).Center().CenterV());
            return;
        }
        Build(paths!, ink ?? U.B("Text"), plain);
        Children.Add(new Viewbox { Child = _canvas, Margin = new Thickness(plain ? 0 : size * 0.06) });
    }

    readonly double _thick;

    void AddCell(double size)
    {
        // Клетка-пропись
        var cell = new Border { CornerRadius = new CornerRadius(14), Background = U.B("Surface2"), BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(1) };
        Children.Add(cell);
        var dash = new DoubleCollection { 3, 3 }; dash.Freeze();
        Children.Add(new Line { X1 = size / 2, X2 = size / 2, Y1 = 8, Y2 = size - 8, Stroke = U.B("Stroke"), StrokeDashArray = dash });
        Children.Add(new Line { Y1 = size / 2, Y2 = size / 2, X1 = 8, X2 = size - 8, Stroke = U.B("Stroke"), StrokeDashArray = dash });
    }

    void Build(string[] paths, Brush inkBrush, bool plain)
    {
        double Thick = _thick;
        int n = 0;
        foreach (var d in paths)
        {
            Geometry g;
            try { g = Geometry.Parse(d); g.Freeze(); } catch { continue; }
            n++;
            var flat = g.GetFlattenedPathGeometry(0.2, ToleranceType.Absolute);
            double len = Length(flat);
            // Подложка-подсказка (бледный контур знака)
            if (!plain) _canvas.Children.Add(new Path { Data = g, Stroke = U.B("Surface3"), StrokeThickness = Thick, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
            double units = len / Thick + 1;
            var ink = new Path
            {
                Data = g, Stroke = inkBrush, StrokeThickness = Thick,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, StrokeDashCap = PenLineCap.Round,
                StrokeDashArray = new DoubleCollection { units, units * 2 }, StrokeDashOffset = 0,
            };
            var start = flat.Figures.Count > 0 ? flat.Figures[0].StartPoint : new Point();
            var num = new TextBlock { Text = n.ToString(), FontSize = 6.5, Foreground = U.B("Accent"), FontWeight = FontWeights.SemiBold, Opacity = 0, IsHitTestVisible = false };
            Canvas.SetLeft(num, Math.Clamp(start.X - 7, 0, 100));
            Canvas.SetTop(num, Math.Clamp(start.Y - 8, 0, 100));
            _canvas.Children.Add(ink);
            _strokes.Add((ink, units, num));
        }
        if (!plain) foreach (var s in _strokes) _canvas.Children.Add(s.num);
    }

    static double Length(PathGeometry flat)
    {
        double total = 0;
        foreach (var f in flat.Figures)
        {
            var p = f.StartPoint;
            foreach (var seg in f.Segments)
            {
                IEnumerable<Point> pts = seg switch
                {
                    PolyLineSegment pl => pl.Points,
                    LineSegment l => new[] { l.Point },
                    _ => Array.Empty<Point>(),
                };
                foreach (var q in pts) { total += (q - p).Length; p = q; }
            }
        }
        return Math.Max(total, 1);
    }

    /// <summary>Проиграть написание черта за чертой. speed — множитель скорости.</summary>
    public double Play(double speed = 1, double startMs = 150)
    {
        double Thick = _thick;
        double t = startMs;
        foreach (var (ink, units, num) in _strokes)
        {
            double dur = Math.Clamp(units * Thick * 9 / speed, 220 / speed, 900);
            ink.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            ink.StrokeDashOffset = units;
            var a = new DoubleAnimation(units, 0, TimeSpan.FromMilliseconds(dur))
            {
                BeginTime = TimeSpan.FromMilliseconds(t),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd,
            };
            ink.BeginAnimation(Shape.StrokeDashOffsetProperty, a);
            num.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)) { BeginTime = TimeSpan.FromMilliseconds(t) });
            t += dur + 140 / speed;
        }
        return t;
    }

    /// <summary>Показать знак целиком (без анимации), опционально с номерами черт.</summary>
    public void ShowAll(bool numbers)
    {
        foreach (var (ink, _, num) in _strokes)
        {
            ink.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            ink.StrokeDashOffset = 0;
            num.BeginAnimation(OpacityProperty, null);
            num.Opacity = numbers ? 1 : 0;
        }
    }
}

/// <summary>Форма волны: столбики рисуются одним вызовом OnRender (без сотен элементов).</summary>
public sealed class WaveView : FrameworkElement
{
    float[] _peaks = Array.Empty<float>();
    Brush _fill;
    public double Seconds { get; private set; }

    public WaveView(Brush fill, double height = 44)
    {
        _fill = fill;
        Height = height;
        SnapsToDevicePixels = true;
    }

    public void SetPeaks(float[] peaks, double seconds)
    {
        _peaks = peaks;
        Seconds = seconds;
        InvalidateVisual();
        RenderTransformOrigin = new Point(0.5, 0.5);
        var st = new ScaleTransform(1, 0.2);
        RenderTransform = st;
        st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.Da(0.2, 1, 420, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }));
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_peaks.Length == 0 || ActualWidth <= 0) return;
        double w = ActualWidth / _peaks.Length, h = ActualHeight, mid = h / 2;
        for (int i = 0; i < _peaks.Length; i++)
        {
            double bh = Math.Max(2, _peaks[i] * (h - 4));
            dc.DrawRoundedRectangle(_fill, null, new Rect(i * w + w * 0.2, mid - bh / 2, Math.Max(1.5, w * 0.6), bh), 1.5, 1.5);
        }
    }
}
