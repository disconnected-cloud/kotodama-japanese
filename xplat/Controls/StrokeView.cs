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
using Path = Avalonia.Controls.Shapes.Path;
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
        var dash = new Avalonia.Collections.AvaloniaList<double> { 3, 3 };
        Children.Add(new Line { StartPoint = new Point(size / 2, 8), EndPoint = new Point(size / 2, size - 8), Stroke = U.B("Stroke"), StrokeDashArray = dash });
        Children.Add(new Line { StartPoint = new Point(8, size / 2), EndPoint = new Point(size - 8, size / 2), Stroke = U.B("Stroke"), StrokeDashArray = dash });
    }

    void Build(string[] paths, Brush inkBrush, bool plain)
    {
        double Thick = _thick;
        int n = 0;
        foreach (var d in paths)
        {
            Geometry g;
            try { g = Geometry.Parse(d); } catch { continue; }
            n++;
            var (len, start) = SvgPath.Measure(d);
            // Подложка-подсказка (бледный контур знака)
            if (!plain) _canvas.Children.Add(new Path { Data = g, Stroke = U.B("Surface3"), StrokeThickness = Thick, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round });
            double units = len / Thick + 1;
            var ink = new Path
            {
                Data = g, Stroke = inkBrush, StrokeThickness = Thick,
                StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { units, units * 2 }, StrokeDashOffset = 0,
            };
            var num = new TextBlock { Text = n.ToString(), FontSize = 6.5, Foreground = U.B("Accent"), FontWeight = FontWeights.SemiBold, Opacity = 0, IsHitTestVisible = false };
            Canvas.SetLeft(num, Math.Clamp(start.X - 7, 0, 100));
            Canvas.SetTop(num, Math.Clamp(start.Y - 8, 0, 100));
            _canvas.Children.Add(ink);
            _strokes.Add((ink, units, num));
        }
        if (!plain) foreach (var s in _strokes) _canvas.Children.Add(s.num);
    }

    /// <summary>Проиграть написание черта за чертой. speed — множитель скорости.</summary>
    public double Play(double speed = 1, double startMs = 150)
    {
        double Thick = _thick;
        double t = startMs;
        foreach (var (ink, units, num) in _strokes)
        {
            double dur = Math.Clamp(units * Thick * 9 / speed, 220 / speed, 900);
            var stroke = ink; double u = units;
            stroke.StrokeDashOffset = u;
            Anim.Run((stroke, "dash"), u, 0, (int)dur, v => stroke.StrokeDashOffset = v, Ease.SineInOut, (int)t);
            num.Opacity = 0;
            Anim.Opacity(num, 0, 1, 150, (int)t);
            t += dur + 140 / speed;
        }
        return t;
    }

    /// <summary>Показать знак целиком (без анимации), опционально с номерами черт.</summary>
    public void ShowAll(bool numbers)
    {
        foreach (var (ink, _, num) in _strokes)
        {
            Anim.Stop((ink, "dash"));
            ink.StrokeDashOffset = 0;
            Anim.Stop((num, "op"));
            num.Opacity = numbers ? 1 : 0;
        }
    }
}

/// <summary>Форма волны: столбики рисуются одним вызовом OnRender (без сотен элементов).</summary>
public sealed class WaveView : Control
{
    float[] _peaks = Array.Empty<float>();
    Brush _fill;
    public double Seconds { get; private set; }

    public WaveView(Brush fill, double height = 44)
    {
        _fill = fill;
        Height = height;
    }

    public void SetPeaks(float[] peaks, double seconds)
    {
        _peaks = peaks;
        Seconds = seconds;
        InvalidateVisual();
        RenderTransformOrigin = RelativePoint.Center;
        var st = new ScaleTransform(1, 0.2);
        RenderTransform = st;
        Anim.Run((st, "sy"), 0.2, 1, 420, v => st.ScaleY = v, Ease.BackOut(0.3));
    }

    public override void Render(DrawingContext dc)
    {
        if (_peaks.Length == 0 || Bounds.Width <= 0) return;
        double w = Bounds.Width / _peaks.Length, h = Bounds.Height, mid = h / 2;
        for (int i = 0; i < _peaks.Length; i++)
        {
            double bh = Math.Max(2, _peaks[i] * (h - 4));
            dc.DrawRectangle(_fill, null, new Rect(i * w + w * 0.2, mid - bh / 2, Math.Max(1.5, w * 0.6), bh), 1.5, 1.5);
        }
    }
}

/// <summary>Длина SVG-пути и его начальная точка (KanjiVG: команды M, L, C, S, Z в обоих регистрах).</summary>
public static class SvgPath
{
    /// <summary>Точки ломаной вдоль пути (для сравнения с чертой, нарисованной пером).</summary>
    public static List<Point> Points(string d) { var pts = new List<Point>(); Walk(d, pts); return pts; }

    public static (double len, Point start) Measure(string d) => Walk(d, null);

    static (double len, Point start) Walk(string d, List<Point>? pts)
    {
        var tokens = System.Text.RegularExpressions.Regex.Matches(d, @"[MmLlCcSsZzHhVv]|-?\d*\.?\d+(?:e-?\d+)?").Select(m => m.Value).ToList();
        int i = 0; char cmd = 'M';
        Point cur = new(), start = new(), first = new(), lastCtrl = new();
        bool haveFirst = false, prevCubic = false;
        double total = 0;
        double Num() => double.Parse(tokens[i++], System.Globalization.CultureInfo.InvariantCulture);
        void Cubic(Point c1, Point c2, Point to)
        {
            Point p = cur;
            for (int k = 1; k <= 16; k++)
            {
                double t = k / 16.0, u = 1 - t;
                var q = new Point(u * u * u * cur.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * to.X,
                                  u * u * u * cur.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * to.Y);
                total += Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y)); p = q; pts?.Add(q);
            }
            lastCtrl = c2; cur = to; prevCubic = true;
        }
        while (i < tokens.Count)
        {
            if (char.IsLetter(tokens[i][0]) && tokens[i] is not ("e")) cmd = tokens[i++][0];
            if (i >= tokens.Count && cmd is not ('Z' or 'z')) break;
            bool rel = char.IsLower(cmd);
            Point Pt() { double x = Num(), y = Num(); return rel ? new Point(cur.X + x, cur.Y + y) : new Point(x, y); }
            switch (char.ToUpper(cmd))
            {
                case 'M': cur = start = Pt(); pts?.Add(cur); if (!haveFirst) { first = cur; haveFirst = true; } prevCubic = false; cmd = rel ? 'l' : 'L'; break;
                case 'L': { var q = Pt(); total += Math.Sqrt((q.X - cur.X) * (q.X - cur.X) + (q.Y - cur.Y) * (q.Y - cur.Y)); cur = q; pts?.Add(q); prevCubic = false; break; }
                case 'H': { double x = Num(); var q = new Point(rel ? cur.X + x : x, cur.Y); total += Math.Abs(q.X - cur.X); cur = q; prevCubic = false; break; }
                case 'V': { double y = Num(); var q = new Point(cur.X, rel ? cur.Y + y : y); total += Math.Abs(q.Y - cur.Y); cur = q; prevCubic = false; break; }
                case 'C': { var c1 = Pt(); var c2 = Pt(); var to = Pt(); Cubic(c1, c2, to); break; }
                case 'S': { var c1 = prevCubic ? new Point(2 * cur.X - lastCtrl.X, 2 * cur.Y - lastCtrl.Y) : cur; var c2 = Pt(); var to = Pt(); Cubic(c1, c2, to); break; }
                case 'Z': total += Math.Sqrt((start.X - cur.X) * (start.X - cur.X) + (start.Y - cur.Y) * (start.Y - cur.Y)); cur = start; prevCubic = false; break;
                default: i++; break;
            }
        }
        return (Math.Max(total, 1), first);
    }
}

/// <summary>Холст для письма пером/мышью (замена WPF InkCanvas): рисует черту, по отпусканию отдаёт её точки.</summary>
public sealed class InkPad : Canvas
{
    Polyline? _live;
    readonly List<Point> _pts = new();
    public IBrush Ink { get; set; } = Brushes.White;
    public event Action<Point[]>? StrokeCollected;

    public InkPad()
    {
        Background = Brushes.Transparent;
        Cursor = new Cursor(StandardCursorType.Cross);
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            e.Pointer.Capture(this);
            _pts.Clear();
            _pts.Add(e.GetPosition(this));
            _live = new Polyline { Stroke = Ink, StrokeThickness = 9, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Points = new Avalonia.Collections.AvaloniaList<Point>(_pts) };
            Children.Add(_live);
        };
        PointerMoved += (_, e) =>
        {
            if (_live == null) return;
            var p = e.GetPosition(this);
            if (_pts.Count > 0 && Geo.Dist(p, _pts[^1]) < 1.2) return;
            _pts.Add(p);
            _live.Points.Add(p);
        };
        PointerReleased += (_, e) =>
        {
            if (_live == null) return;
            e.Pointer.Capture(null);
            Children.Remove(_live);
            _live = null;
            StrokeCollected?.Invoke(_pts.ToArray());
        };
    }

    public void Clear() { Children.Clear(); _live = null; }
}
