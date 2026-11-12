using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Matsubakira.Services;

/// <summary>Страница, которую нужно обновить при показе.</summary>
public interface IPage { string Title { get; } void OnShown() { } }
/// <summary>Страница, обрабатывающая клавиатуру (1-4, пробел, Enter).</summary>
public interface IKeyTarget { bool HandleKey(Key key); }

public static class Shell
{
    public static MainView Main { get; set; } = null!;
    /// <summary>Телефонная раскладка: нижние вкладки вместо бокового меню, одна колонка, меньше отступы.</summary>
    public static bool Compact { get; set; } = OperatingSystem.IsAndroid() || Environment.GetCommandLineArgs().Contains("--phone");
    public static void Open(Control page) => Main.Push(page);
    /// <summary>Открыть страницу с анимацией «раскрытия» из карточки.</summary>
    public static void OpenFrom(Control source, Control page) => Main.ExpandFrom(source, page);
    public static void Back() => Main.Pop();
    public static void Go(string section) => Main.NavigateRoot(section);
    public static void Toast(string text, string? jp = null) => Main.ShowToast(text, jp);
    /// <summary>Вопрос «да/нет» поверх окна (замена MessageBox).</summary>
    public static Task<bool> Confirm(string text, string yes = "Да", string no = "Отмена") => Main.Confirm(text, yes, no);
}

/// <summary>Функции сглаживания для анимаций.</summary>
public static class Ease
{
    public static double Linear(double t) => t;
    public static double CubicOut(double t) => 1 - Math.Pow(1 - t, 3);
    public static double QuinticOut(double t) => 1 - Math.Pow(1 - t, 5);
    public static double QuadIn(double t) => t * t;
    public static double CubicInOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    public static double QuinticInOut(double t) => t < 0.5 ? 16 * Math.Pow(t, 5) : 1 - Math.Pow(-2 * t + 2, 5) / 2;
    public static double SineInOut(double t) => -(Math.Cos(Math.PI * t) - 1) / 2;
    public static Func<double, double> BackOut(double amp = 0.4) => t =>
    {
        double c1 = 1.70158 * amp, c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    };
}

/// <summary>
/// Небольшие анимации: только прозрачность и трансформации (дёшево, без перерасчёта разметки).
/// Свой лёгкий движок: таймер работает, только пока идёт хотя бы одна анимация, — в простое 0% CPU.
/// </summary>
public static class Anim
{
    sealed class Tween
    {
        public long Start; public int Delay, Ms; public double From, To; public Action<double> Set = null!;
        public Func<double, double> Ease = Services.Ease.CubicOut; public Action? Done; public bool Forever, AutoReverse;
    }

    static readonly Dictionary<object, Tween> Active = new();
    static DispatcherTimer? _timer;

    static long Now => Environment.TickCount64;

    /// <summary>Анимирует значение от from до to. key — «слот» (элемент+свойство): новая анимация в том же слоте заменяет старую.</summary>
    public static void Run(object key, double from, double to, int ms, Action<double> set, Func<double, double>? ease = null, int delay = 0, Action? done = null, bool forever = false, bool autoReverse = false)
    {
        var tw = new Tween { Start = Now, Delay = delay, Ms = Math.Max(1, ms), From = from, To = to, Set = set, Ease = ease ?? Ease.CubicOut, Done = done, Forever = forever, AutoReverse = autoReverse };
        Active[key] = tw;
        if (delay <= 0) set(from);
        EnsureTimer();
    }

    public static void Stop(object key) => Active.Remove(key);

    static void EnsureTimer()
    {
        if (_timer == null)
        {
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => Tick());
        }
        if (!_timer.IsEnabled) _timer.Start();
    }

    static void Tick()
    {
        long now = Now;
        List<(object, Tween)>? finished = null;
        foreach (var (key, tw) in Active.ToArray())
        {
            long el = now - tw.Start - tw.Delay;
            if (el < 0) continue;
            double k = (double)el / tw.Ms;
            if (tw.Forever)
            {
                double cyc = k % (tw.AutoReverse ? 2 : 1);
                if (tw.AutoReverse && cyc > 1) cyc = 2 - cyc;
                tw.Set(tw.From + (tw.To - tw.From) * tw.Ease(Math.Min(1, cyc)));
                continue;
            }
            if (k >= 1) { tw.Set(tw.To); (finished ??= new()).Add((key, tw)); continue; }
            tw.Set(tw.From + (tw.To - tw.From) * tw.Ease(k));
        }
        if (finished != null)
            foreach (var (key, tw) in finished)
            {
                if (Active.TryGetValue(key, out var cur) && cur == tw) Active.Remove(key);
                tw.Done?.Invoke();
            }
        if (Active.Count == 0) _timer?.Stop();
    }

    public static (ScaleTransform s, TranslateTransform t) Transforms(Visual el)
    {
        if (el.RenderTransform is TransformGroup g && g.Children.Count == 2 && g.Children[0] is ScaleTransform s0 && g.Children[1] is TranslateTransform t0)
            return (s0, t0);
        var s = new ScaleTransform(1, 1); var t = new TranslateTransform();
        el.RenderTransform = new TransformGroup { Children = { s, t } };
        el.RenderTransformOrigin = RelativePoint.Center;
        return (s, t);
    }

    public static void Opacity(Visual el, double from, double to, int ms, int delay = 0, Action? done = null, Func<double, double>? ease = null) =>
        Run((el, "op"), from, to, ms, v => el.Opacity = v, ease, delay, done);

    public static void FadeUp(Visual el, int delay = 0, double dy = 16, int ms = 380)
    {
        var (_, t) = Transforms(el);
        el.Opacity = 0;
        t.Y = dy;
        Run((el, "op"), 0, 1, ms, v => el.Opacity = v, null, delay);
        Run((t, "y"), dy, 0, ms, v => t.Y = v, null, delay);
    }

    /// <summary>Каскадное появление детей панели.</summary>
    public static void Stagger(Panel p, int step = 40, int start = 0, int max = 24)
    {
        int i = 0;
        foreach (var c in p.Children)
        {
            if (i < max) FadeUp(c, start + i * step, 14, 360);
            i++;
        }
    }

    public static void Pop(Visual el, double from = 0.85)
    {
        var (s, _) = Transforms(el);
        Run((s, "sc"), from, 1, 380, v => { s.ScaleX = v; s.ScaleY = v; }, Ease.BackOut());
    }

    public static void Shake(Visual el)
    {
        var (_, t) = Transforms(el);
        double[] xs = { 0, -10, 9, -7, 5, -3, 0 };
        Run((t, "x"), 0, 1, 380, k =>
        {
            double p = k * (xs.Length - 1); int i = Math.Min((int)p, xs.Length - 2);
            t.X = xs[i] + (xs[i + 1] - xs[i]) * (p - i);
        }, Ease.Linear);
    }

    /// <summary>«3D»-переворот карточки: сжатие по X, смена содержимого, разворот.</summary>
    public static void Flip(Visual el, Action midpoint)
    {
        var (s, _) = Transforms(el);
        Run((s, "sx"), 1, 0, 140, v => s.ScaleX = v, Ease.QuadIn, 0, () =>
        {
            midpoint();
            Run((s, "sx"), 0, 1, 220, v => s.ScaleX = v, Ease.BackOut());
        });
    }

    public static void Pulse(Visual el, bool on)
    {
        var (s, _) = Transforms(el);
        if (!on) { Stop((s, "sc")); Stop((el, "op")); s.ScaleX = s.ScaleY = 1; return; }
        Run((s, "sc"), 1, 1.9, 1300, v => { s.ScaleX = v; s.ScaleY = v; }, Ease.CubicOut, forever: true);
        Run((el, "op"), 0.7, 0, 1300, v => el.Opacity = v, Ease.Linear, forever: true);
    }

    public static void CountUp(TextBlock tb, int to, string suffix = "", int ms = 700) =>
        Run((tb, "count"), 0, to, ms, v => tb.Text = ((int)Math.Round(v)).ToString() + suffix);
}

/// <summary>Утилиты построения UI из кода — для генерируемых списков и сеток.</summary>
public static class U
{
    public static T R<T>(string key)
    {
        var app = Application.Current!;
        if (app.TryGetResource(key, app.ActualThemeVariant, out var v) && v is T t) return t;
        throw new KeyNotFoundException("Ресурс не найден: " + key);
    }
    public static Brush B(string key) => R<Brush>(key);

    public static TextBlock T(string text, string style = "Body", double? size = null, IBrush? fg = null, FontWeight? weight = null)
    {
        var tb = new TextBlock { Text = text };
        tb.Classes.Add(style == "Muted" ? "MutedText" : style);
        if (size is { } s) tb.FontSize = s;
        if (fg != null) tb.Foreground = fg;
        if (weight is { } w) tb.FontWeight = w;
        return tb;
    }

    public static TextBlock Jp(string text, double size, IBrush? fg = null, bool serif = false) => new()
    {
        Text = text, FontSize = size,
        FontFamily = R<FontFamily>(serif ? "JpSerif" : "JpFont"),
        Foreground = fg ?? B("Text"),
        TextWrapping = TextWrapping.Wrap,
    };

    public static Border Card(Control child, Thickness? pad = null)
    {
        var b = new Border { Child = child };
        b.Classes.Add("Card");
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
        if (style.Length > 0) b.Classes.Add(style);
        b.Click += (_, _) => click();
        return b;
    }

    public static Button SpeakBtn(Func<string> text, bool slow = false)
    {
        var b = new Button { Content = slow ? "" : "" };
        b.Classes.Add("IconButton");
        b.Tip(slow ? "Медленно" : "Произнести");
        b.Click += (_, _) => Speech.Say(text(), slow);
        return b;
    }

    public static Button SpeakBtnSmall(string text)
    {
        var b = new Button { Content = "", Width = 30, Height = 30, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        b.Classes.Add("IconButton");
        b.Tip("Произнести");
        b.Click += (_, _) => Speech.Say(text);
        return b;
    }

    public static T Tip<T>(this T el, object? tip) where T : Control { ToolTip.SetTip(el, tip); return el; }

    public static T Center<T>(this T el) where T : Control
    {
        el.HorizontalAlignment = HorizontalAlignment.Center;
        if (el is TextBlock tb) tb.TextAlignment = TextAlignment.Center;
        return el;
    }

    public static T CenterV<T>(this T el) where T : Control
    { el.VerticalAlignment = VerticalAlignment.Center; return el; }

    public static T Col<T>(this T el, int col, int span = 1) where T : Control
    { Grid.SetColumn(el, col); if (span > 1) Grid.SetColumnSpan(el, span); return el; }

    public static T RowAt<T>(this T el, int row) where T : Control
    { Grid.SetRow(el, row); return el; }

    public static Grid Cols(params string[] widths)
    {
        var g = new Grid();
        foreach (var w in widths)
            g.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = w == "auto" ? GridLength.Auto : w.EndsWith('*')
                    ? new GridLength(w.Length == 1 ? 1 : double.Parse(w[..^1], System.Globalization.CultureInfo.InvariantCulture), GridUnitType.Star)
                    : new GridLength(double.Parse(w, System.Globalization.CultureInfo.InvariantCulture)),
            });
        return g;
    }

    /// <summary>Подсказка про клавиатуру — на телефоне не нужна.</summary>
    public static T DesktopOnly<T>(this T el) where T : Control { if (Shell.Compact) el.IsVisible = false; return el; }

    /// <summary>Сетка карточек: на компьютере desktop колонок, на телефоне phone.</summary>
    public static int Columns(int desktop, int phone = 1) => Shell.Compact ? phone : desktop;

    /// <summary>
    /// «Основное + боковая панель» (колонки *, отступ, фикс.) — на телефоне друг под другом.
    /// starRows: обе части делят высоту пополам (для прокручиваемых списков).
    /// </summary>
    public static Grid Stackify(Grid g, bool starRows = false)
    {
        if (!Shell.Compact || g.ColumnDefinitions.Count < 2) return g;
        g.ColumnDefinitions.Clear();
        g.RowDefinitions = new RowDefinitions(starRows ? "*,*" : "auto,auto");
        foreach (var c in g.Children)
        {
            int col = Grid.GetColumn(c);
            if (col > 0) { Grid.SetColumn(c, 0); Grid.SetRow(c, 1); c.Margin = new Thickness(c.Margin.Left, c.Margin.Top + 16, c.Margin.Right, c.Margin.Bottom); }
        }
        return g;
    }

    /// <summary>Какая иконка у раздела: по названию страницы, иначе по иероглифу.</summary>
    static readonly Dictionary<string, string> HeaderIcons = new()
    {
        ["Путь обучения"] = "path", ["Кандзи"] = "kanji", ["Слова"] = "words", ["Грамматика"] = "grammar", ["Говорение"] = "speech",
        ["Диалоги"] = "dialogs", ["Словарь"] = "dict", ["Настройки"] = "settings", ["Тренажёры"] = "trainers", ["Тренажёры и игры"] = "trainers", ["Япония"] = "torii", ["本"] = "books", ["Разделы"] = "more", ["Ключи кандзи"] = "kanji", ["Чтение"] = "books",
        ["Книги и тексты"] = "books", ["Прописи"] = "write", ["Минимальные пары"] = "voice",
        ["Проверка микрофона"] = "mic", ["Прогресс"] = "trophy", ["Кана"] = "kana", ["Ещё"] = "more", ["Счётные слова"] = "target",
        ["話"] = "speech", ["対"] = "dialogs", ["道"] = "path", ["耳"] = "mic", ["書"] = "write", ["練"] = "trainers", ["数"] = "target",
        ["漢"] = "kanji", ["語"] = "words", ["文"] = "grammar", ["辞"] = "dict", ["読"] = "books", ["設"] = "settings", ["あ"] = "kana",
    };

    /// <summary>
    /// Шапка страницы: у каждого раздела свой цветной баннер — иконка, название, подзаголовок
    /// и крупный иероглиф-водяной знак в цвет раздела.
    /// </summary>
    public static Control Header(string jp, string title, string subtitle, Control? right = null)
    {
        string icon = HeaderIcons.TryGetValue(title, out var i1) ? i1 : HeaderIcons.TryGetValue(jp, out var i2) ? i2 : "sparkle";
        var (ca, cb) = Controls.Art.IconColors(icon);
        var g = Cols("auto", "*", "auto");
        g.Children.Add(Shell.Compact ? Controls.Art.Icon(icon, 42) : Controls.Art.Icon(icon, 58));
        var sp = Stack(T(title, "H1", Shell.Compact ? 22 : 28), T(subtitle, "Muted", Shell.Compact ? 12.5 : 13.5).Also(t => t.TextWrapping = TextWrapping.Wrap).M(0, 3, 0, 0));
        sp.VerticalAlignment = VerticalAlignment.Center; sp.Margin = new Thickness(18, 0, 12, 0);
        g.Children.Add(sp.Col(1));
        if (right != null)
        {
            // на телефоне элементы справа (переключатели уровня и т.п.) переносятся под заголовок
            if (Shell.Compact && right is StackPanel { Orientation: Orientation.Horizontal } rs)
            {
                var wp = new WrapPanel();
                foreach (var c in rs.Children.ToList()) { rs.Children.Remove(c); c.Margin = new Thickness(0, 0, 6, 6); wp.Children.Add(c); }
                right = wp;
            }
            if (Shell.Compact) { g.RowDefinitions = new RowDefinitions("auto,auto"); right.Margin = new Thickness(0, 12, 0, 0); right.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetColumnSpan(right, 3); g.Children.Add(right.RowAt(1)); }
            else { right.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(right.Col(2)); }
        }
        if (Shell.Compact) { g.Margin = new Thickness(0, 4, 0, 16); return g; }

        var layers = new Grid();
        layers.Children.Add(new TextBlock
        {
            Text = jp, FontFamily = R<FontFamily>("JpSerif"), FontSize = 150, FontWeight = FontWeight.Bold, Opacity = 0.07,
            Foreground = new SolidColorBrush(cb), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -40, right != null ? 260 : 30, -40), IsHitTestVisible = false,
        });
        layers.Children.Add(g.M(22, 18, 22, 18));
        return new Border
        {
            Margin = new Thickness(0, 4, 0, 22), CornerRadius = new CornerRadius(22), ClipToBounds = true,
            BorderBrush = B("Stroke"), BorderThickness = new Thickness(1), Child = layers,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x30, ca.R, ca.G, ca.B), 0),
                    new GradientStop(Color.FromArgb(0x10, cb.R, cb.G, cb.B), 0.55),
                    new GradientStop(Color.FromArgb(0x00, cb.R, cb.G, cb.B), 1),
                },
            },
        };
    }

    /// <summary>Переключатель-«тумблер» со строкой описания: вся строка кликабельна.</summary>
    public static Control Switch(string text, bool on, Action<bool> set, string? sub = null)
    {
        var knobT = new TranslateTransform(on ? 20 : 0, 0);
        var knob = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Brushes.White, RenderTransform = knobT, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0), BoxShadow = BoxShadows.Parse("0 1 3 0 #40000000") };
        var track = new Border { Width = 46, Height = 26, CornerRadius = new CornerRadius(13), Background = on ? B("Accent") : B("Surface3"), Child = knob, VerticalAlignment = VerticalAlignment.Center };
        var g = Cols("*", "auto");
        g.Children.Add(sub == null ? T(text, "Body", 14).Also(t => t.TextWrapping = TextWrapping.Wrap).CenterV() : Stack(T(text, "Body", 14), T(sub, "Muted", 12)).CenterV());
        g.Children.Add(track.M(16, 0, 0, 0).Col(1));
        var row = new Border { Child = g, Background = Brushes.Transparent, Padding = new Thickness(0, 7), Cursor = Cursors.Hand };
        bool state = on;
        row.PointerReleased += (_, _) =>
        {
            state = !state;
            track.Background = state ? B("Accent") : B("Surface3");
            Anim.Run((knobT, "x"), knobT.X, state ? 20 : 0, 200, v => knobT.X = v);
            set(state);
        };
        return row;
    }

    /// <summary>Стандартная прокручиваемая страница с отступами.</summary>
    public static ScrollViewer Page(Control content) => new()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        Content = new Border { Padding = Shell.Compact ? new Thickness(16, 8, 16, 24) : new Thickness(40, 12, 40, 40), Child = content, MaxWidth = 1240 },
    };

    public static Border Pill(string text, IBrush? bg = null, IBrush? fg = null)
    {
        var b = new Border
        {
            Background = bg ?? B("Surface2"),
            Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = fg ?? B("Muted") },
        };
        b.Classes.Add("Pill");
        return b;
    }

    public static StackPanel Stack(params Control[] kids)
    {
        var sp = new StackPanel();
        foreach (var k in kids) sp.Children.Add(k);
        return sp;
    }

    public static StackPanel Row(double gap, params Control[] kids)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < kids.Length; i++)
        {
            if (i > 0) kids[i].Margin = new Thickness(gap, kids[i].Margin.Top, kids[i].Margin.Right, kids[i].Margin.Bottom);
            if (Shell.Compact && kids[i] is TextBlock { TextWrapping: TextWrapping.Wrap } tw) tw.MaxWidth = Math.Min(double.IsNaN(tw.MaxWidth) ? 1e9 : tw.MaxWidth, 270);
            sp.Children.Add(kids[i]);
        }
        return sp;
    }

    public static T M<T>(this T el, double l, double t, double r, double b) where T : Control
    { el.Margin = new Thickness(l, t, r, b); return el; }

    public static T M<T>(this T el, double all) where T : Control
    { el.Margin = new Thickness(all); return el; }

    public static T Also<T>(this T el, Action<T> a) { a(el); return el; }

    /// <summary>Отцепить элемент от прежнего родителя — для полей, которые заново вставляются при перестройке страницы
    /// (иначе Avalonia бросает «already has a visual parent»).</summary>
    public static T Detach<T>(this T el) where T : Control
    {
        switch (el.Parent)
        {
            case Panel p: p.Children.Remove(el); break;
            case ContentControl c when ReferenceEquals(c.Content, el): c.Content = null; break;
            case Decorator d when ReferenceEquals(d.Child, el): d.Child = null; break;
            case ContentPresenter cp when ReferenceEquals(cp.Content, el): cp.Content = null; break;
        }
        return el;
    }

    /// <summary>Клик левой кнопкой (замена WPF MouseLeftButtonUp).</summary>
    public static T OnClick<T>(this T el, Action a) where T : Control
    {
        el.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) a(); };
        return el;
    }

    public static Random Rng { get; } = new();

    public static List<T> Shuffled<T>(this IEnumerable<T> src)
    {
        var l = src.ToList();
        for (int i = l.Count - 1; i > 0; i--) { int j = Rng.Next(i + 1); (l[i], l[j]) = (l[j], l[i]); }
        return l;
    }

    /// <summary>Цвет из строки «#RRGGBB».</summary>
    public static SolidColorBrush Hex(string hex) => new(Color.Parse(hex));
}

/// <summary>WPF-совместимые имена, чтобы код страниц переносился без переписывания.</summary>
public static class Cursors
{
    public static readonly Cursor Hand = new(StandardCursorType.Hand);
    public static readonly Cursor Arrow = new(StandardCursorType.Arrow);
}

public static class FontWeights
{
    public static readonly FontWeight Normal = FontWeight.Normal;
    public static readonly FontWeight SemiBold = FontWeight.SemiBold;
    public static readonly FontWeight Bold = FontWeight.Bold;
    public static readonly FontWeight Light = FontWeight.Light;
}

/// <summary>Геометрия точек (в Avalonia разность Point — тоже Point, без длины).</summary>
public static class Geo
{
    public static double Dist(Avalonia.Point a, Avalonia.Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
