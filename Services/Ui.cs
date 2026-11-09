using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Matsubakira.Services;

/// <summary>Страница, которую нужно обновить при показе.</summary>
public interface IPage { string Title { get; } void OnShown() { } }
/// <summary>Страница, обрабатывающая клавиатуру (1-4, пробел, Enter).</summary>
public interface IKeyTarget { bool HandleKey(Key key); }

public static class Shell
{
    public static MainWindow Main { get; set; } = null!;
    public static void Open(FrameworkElement page) => Main.Push(page);
    /// <summary>Открыть страницу с анимацией «раскрытия» из карточки.</summary>
    public static void OpenFrom(FrameworkElement source, FrameworkElement page) => Main.ExpandFrom(source, page);
    public static void Back() => Main.Pop();
    public static void Go(string section) => Main.NavigateRoot(section);
    public static void Toast(string text, string? jp = null) => Main.ShowToast(text, jp);
}

/// <summary>Небольшие анимации. Только Opacity/RenderTransform — они композятся на GPU и не вызывают перерасчёт layout.</summary>
public static class Anim
{
    static readonly IEasingFunction Out = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    static readonly IEasingFunction Back = Freeze(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 });

    static IEasingFunction Freeze(EasingFunctionBase e) { e.Freeze(); return e; }

    static (ScaleTransform s, TranslateTransform t) Transforms(UIElement el)
    {
        if (el.RenderTransform is TransformGroup g && g.Children.Count == 2 && g.Children[0] is ScaleTransform s0 && g.Children[1] is TranslateTransform t0 && !g.IsFrozen)
            return (s0, t0);
        var s = new ScaleTransform(); var t = new TranslateTransform();
        el.RenderTransform = new TransformGroup { Children = { s, t } };
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        return (s, t);
    }

    public static DoubleAnimation Da(double? from, double to, int ms, IEasingFunction? ease = null, int delay = 0) =>
        new(to, TimeSpan.FromMilliseconds(ms)) { From = from, EasingFunction = ease ?? Out, BeginTime = TimeSpan.FromMilliseconds(delay) };

    public static void FadeUp(UIElement el, int delay = 0, double dy = 16, int ms = 380)
    {
        var (_, t) = Transforms(el);
        el.Opacity = 0;
        el.BeginAnimation(UIElement.OpacityProperty, Da(0, 1, ms, null, delay));
        t.BeginAnimation(TranslateTransform.YProperty, Da(dy, 0, ms, null, delay));
    }

    /// <summary>Каскадное появление детей панели.</summary>
    public static void Stagger(Panel p, int step = 40, int start = 0, int max = 24)
    {
        int i = 0;
        foreach (UIElement c in p.Children)
        {
            if (i < max) FadeUp(c, start + i * step, 14, 360);
            i++;
        }
    }

    public static void Pop(UIElement el, double from = 0.85)
    {
        var (s, _) = Transforms(el);
        s.BeginAnimation(ScaleTransform.ScaleXProperty, Da(from, 1, 380, Back));
        s.BeginAnimation(ScaleTransform.ScaleYProperty, Da(from, 1, 380, Back));
    }

    public static void Shake(UIElement el)
    {
        var (_, t) = Transforms(el);
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(380) };
        double[] xs = { -10, 9, -7, 5, -3, 0 };
        for (int i = 0; i < xs.Length; i++)
            a.KeyFrames.Add(new EasingDoubleKeyFrame(xs[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(60 * (i + 1)))));
        t.BeginAnimation(TranslateTransform.XProperty, a);
    }

    /// <summary>«3D»-переворот карточки: сжатие по X, смена содержимого, разворот.</summary>
    public static void Flip(UIElement el, Action midpoint)
    {
        var (s, _) = Transforms(el);
        var half = Da(1, 0, 140, new QuadraticEase { EasingMode = EasingMode.EaseIn });
        half.Completed += (_, _) =>
        {
            midpoint();
            s.BeginAnimation(ScaleTransform.ScaleXProperty, Da(0, 1, 220, Back));
        };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, half);
    }

    public static void Pulse(UIElement el, bool on)
    {
        var (s, _) = Transforms(el);
        if (!on)
        {
            s.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            el.BeginAnimation(UIElement.OpacityProperty, null);
            return;
        }
        var a = new DoubleAnimation(1, 1.9, TimeSpan.FromMilliseconds(1300)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Out };
        var o = new DoubleAnimation(0.7, 0, TimeSpan.FromMilliseconds(1300)) { RepeatBehavior = RepeatBehavior.Forever };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        el.BeginAnimation(UIElement.OpacityProperty, o);
    }

    public static void CountUp(TextBlock tb, int to, string suffix = "", int ms = 700)
    {
        var start = Environment.TickCount64;
        EventHandler? h = null;
        h = (_, _) =>
        {
            var k = Math.Min(1.0, (Environment.TickCount64 - start) / (double)ms);
            var e = 1 - Math.Pow(1 - k, 3);
            tb.Text = ((int)Math.Round(to * e)).ToString() + suffix;
            if (k >= 1) CompositionTarget.Rendering -= h;
        };
        CompositionTarget.Rendering += h;
    }
}

/// <summary>Утилиты построения UI из кода — для генерируемых списков и сеток.</summary>
public static class U
{
    public static T R<T>(string key) => (T)Application.Current.Resources[key];
    public static Brush B(string key) => R<Brush>(key);
    public static Style St(string key) => R<Style>(key);

    public static TextBlock T(string text, string style = "Body", double? size = null, Brush? fg = null, FontWeight? weight = null)
    {
        var tb = new TextBlock { Text = text, Style = St(style == "Muted" ? "MutedText" : style) };
        if (size is { } s) tb.FontSize = s;
        if (fg != null) tb.Foreground = fg;
        if (weight is { } w) tb.FontWeight = w;
        return tb;
    }

    public static TextBlock Jp(string text, double size, Brush? fg = null, bool serif = false)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = size,
            FontFamily = R<FontFamily>(serif ? "JpSerif" : "JpFont"),
            Foreground = fg ?? B("Text"),
            TextWrapping = TextWrapping.Wrap,
        };
        return tb;
    }

    public static Border Card(UIElement child, Thickness? pad = null)
    {
        var b = new Border { Style = St("Card"), Child = child };
        if (pad is { } p) b.Padding = p;
        return b;
    }

    public static Button Btn(string text, Action click, string style = "", string? icon = null)
    {
        object content = text;
        if (icon != null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = icon, FontFamily = R<FontFamily>("Icons"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, text.Length > 0 ? 8 : 0, 0) });
            if (text.Length > 0) sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            content = sp;
        }
        var b = new Button { Content = content };
        if (style.Length > 0) b.Style = St(style);
        b.Click += (_, _) => click();
        return b;
    }

    public static Button SpeakBtn(Func<string> text, bool slow = false)
    {
        var b = new Button { Style = St("IconButton"), Content = slow ? "" : "", ToolTip = slow ? "Медленно" : "Произнести" };
        b.Click += (_, _) => Speech.Say(text(), slow);
        return b;
    }

    public static Button SpeakBtnSmall(string text)
    {
        var b = new Button { Style = St("IconButton"), Content = "", Width = 30, Height = 30, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Произнести" };
        b.Click += (_, _) => Speech.Say(text);
        return b;
    }

    public static T Center<T>(this T el) where T : FrameworkElement
    {
        el.HorizontalAlignment = HorizontalAlignment.Center;
        if (el is TextBlock tb) tb.TextAlignment = TextAlignment.Center;
        return el;
    }

    public static T CenterV<T>(this T el) where T : FrameworkElement
    { el.VerticalAlignment = VerticalAlignment.Center; return el; }

    public static T Col<T>(this T el, int col, int span = 1) where T : UIElement
    { Grid.SetColumn(el, col); if (span > 1) Grid.SetColumnSpan(el, span); return el; }

    public static T RowAt<T>(this T el, int row) where T : UIElement
    { Grid.SetRow(el, row); return el; }

    public static Grid Cols(params string[] widths)
    {
        var g = new Grid();
        foreach (var w in widths)
            g.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = w == "auto" ? GridLength.Auto : w.EndsWith('*')
                    ? new GridLength(w.Length == 1 ? 1 : double.Parse(w[..^1]), GridUnitType.Star)
                    : new GridLength(double.Parse(w)),
            });
        return g;
    }

    /// <summary>Шапка страницы: японский заголовок-иероглиф, название и подзаголовок.</summary>
    public static FrameworkElement Header(string jp, string title, string subtitle, FrameworkElement? right = null)
    {
        var g = Cols("auto", "*", "auto");
        g.Margin = new Thickness(0, 4, 0, 24);
        var badge = new Border
        {
            Width = 52, Height = 52, CornerRadius = new CornerRadius(14), Background = B("AccentSoft"),
            Child = new TextBlock { Text = jp, FontFamily = R<FontFamily>("JpSerif"), FontSize = jp.Length > 1 ? 20 : 26, Foreground = B("Accent"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) },
        };
        g.Children.Add(badge);
        var sp = Stack(T(title, "H1"), T(subtitle, "Muted", 13.5).M(0, 3, 0, 0));
        sp.VerticalAlignment = VerticalAlignment.Center; sp.Margin = new Thickness(16, 0, 0, 0);
        g.Children.Add(sp.Col(1));
        if (right != null) { right.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(right.Col(2)); }
        return g;
    }

    /// <summary>Стандартная прокручиваемая страница с отступами.</summary>
    public static ScrollViewer Page(UIElement content) => new()
    {
        Content = new Border { Padding = new Thickness(40, 12, 40, 40), Child = content, MaxWidth = 1240 },
    };

    public static Border Pill(string text, Brush? bg = null, Brush? fg = null)
    {
        return new Border
        {
            Style = St("Pill"),
            Background = bg ?? B("Surface2"),
            Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = fg ?? B("Muted") },
        };
    }

    public static StackPanel Stack(params UIElement[] kids)
    {
        var sp = new StackPanel();
        foreach (var k in kids) sp.Children.Add(k);
        return sp;
    }

    public static StackPanel Row(double gap, params UIElement[] kids)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < kids.Length; i++)
        {
            if (i > 0 && kids[i] is FrameworkElement fe) fe.Margin = new Thickness(gap, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
            sp.Children.Add(kids[i]);
        }
        return sp;
    }

    public static T M<T>(this T el, double l, double t, double r, double b) where T : FrameworkElement
    { el.Margin = new Thickness(l, t, r, b); return el; }

    public static T M<T>(this T el, double all) where T : FrameworkElement
    { el.Margin = new Thickness(all); return el; }

    public static T Also<T>(this T el, Action<T> a) { a(el); return el; }

    public static Random Rng { get; } = new();

    public static List<T> Shuffle<T>(this IEnumerable<T> src)
    {
        var l = src.ToList();
        for (int i = l.Count - 1; i > 0; i--) { int j = Rng.Next(i + 1); (l[i], l[j]) = (l[j], l[i]); }
        return l;
    }
}
