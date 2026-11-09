using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>
/// Прописи: пишешь знак мышью/пером, каждая черта сверяется с KanjiVG —
/// порядок, направление (начальная точка) и форма (средняя дистанция после ресемплинга).
/// </summary>
public sealed class WritingPage : UserControl, IPage, IKeyTarget
{
    public string Title => "Прописи";
    const double Size = 330;
    readonly List<(string ch, string hint)> _deck;
    int _i, _stroke, _mistakes, _total, _score;
    bool _showGuide = true;
    readonly InkCanvas _ink = new() { Width = Size, Height = Size, Background = Brushes.Transparent };
    readonly Canvas _done = new() { Width = 109, Height = 109, IsHitTestVisible = false };
    readonly Canvas _guide = new() { Width = 109, Height = 109, IsHitTestVisible = false };
    readonly Canvas _flash = new() { Width = 109, Height = 109, IsHitTestVisible = false };
    readonly TextBlock _title = U.T("", "H2", 20), _status = U.T("", "Body", 14), _counter = U.T("", "Muted");
    List<(Geometry g, Point[] pts)> _expected = new();

    public WritingPage(string? level = null)
    {
        var kanji = Db.KanjiOf(level ?? (ProgressStore.P.Level is "N4" or "N3" ? ProgressStore.P.Level : "N5")).Where(k => Db.Strokes(k.Char) != null).Select(k => (k.Char, k.Ru));
        var kana = Kana.All.Where(c => c.Group == Kana.Basic && Db.Strokes(c.Hira) != null).Select(c => (c.Hira, c.Romaji));
        // Сначала знаки, которые уже изучаются, затем остальные
        _deck = kanji.OrderBy(k => ProgressStore.Card("k:" + k.Item1) == null ? 1 : 0).ThenBy(_ => U.Rng.Next()).Take(10).ToList();
        if (_deck.Count < 3) _deck = kana.Take(10).ToList();

        _ink.DefaultDrawingAttributes = new DrawingAttributes { Color = ((SolidColorBrush)U.B("Text")).Color, Width = 9, Height = 9, FitToCurve = true, StylusTip = StylusTip.Ellipse };
        _ink.StrokeCollected += OnStroke;

        var board = new Grid { Width = Size, Height = Size };
        board.Children.Add(new Border { CornerRadius = new CornerRadius(18), Background = U.B("Surface2"), BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(1) });
        var dash = new DoubleCollection { 4, 4 };
        board.Children.Add(new Line { X1 = Size / 2, X2 = Size / 2, Y1 = 10, Y2 = Size - 10, Stroke = U.B("Stroke"), StrokeDashArray = dash });
        board.Children.Add(new Line { Y1 = Size / 2, Y2 = Size / 2, X1 = 10, X2 = Size - 10, Stroke = U.B("Stroke"), StrokeDashArray = dash });
        board.Children.Add(new Viewbox { Child = _guide });
        board.Children.Add(new Viewbox { Child = _done });
        board.Children.Add(new Viewbox { Child = _flash });
        board.Children.Add(_ink);

        var side = new StackPanel { Margin = new Thickness(28, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Width = 300 };
        side.Children.Add(_counter);
        side.Children.Add(_title.M(0, 6, 0, 6));
        side.Children.Add(_status);
        side.Children.Add(U.Row(8,
            U.Btn("Подсказка", () => { _showGuide = !_showGuide; DrawGuide(); }, "", ""),
            U.Btn("Заново", () => Load(), "Ghost", "")).M(0, 18, 0, 0));
        side.Children.Add(U.Row(8,
            U.Btn("Показать порядок", ShowOrder, "Ghost", ""),
            U.Btn("Дальше", Next, "Primary", "")).M(0, 8, 0, 0));
        side.Children.Add(U.T("Пиши каждую черту одним движением. Важно: порядок черт и направление (сверху вниз, слева направо).", "Muted", 12.5).M(0, 18, 0, 0));

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(board); row.Children.Add(side);
        var root = new StackPanel();
        root.Children.Add(U.Header("書", "Прописи", "Пиши мышью или пером — приложение проверит порядок, направление и форму каждой черты."));
        root.Children.Add(row);
        Content = U.Page(root);
        Load();
    }

    void Load()
    {
        _ink.Strokes.Clear(); _done.Children.Clear(); _flash.Children.Clear();
        _stroke = 0; _mistakes = 0;
        var (ch, hint) = _deck[_i];
        _expected = Db.Strokes(ch)!.Select(d =>
        {
            var g = Geometry.Parse(d); g.Freeze();
            return (g, Resample(Flatten(g), 24));
        }).ToList();
        _title.Text = $"{ch}  ·  {hint}";
        _counter.Text = $"Знак {_i + 1} из {_deck.Count} · черт: {_expected.Count}";
        _status.Text = "Начни с черты 1";
        _status.Foreground = U.B("Muted");
        DrawGuide();
    }

    void DrawGuide()
    {
        _guide.Children.Clear();
        if (!_showGuide) return;
        foreach (var (g, _) in _expected)
            _guide.Children.Add(new Path { Data = g, Stroke = U.B("Surface3"), StrokeThickness = 3.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        if (_stroke < _expected.Count)
        {
            var start = _expected[_stroke].pts[0];
            var dot = new Ellipse { Width = 5, Height = 5, Fill = U.B("Accent") };
            Canvas.SetLeft(dot, start.X - 2.5); Canvas.SetTop(dot, start.Y - 2.5);
            _guide.Children.Add(dot);
        }
    }

    void OnStroke(object? sender, InkCanvasStrokeCollectedEventArgs e)
    {
        if (_stroke >= _expected.Count) { _ink.Strokes.Remove(e.Stroke); return; }
        var pts = e.Stroke.StylusPoints.Select(p => new Point(p.X * 109 / Size, p.Y * 109 / Size)).ToArray();
        _ink.Strokes.Remove(e.Stroke);
        if (pts.Length < 2) return;
        var user = Resample(pts.ToList(), 24);
        var exp = _expected[_stroke].pts;
        double fwd = Mean(user, exp), rev = Mean(user, exp.Reverse().ToArray());
        double len = Length(exp);
        double tol = Math.Clamp(9 + len * 0.12, 11, 20);
        bool ok = fwd < tol;
        string? why = null;
        if (!ok && rev < tol) why = "Направление наоборот — веди черту от начальной точки.";
        else if (!ok)
        {
            // Может, это другая черта знака (нарушен порядок)?
            int other = _expected.FindIndex(_stroke, x => Mean(user, x.pts) < tol);
            why = other > _stroke ? $"Это черта {other + 1}, а сейчас нужна черта {_stroke + 1}." : "Форма или положение не совпадают — посмотри на подсветку.";
        }
        if (ok)
        {
            AddDone(_expected[_stroke].g, U.B("Text"));
            _stroke++;
            Sfx.Ok();
            if (_stroke == _expected.Count) Finish();
            else { _status.Text = $"Верно! Черта {_stroke + 1}"; _status.Foreground = U.B("Good"); }
        }
        else
        {
            _mistakes++;
            Sfx.Bad();
            _status.Text = why!; _status.Foreground = U.B("Bad");
            FlashExpected(_stroke);
        }
        DrawGuide();
    }

    void AddDone(Geometry g, Brush b) =>
        _done.Children.Add(new Path { Data = g, Stroke = b, StrokeThickness = 3.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });

    void FlashExpected(int i)
    {
        _flash.Children.Clear();
        var path = new Path { Data = _expected[i].g, Stroke = U.B("Accent"), StrokeThickness = 4.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _flash.Children.Add(path);
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1400))));
        path.BeginAnimation(OpacityProperty, a);
    }

    void ShowOrder()
    {
        _flash.Children.Clear();
        var sv = new StrokeView(_deck[_i].ch, 109, plain: true, ink: U.B("Accent"), thick: 3.6);
        _flash.Children.Add(sv);
        sv.Play(1.2);
    }

    void Finish()
    {
        int score = Math.Max(0, 100 - _mistakes * 15);
        _score += score; _total++;
        _status.Text = _mistakes == 0 ? "Идеально! Все черты верно 完璧" : $"Готово! Ошибок: {_mistakes} · {score} баллов";
        _status.Foreground = U.B(_mistakes == 0 ? "Good" : "Gold");
        ProgressStore.AddXp(_mistakes == 0 ? 5 : 3);
        Speech.Say(_deck[_i].ch);
        Sfx.Done();
    }

    void Next()
    {
        if (_i + 1 >= _deck.Count)
        {
            Shell.Toast($"Прописи завершены: в среднем {(_total == 0 ? 0 : _score / _total)} баллов", "書");
            Shell.Back();
            return;
        }
        _i++;
        Load();
    }

    public bool HandleKey(Key key)
    {
        if (key == Key.Enter) { Next(); return true; }
        if (key == Key.H) { _showGuide = !_showGuide; DrawGuide(); return true; }
        return false;
    }

    // ─────────── Геометрия ───────────
    static List<Point> Flatten(Geometry g)
    {
        var res = new List<Point>();
        var flat = g.GetFlattenedPathGeometry(0.3, ToleranceType.Absolute);
        foreach (var f in flat.Figures)
        {
            res.Add(f.StartPoint);
            foreach (var s in f.Segments)
            {
                if (s is PolyLineSegment pl) res.AddRange(pl.Points);
                else if (s is LineSegment l) res.Add(l.Point);
            }
        }
        return res;
    }

    static double Length(IList<Point> p) { double s = 0; for (int i = 1; i < p.Count; i++) s += (p[i] - p[i - 1]).Length; return s; }

    static Point[] Resample(List<Point> p, int n)
    {
        double total = Length(p), step = total / (n - 1), acc = 0;
        var res = new List<Point> { p[0] };
        for (int i = 1; i < p.Count && res.Count < n; i++)
        {
            double d = (p[i] - p[i - 1]).Length;
            while (acc + d >= step && res.Count < n)
            {
                double t = (step - acc) / d;
                var q = p[i - 1] + (p[i] - p[i - 1]) * t;
                res.Add(q);
                p[i - 1] = q; d = (p[i] - q).Length; acc = 0;
            }
            acc += d;
        }
        while (res.Count < n) res.Add(p[^1]);
        return res.ToArray();
    }

    static double Mean(Point[] a, Point[] b) { double s = 0; for (int i = 0; i < a.Length; i++) s += (a[i] - b[i]).Length; return s / a.Length; }
}
