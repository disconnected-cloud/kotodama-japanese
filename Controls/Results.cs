using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Matsubakira.Services;

namespace Matsubakira.Controls;

/// <summary>Итоговый экран сессии: кольцо точности, XP, статистика, конфетти.</summary>
public static class Results
{
    public static FrameworkElement Build(int pct, int xp, (string label, string value)[] stats, Action? again, Action done, Panel confettiHost)
    {
        var root = new Grid();
        var card = new Border { Style = U.St("Card"), MaxWidth = 520, Padding = new Thickness(36, 32, 36, 28), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var sp = new StackPanel();
        card.Child = sp;

        // Кольцо
        var ring = new Grid { Width = 170, Height = 170, HorizontalAlignment = HorizontalAlignment.Center };
        ring.Children.Add(new Ellipse { Stroke = U.B("Surface2"), StrokeThickness = 12 });
        var arc = Ring(85, 79, pct / 100.0);
        arc.Stroke = pct >= 70 ? U.B("AccentGradient") : U.B("GoldGradient");
        arc.StrokeThickness = 12;
        arc.StrokeStartLineCap = PenLineCap.Round; arc.StrokeEndLineCap = PenLineCap.Round;
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

        root.Children.Add(card);
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
        if (frac > 0) fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, frac > 0.5, SweepDirection.Clockwise, true));
        var g = new PathGeometry(new[] { fig });
        g.Freeze();
        return new Path { Data = g };
    }

    /// <summary>Короткий салют из конфетти. Элементы удаляются после анимации.</summary>
    public static void Confetti(Panel host)
    {
        var canvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        if (host is Grid g) { Grid.SetRowSpan(canvas, Math.Max(1, g.RowDefinitions.Count)); Grid.SetColumnSpan(canvas, Math.Max(1, g.ColumnDefinitions.Count)); }
        host.Children.Add(canvas);
        double w = Math.Max(400, host.ActualWidth), h = Math.Max(400, host.ActualHeight);
        Color[] colors = { Color.FromRgb(0xFF, 0x6B, 0x9D), Color.FromRgb(0xA7, 0x7B, 0xFF), Color.FromRgb(0xFF, 0xC8, 0x57), Color.FromRgb(0x4A, 0xDE, 0x9B), Color.FromRgb(0x5C, 0xC8, 0xFF) };
        var rng = U.Rng;
        for (int i = 0; i < 70; i++)
        {
            var brush = new SolidColorBrush(colors[rng.Next(colors.Length)]); brush.Freeze();
            var rot = new RotateTransform(rng.Next(360));
            var tr = new TranslateTransform();
            var rect = new Rectangle
            {
                Width = 6 + rng.Next(6), Height = 10 + rng.Next(8), Fill = brush, RadiusX = 2, RadiusY = 2,
                RenderTransform = new TransformGroup { Children = { rot, tr } }, RenderTransformOrigin = new Point(0.5, 0.5),
            };
            Canvas.SetLeft(rect, w / 2); Canvas.SetTop(rect, h * 0.35);
            canvas.Children.Add(rect);
            double ang = rng.NextDouble() * Math.PI * 2, power = 180 + rng.NextDouble() * 380;
            double dx = Math.Cos(ang) * power, dy = Math.Sin(ang) * power * 0.7 - 120;
            var dur = TimeSpan.FromMilliseconds(1400 + rng.Next(900));
            tr.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dx, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            var y = new DoubleAnimationUsingKeyFrames { Duration = dur };
            y.KeyFrames.Add(new EasingDoubleKeyFrame(dy, KeyTime.FromPercent(0.35), new CubicEase { EasingMode = EasingMode.EaseOut }));
            y.KeyFrames.Add(new EasingDoubleKeyFrame(dy + 420, KeyTime.FromPercent(1), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            tr.BeginAnimation(TranslateTransform.YProperty, y);
            rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(rot.Angle, rot.Angle + rng.Next(-720, 720), dur));
            rect.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, dur) { BeginTime = TimeSpan.FromMilliseconds(500) });
        }
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        t.Tick += (_, _) => { t.Stop(); host.Children.Remove(canvas); };
        t.Start();
    }
}
