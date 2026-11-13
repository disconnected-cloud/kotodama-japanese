using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Matsubakira.Services;

/// <summary>Страница, которую нужно обновить при показе.</summary>
public interface IPage { string Title { get; } void OnShown() { } }
/// <summary>Страница, обрабатывающая клавиатуру (1-4, пробел, Enter).</summary>
public interface IKeyTarget { bool HandleKey(Key key); }

public static class Shell
{
    /// <summary>Отладочная проверка (--snap, --cliptest…): без трея, значка в панели задач и уведомлений.</summary>
    public static bool TestRun;
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

/// <summary>Утилиты построения UI из кода — для генерируемых списков и сеток.</summary>
public static class U
{
    // Ресурсы темы меняются только перезапуском (смена темы, акцента, шрифта просит перезапуск),
    // поэтому найденное значение кэшируем: поиск по словарям темы шёл тысячи раз при построении длинных страниц.
    static readonly Dictionary<string, object> _res = new();

    public static T R<T>(string key)
    {
        if (_res.TryGetValue(key, out var c) && c is T ct) return ct;
        var app = Application.Current!;
        if (app.TryGetResource(key, app.ActualThemeVariant, out var v) && v is T t) { _res[key] = t; return t; }
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
        Text = text, FontSize = size * ProgressStore.P.JpScale,
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
            // на телефоне подпись кнопки может переноситься (в горизонтальном StackPanel длинная уходила за край)
            // подпись может переноситься, если кнопке не хватает ширины (в StackPanel длинная уходила за край)
            var sp = new Grid { ColumnDefinitions = new ColumnDefinitions("auto,*") };
            sp.Children.Add(new TextBlock { Text = icon, FontFamily = R<FontFamily>("Icons"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, text.Length > 0 ? 8 : 0, 0) });
            if (text.Length > 0) sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }.Col(1));
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
        var g = Shell.Compact ? new AdaptiveGrid() : new Grid();
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
        if (g is AdaptiveGrid ag) ag.Stacked = true;
        // каждая занятая колонка — своя строка (пустые колонки-отступы пропускаем)
        var cols = g.Children.Select(Grid.GetColumn).Distinct().OrderBy(c => c).ToList();
        g.ColumnDefinitions.Clear();
        g.RowDefinitions = new RowDefinitions(string.Join(",", cols.Select(_ => starRows ? "*" : "auto")));
        foreach (var c in g.Children)
        {
            int row = cols.IndexOf(Grid.GetColumn(c));
            Grid.SetColumn(c, 0); Grid.SetRow(c, row);
            if (row > 0) c.Margin = new Thickness(c.Margin.Left, c.Margin.Top + 16, c.Margin.Right, c.Margin.Bottom);
        }
        return g;
    }

    /// <summary>Какая иконка у раздела: по названию страницы, иначе по иероглифу.</summary>
    static readonly Dictionary<string, string> HeaderIcons = new()
    {
        ["Путь обучения"] = "path", ["Кандзи"] = "kanji", ["Слова"] = "words", ["Грамматика"] = "grammar", ["Говорение"] = "speech",
        ["Диалоги"] = "dialogs", ["Словарь"] = "dict", ["Настройки"] = "settings", ["Тренажёры"] = "trainers", ["Тренажёры и игры"] = "trainers", ["Япония"] = "torii", ["本"] = "books", ["Разделы"] = "more", ["Ключи кандзи"] = "kanji", ["Чтение"] = "books",
        ["Книги и тексты"] = "books", ["Прописи"] = "write", ["Минимальные пары"] = "voice",
        ["Проверка микрофона"] = "mic", ["Прогресс"] = "trophy", ["Кана"] = "kana", ["Ещё"] = "more", ["Счётные слова"] = "target", ["Набор на японском"] = "keyboard", ["Числа и время"] = "clock", ["Аудиоплеер"] = "headphones",
        ["話"] = "speech", ["対"] = "dialogs", ["道"] = "path", ["耳"] = "mic", ["書"] = "write", ["練"] = "trainers", ["数"] = "target",
        ["漢"] = "kanji", ["語"] = "words", ["文"] = "grammar", ["辞"] = "dict", ["読"] = "books", ["設"] = "settings", ["あ"] = "kana",
    };

    /// <summary>
    /// Шапка страницы: у каждого раздела свой цветной баннер — иконка, название, подзаголовок
    /// и крупный иероглиф-водяной знак в цвет раздела.
    /// </summary>
    public static Control Header(string jp, string title, string subtitle, Control? right = null, string? art = null)
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
            // много переключателей (больше четырёх) — и на компьютере под заголовок: справа они сжимали описание
            bool below = Shell.Compact || right is Panel { Children.Count: > 4 };
            if (below) { g.RowDefinitions = new RowDefinitions("auto,auto"); right.Margin = new Thickness(0, 12, 0, 0); right.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetColumnSpan(right, 3); g.Children.Add(right.RowAt(1)); }
            else { right.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(right.Col(2)); }
        }
        if (Shell.Compact) { g.Margin = new Thickness(0, 4, 0, 16); return g; }

        var layers = new Grid();
        layers.Children.Add(new TextBlock
        {
            Text = jp, FontFamily = R<FontFamily>("JpSerif"), FontSize = 150, FontWeight = FontWeight.Bold, Opacity = 0.07,
            Foreground = new SolidColorBrush(cb), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -40, right != null ? 260 : art != null ? 190 : 30, -40), IsHitTestVisible = false,
        });
        // Википе-тан выглядывает из баннера раздела (обрезается по нижнему краю)
        if (art != null && right == null)
        {
            var pic = Controls.Art.Anime(art, 190);
            pic.HorizontalAlignment = HorizontalAlignment.Right; pic.VerticalAlignment = VerticalAlignment.Top;
            pic.Margin = new Thickness(0, 10, 34, -90); pic.IsHitTestVisible = false;
            layers.Children.Add(pic);
            Anim.FadeUp(pic, 120, 18, 520);
        }
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

    /// <summary>Строка-превью (первая фраза текста и т.п.): на компьютере одна строка с «…», на телефоне до двух строк.</summary>
    public static void Preview(TextBlock t)
    {
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        t.TextWrapping = Shell.Compact ? TextWrapping.Wrap : TextWrapping.NoWrap;
        if (Shell.Compact) t.MaxLines = 2;
    }

    /// <summary>
    /// Длинная страница без задержки открытия: то, что ниже первого экрана, строится и добавляется уже после
    /// первого кадра, по нескольку элементов за раз (на слабом телефоне 40 карточек юнитов — это секунда стоп-кадра).
    /// Если страница успела закрыться или перестроиться, недостроенный хвост отбрасывается.
    /// </summary>
    public static void Deferred(Panel host, IEnumerable<Func<Control>> rest, int perBatch = 4, Func<bool>? alive = null)
    {
        var e = rest.GetEnumerator();
        bool seen = false;
        void Step()
        {
            if (host.GetVisualRoot() == null)
            {
                // ещё не показана — достроим, когда появится; уже убрана — бросаем
                if (!seen) { seen = true; host.AttachedToVisualTree += Once; }
                return;
            }
            seen = true;
            if (alive != null && !alive()) return; // панель переиспользована (список перерисован заново)
            for (int i = 0; i < perBatch; i++)
            {
                if (!e.MoveNext()) return;
                host.Children.Add(e.Current());
            }
            Dispatcher.UIThread.Post(Step, DispatcherPriority.Background);
        }
        void Once(object? s, VisualTreeAttachmentEventArgs a) { host.AttachedToVisualTree -= Once; Dispatcher.UIThread.Post(Step, DispatcherPriority.Background); }
        // первый шаг — после того как страница показана (Loaded), иначе панель ещё не в дереве
        Dispatcher.UIThread.Post(() => Dispatcher.UIThread.Post(Step, DispatcherPriority.Background), DispatcherPriority.Loaded);
    }

    /// <summary>Уже построенные элементы панели после первых keep — в разметку после первого кадра.</summary>
    public static void DeferTail(Panel host, int keep)
    {
        if (host.Children.Count <= keep) return;
        var tail = host.Children.Skip(keep).ToList();
        host.Children.RemoveRange(keep, tail.Count);
        Deferred(host, tail.Select(c => (Func<Control>)(() => c)), 1);
    }

    /// <summary>
    /// Элементы в ряд. На телефоне — с переносом: что не помещается по ширине, уходит на следующую строку,
    /// а длинный текст переносится внутри (горизонтальный StackPanel даёт детям бесконечную ширину — текст уходил за край).
    /// </summary>
    public static Panel Row(double gap, params Control[] kids)
    {
        Panel sp = Shell.Compact ? new WrapPanel { Orientation = Orientation.Horizontal } : new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < kids.Length; i++)
        {
            // на телефоне отступ справа и снизу: перенесённый на новую строку элемент не съезжает вправо
            if (Shell.Compact) { if (i < kids.Length - 1) kids[i].Margin = new Thickness(kids[i].Margin.Left, kids[i].Margin.Top, kids[i].Margin.Right + gap, kids[i].Margin.Bottom); }
            else if (i > 0) kids[i].Margin = new Thickness(gap, kids[i].Margin.Top, kids[i].Margin.Right, kids[i].Margin.Bottom);
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
