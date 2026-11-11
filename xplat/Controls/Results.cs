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

/// <summary>Итоговый экран сессии: кольцо точности, XP, статистика, конфетти.</summary>
public static class Results
{
    public static Control Build(int pct, int xp, (string label, string value)[] stats, Action? again, Action done, Panel confettiHost)
    {
        var root = new Grid();
        var card = new Border { Classes = { "Card" }, MaxWidth = 520, Padding = new Thickness(36, 32, 36, 28), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var sp = new StackPanel();
        card.Child = sp;

        // Кольцо
        var ring = new Grid { Width = 170, Height = 170, HorizontalAlignment = HorizontalAlignment.Center };
        ring.Children.Add(new Ellipse { Stroke = U.B("Surface2"), StrokeThickness = 12 });
        var arc = Ring(85, 79, pct / 100.0);
        arc.Stroke = pct >= 70 ? U.B("AccentGradient") : U.B("GoldGradient");
        arc.StrokeThickness = 12;
        arc.StrokeLineCap = PenLineCap.Round;
        ring.Children.Add(arc);
        var pctText = new TextBlock { FontSize = 40, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
        ring.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { pctText, new TextBlock { Text = "точность", FontSize = 12, Foreground = U.B("Muted"), HorizontalAlignment = HorizontalAlignment.Center } },
        });
        sp.Children.Add(ring);
        Anim.CountUp(pctText, pct, "%");

        var (jp, title) = pct switch
        {
            >= 95 => ("完璧！", "Идеально!"),
            >= 80 => ("すばらしい！", "Отличная работа!"),
            >= 60 => ("いいね！", "Хороший результат"),
            _ => ("がんばって！", "Не сдавайся — повторение творит чудеса"),
        };
        sp.Children.Add(new TextBlock { Text = jp, FontFamily = U.R<FontFamily>("JpSerif"), FontSize = 26, Foreground = U.B("Accent"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 2) });
        sp.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });

        var xpPill = U.Pill($"+{xp} XP", U.B("GoldGradient"), new SolidColorBrush(Color.FromRgb(0x3A, 0x1A, 0x00)));
        xpPill.HorizontalAlignment = HorizontalAlignment.Center; xpPill.Margin = new Thickness(0, 14, 0, 18); xpPill.Padding = new Thickness(14, 5, 14, 5);
        sp.Children.Add(xpPill);

        foreach (var (label, value) in stats)
        {
            var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            row.Children.Add(new TextBlock { Text = label, Foreground = U.B("Muted"), FontSize = 14 });
            row.Children.Add(new TextBlock { Text = value, FontWeight = FontWeights.SemiBold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Right });
            sp.Children.Add(row);
        }

        var buttons = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        if (again != null)
        {
            var b1 = U.Btn("Ещё раз", again, "", "");
            buttons.Children.Add(b1);
        }
        var b2 = U.Btn("Готово", done, "Primary", "");
        Grid.SetColumn(b2, again != null ? 2 : 0);
        if (again == null) Grid.SetColumnSpan(b2, 3);
        buttons.Children.Add(b2);
        sp.Children.Add(buttons);

        // Википе-тан радуется вместе с учеником (или подбадривает)
        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var mascot = Art.Mascot(250, pct >= 90 ? Mood.Joy : pct >= 70 ? Mood.Happy : pct >= 50 ? Mood.Wink : Mood.Think);
        mascot.VerticalAlignment = VerticalAlignment.Bottom; mascot.Margin = new Thickness(0, 0, 18, 30);
        if (!Shell.Compact) line.Children.Add(mascot);
        line.Children.Add(card);
        root.Children.Add(line);
        Anim.FadeUp(mascot, 120, 30, 500);
        Anim.FadeUp(card, 0, 24, 450);
        Anim.Pop(card, 0.9);
        if (pct >= 70) root.Loaded += (_, _) => Confetti(root);
        return root;
    }

    public static Path Ring(double center, double r, double frac)
    {
        frac = Math.Clamp(frac, 0, 0.9999);
        double a = frac * 2 * Math.PI;
        var start = new Point(center, center - r);
        var end = new Point(center + r * Math.Sin(a), center - r * Math.Cos(a));
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments = new PathSegments();
        if (frac > 0) fig.Segments.Add(new ArcSegment { Point = end, Size = new Size(r, r), IsLargeArc = frac > 0.5, SweepDirection = SweepDirection.Clockwise });
        var g = new PathGeometry { Figures = new PathFigures { fig } };
        return new Path { Data = g };
    }

    /// <summary>Короткий салют из конфетти. Элементы удаляются после анимации.</summary>
    public static void Confetti(Panel host)
    {
        var canvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        if (host is Grid g) { Grid.SetRowSpan(canvas, Math.Max(1, g.RowDefinitions.Count)); Grid.SetColumnSpan(canvas, Math.Max(1, g.ColumnDefinitions.Count)); }
        host.Children.Add(canvas);
        double w = Math.Max(400, host.Bounds.Width), h = Math.Max(400, host.Bounds.Height);
        Color[] colors = { Color.FromRgb(0xFF, 0x6B, 0x9D), Color.FromRgb(0xA7, 0x7B, 0xFF), Color.FromRgb(0xFF, 0xC8, 0x57), Color.FromRgb(0x4A, 0xDE, 0x9B), Color.FromRgb(0x5C, 0xC8, 0xFF) };
        var rng = U.Rng;
        for (int i = 0; i < 70; i++)
        {
            var brush = new SolidColorBrush(colors[rng.Next(colors.Length)]);
            var rot = new RotateTransform(rng.Next(360));
            var tr = new TranslateTransform();
            var rect = new Rectangle
            {
                Width = 6 + rng.Next(6), Height = 10 + rng.Next(8), Fill = brush, RadiusX = 2, RadiusY = 2,
                RenderTransform = new TransformGroup { Children = { rot, tr } }, RenderTransformOrigin = RelativePoint.Center,
            };
            Canvas.SetLeft(rect, w / 2); Canvas.SetTop(rect, h * 0.35);
            canvas.Children.Add(rect);
            double ang = rng.NextDouble() * Math.PI * 2, power = 180 + rng.NextDouble() * 380;
            double dx = Math.Cos(ang) * power, dy = Math.Sin(ang) * power * 0.7 - 120;
            int dur = 1400 + rng.Next(900);
            double a0 = rot.Angle, spin = rng.Next(-720, 720);
            // вылет вверх-в стороны (35% времени), затем падение
            Anim.Run((tr, "confetti"), 0, 1, dur, k =>
            {
                tr.X = dx * Ease.CubicOut(k);
                tr.Y = k < 0.35 ? dy * Ease.CubicOut(k / 0.35) : dy + 420 * Ease.QuadIn((k - 0.35) / 0.65);
                rot.Angle = a0 + spin * k;
            }, Ease.Linear);
            Anim.Opacity(rect, 1, 0, dur - 500, 500);
        }
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        t.Tick += (_, _) => { t.Stop(); host.Children.Remove(canvas); };
        t.Start();
    }
}
