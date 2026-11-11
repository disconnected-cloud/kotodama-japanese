using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Matsubakira.Services;
using Matsubakira.Views;
using RadioButton = Matsubakira.Compat.RadioButton;

namespace Matsubakira;

/// <summary>
/// Весь интерфейс приложения: меню, область страниц со стеком навигации, тосты, интро, Ctrl+K.
/// На компьютере живёт внутри окна MainWindow, на телефоне — показывается напрямую.
/// </summary>
public class MainView : UserControl
{
    readonly Dictionary<string, Control> _roots = new();
    readonly List<Control> _stack = new();
    readonly Grid _root = new();
    readonly ContentControl _host = new() { Focusable = false };
    readonly TranslateTransform _hostShift = new();
    readonly Border _sidebar = new();
    readonly ColumnDefinition _sideCol = new(new GridLength(232));
    readonly StackPanel _nav = new();
    readonly Border _navIndicator = new();
    readonly TranslateTransform _navShift = new();
    readonly Dictionary<string, RadioButton> _navItems = new();
    readonly Button _backBtn;
    readonly TextBlock _crumb = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _toast = new();
    readonly TextBlock _toastJp = new(), _toastText = new();
    readonly Grid _intro = new() { IsVisible = false };
    readonly TextBlock _streak = new(), _levelText = new(), _goalText = new();
    readonly ProgressBar _goalBar = new() { Height = 4 }, _xpBar = new() { Height = 4, Margin = new Thickness(0, 6, 0, 0) };
    readonly Border _dueBadge = new();
    readonly TextBlock _dueText = new();
    readonly Button? _maxBtn;
    static readonly bool CustomChrome = OperatingSystem.IsWindows() && !Shell.Compact;
    readonly Grid _tabs = new();
    readonly Dictionary<string, (TextBlock ico, TextBlock label)> _tabItems = new();
    string _rootKey = "home";

    /// <summary>Окно, в котором показан интерфейс (на телефоне — null).</summary>
    Window? Win => TopLevel.GetTopLevel(this) as Window;
    bool _opened;

    void ToggleMax() { if (Win is { } w) w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }

    public MainView()
    {
        Shell.Main = this;
        Background = U.B("Bg");
        _host.RenderTransform = _hostShift;

        // ── Фон: едва заметное свечение в углу
        var glow = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 900, Height = 700, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -460, -260, 0), IsHitTestVisible = false,
            Fill = new RadialGradientBrush { GradientStops = { new GradientStop(Color.Parse("#14F07AA0"), 0), new GradientStop(Color.Parse("#00F07AA0"), 1) } },
        };
        _root.Children.Add(glow);

        var layout = new Grid();
        layout.ColumnDefinitions.Add(_sideCol);
        layout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _sidebar.Child = BuildSidebar();
        _sidebar.BorderBrush = U.B("Stroke");
        _sidebar.BorderThickness = new Thickness(0, 0, 1, 0);
        layout.Children.Add(_sidebar);

        // ── Основная область: строка заголовка + страница
        var main = new Grid { RowDefinitions = new RowDefinitions(Shell.Compact ? "48,*,auto" : "44,*") };
        var bar = new Grid { Background = Brushes.Transparent };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _backBtn = new Button
        {
            Padding = new Thickness(8, 5), Margin = new Thickness(0, 0, 6, 0), IsVisible = false,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = "", FontFamily = U.R<FontFamily>("Icons"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = "Назад", Margin = new Thickness(8, 0, 0, 0), FontSize = 13, FontWeight = FontWeight.Normal },
                },
            },
        };
        _backBtn.Classes.Add("Ghost");
        _backBtn.Click += (_, _) => Pop();
        _crumb.Foreground = U.B("Faint");
        left.Children.Add(_backBtn); left.Children.Add(_crumb);
        if (Shell.Compact) { left.Margin = new Thickness(8, 0, 0, 0); _crumb.FontSize = 15; _crumb.FontWeight = FontWeight.SemiBold; _crumb.Margin = new Thickness(6, 0, 0, 0); }
        bar.Children.Add(left);
        if (CustomChrome)
        {
            var caps = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            Button Cap(string glyph, Action a, bool close = false)
            {
                var b = new Button { Content = glyph };
                b.Classes.Add("CaptionBtn"); if (close) b.Classes.Add("CloseBtn");
                b.Click += (_, _) => a();
                caps.Children.Add(b);
                return b;
            }
            Cap("", () => { if (Win is { } w) w.WindowState = WindowState.Minimized; });
            _maxBtn = Cap("", ToggleMax);
            Cap("", () => Win?.Close(), true);
            bar.Children.Add(caps);
            // Перетаскивание окна за строку заголовка, двойной клик — развернуть
            bar.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed) return;
                if (e.ClickCount == 2) ToggleMax();
                else Win?.BeginMoveDrag(e);
            };
        }
        main.Children.Add(bar);
        main.Children.Add(_host.RowAt(1));
        main.Children.Add(BuildToast().RowAt(1));
        if (Shell.Compact) main.Children.Add(BuildTabs().RowAt(2));
        layout.Children.Add(main.Col(1));
        _root.Children.Add(layout);

        _intro.Background = U.B("Bg");
        _root.Children.Add(_intro);
        // Масштаб интерфейса (настройка) — через LayoutTransformControl, разметка пересчитывается честно
        Content = Math.Abs(ProgressStore.P.UiScale - 1) > 0.01
            ? new LayoutTransformControl { LayoutTransform = new ScaleTransform(ProgressStore.P.UiScale, ProgressStore.P.UiScale), Child = _root }
            : _root;


        ProgressStore.Changed += () => Dispatcher.UIThread.Post(UpdateStats);
        AddHandler(KeyDownEvent, OnPreviewKey, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) =>
        {
            if (_opened) return;
            _opened = true;
            // системная кнопка «Назад» на телефоне — шаг назад по страницам
            if (TopLevel.GetTopLevel(this) is { } tl) tl.BackRequested += (_, e) => { if (_overlay != null) { CloseOverlay(); e.Handled = true; } else if (_stack.Count > 1) { Pop(); e.Handled = true; } };
            Dispatcher.UIThread.Post(OnOpened, DispatcherPriority.Loaded);
        };
    }

    /// <summary>Выход из программы (из меню трея).</summary>
#if MOBILE
    public void Quit() => Environment.Exit(0);
#else
    public void Quit() { if (Win is MainWindow mw) mw.Quit(); else Environment.Exit(0); }
#endif

    void OnOpened()
    {
        UpdateStats();
        Dispatcher.UIThread.Post(() => { if (Win is { } w) Badge.Update(w); }, DispatcherPriority.Background);
        Dispatcher.UIThread.Post(() => { if (!Environment.GetCommandLineArgs().Skip(1).Any()) WordOfDay.Tick(); }, DispatcherPriority.Background);
        _navItems["home"].IsChecked = true;
        PlayIntro();
        if (Win is { } win) Tray.Init(win);
        var args = Environment.GetCommandLineArgs();
        bool plain = !args.Skip(1).Any();
        if (!ProgressStore.P.Flags.Contains("onboarded") && plain) Push(new OnboardingPage());
        if (plain)
        {
            Updater.AutoCheck();
            if (CrashReport.HasNew()) { CrashReport.MarkSeen(); Shell.Toast("В прошлый раз была ошибка — отправить отчёт можно в Настройках → «Обратная связь»", "!"); }
        }
        var remind = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        remind.Tick += (_, _) => Tray.Tick();
        remind.Start();
        int pi = Array.IndexOf(args, "--page");
        if (pi > 0 && pi + 1 < args.Length) NavigateRoot(args[pi + 1]);
#if DEBUG
        int oi = Array.IndexOf(args, "--open");
        if (oi > 0 && oi + 1 < args.Length) DebugOpen(args[oi + 1]);
        int pt = Array.IndexOf(args, "--playtest");
        if (pt > 0 && pt + 1 < args.Length) DebugPlay(args[pt + 1]);
#if !MOBILE
        int li = Array.IndexOf(args, "--llmtest");
        if (li > 0 && li + 2 < args.Length) DebugLlm(args[li + 1], args[li + 2]);
#endif
        int bi = Array.IndexOf(args, "--asrbench");
        if (bi > 0 && bi + 2 < args.Length) DebugAsrBench(args[bi + 1], args[bi + 2]);
        int si = Array.IndexOf(args, "--snap");
        if (si > 0 && si + 1 < args.Length) DebugSnap(args[si + 1]);
#endif
    }

    // ─────────── Боковая панель ───────────
    Control BuildSidebar()
    {
        var dock = new DockPanel();
        var logo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 20, 0, 22) };
        logo.Children.Add(new TextBlock { Text = "言", FontFamily = U.R<FontFamily>("JpSerif"), FontSize = 24, Foreground = U.B("Accent"), VerticalAlignment = VerticalAlignment.Center });
        logo.Children.Add(new StackPanel
        {
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "Kotodama", FontSize = 15, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "日本語を学ぼう", FontFamily = U.R<FontFamily>("JpFont"), FontSize = 10.5, Foreground = U.B("Faint") },
            },
        });
        DockPanel.SetDock(logo, Dock.Top);
        dock.Children.Add(logo);

        // Сводка внизу: серия, уровень, цель дня
        _streak.FontWeight = FontWeight.SemiBold; _streak.FontSize = 13; _streak.Margin = new Thickness(8, 0, 0, 0); _streak.VerticalAlignment = VerticalAlignment.Center;
        _levelText.FontSize = 12; _levelText.Foreground = U.B("Muted"); _levelText.HorizontalAlignment = HorizontalAlignment.Right; _levelText.VerticalAlignment = VerticalAlignment.Center;
        _goalText.FontSize = 11.5; _goalText.Foreground = U.B("Faint"); _goalText.HorizontalAlignment = HorizontalAlignment.Right;
        _goalBar.Foreground = U.B("Gold");
        _xpBar.Tip("Прогресс до следующего уровня");
        var top = new Grid();
        top.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = "火", FontFamily = U.R<FontFamily>("JpSerif"), FontSize = 15, Foreground = U.B("Gold"), VerticalAlignment = VerticalAlignment.Center }, _streak } });
        top.Children.Add(_levelText);
        var goalRow = new Grid { Margin = new Thickness(0, 10, 0, 6) };
        goalRow.Children.Add(new TextBlock { Text = "Сегодня", FontSize = 11.5, Foreground = U.B("Faint") });
        goalRow.Children.Add(_goalText);
        var summary = new Border
        {
            Margin = new Thickness(14, 0, 14, 16), Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(12),
            Background = U.B("Surface"), BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { top, goalRow, _goalBar, _xpBar } },
        };
        DockPanel.SetDock(summary, Dock.Bottom);
        dock.Children.Add(summary);

        // Меню со скользящей подложкой выбранного пункта
        _navIndicator.VerticalAlignment = VerticalAlignment.Top; _navIndicator.Height = 40; _navIndicator.Margin = new Thickness(12, 0);
        _navIndicator.CornerRadius = new CornerRadius(10); _navIndicator.Background = U.B("SelectedTint"); _navIndicator.Opacity = 0; _navIndicator.IsHitTestVisible = false;
        _navIndicator.RenderTransform = _navShift;
        _navIndicator.Child = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(2), Background = U.B("Accent"), HorizontalAlignment = HorizontalAlignment.Left };
        void Item(string key, string icon, object content)
        {
            var rb = new RadioButton { Tag = Controls.Art.Icon(icon, 26), Content = content, GroupName = "nav" };
            rb.Classes.Add("NavItem");
            rb.Checked += (_, _) => { MoveNavIndicator(rb); ShowRoot(key); };
            _navItems[key] = rb;
            _nav.Children.Add(rb);
        }
        void Header(string t) => _nav.Children.Add(U.T(t, "Overline").M(28, 12, 0, 5));
        _dueText.FontSize = 11; _dueText.FontWeight = FontWeight.SemiBold; _dueText.Foreground = U.B("OnAccent");
        _dueBadge.HorizontalAlignment = HorizontalAlignment.Right; _dueBadge.Background = U.B("Accent"); _dueBadge.CornerRadius = new CornerRadius(8);
        _dueBadge.Padding = new Thickness(6, 0); _dueBadge.IsVisible = false; _dueBadge.Child = _dueText;
        Item("home", "home", "Главная");
        Item("path", "path", "Путь обучения");
        Item("review", "review", new Grid { Width = 128, Children = { new TextBlock { Text = "Повторение" }, _dueBadge } });
        Header("УЧИТЬ");
        Item("kana", "kana", "Азбука кана");
        Item("words", "words", "Слова");
        Item("kanji", "kanji", "Кандзи");
        Item("grammar", "grammar", "Грамматика");
        Header("ГОВОРИТЬ");
        Item("speech", "speech", "Говорение");
        Item("dialogs", "dialogs", "Диалоги");
#if !MOBILE
        Item("chat", "chat", "Разговор с ИИ");
#endif
        Header("ЧИТАТЬ И ИГРАТЬ");
        Item("reader", "books", "Книги и тексты");
        Item("dict", "dict", "Словарь");
        Item("trainers", "trainers", "Тренажёры и игры");
        Item("settings", "settings", "Настройки");

        var navGrid = new Grid { Children = { _navIndicator, _nav } };
        dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = navGrid });
        return dock;
    }

    /// <summary>Подложка выбранного пункта плавно «переезжает» к новому пункту.</summary>
    void MoveNavIndicator(Control item)
    {
        if (!item.IsArrangeValid || item.Bounds.Height == 0) { Dispatcher.UIThread.Post(() => MoveNavIndicator(item), DispatcherPriority.Loaded); return; }
        var p = item.TranslatePoint(new Point(0, 0), _nav);
        if (p == null) return;
        double y = p.Value.Y;
        bool first = _navIndicator.Opacity == 0;
        _navIndicator.Opacity = 1;
        if (first) _navShift.Y = y;
        else Anim.Run((_navShift, "y"), _navShift.Y, y, 320, v => _navShift.Y = v, Ease.QuinticOut);
    }

    Border BuildToast()
    {
        _toastJp.FontFamily = U.R<FontFamily>("JpSerif"); _toastJp.FontSize = 18; _toastJp.Foreground = U.B("Accent");
        _toastJp.VerticalAlignment = VerticalAlignment.Center; _toastJp.Margin = new Thickness(0, 0, 12, 0);
        _toastText.FontSize = 13.5; _toastText.VerticalAlignment = VerticalAlignment.Center; _toastText.TextWrapping = TextWrapping.Wrap; _toastText.MaxWidth = 640;
        _toast.HorizontalAlignment = HorizontalAlignment.Center; _toast.VerticalAlignment = VerticalAlignment.Bottom; _toast.Margin = new Thickness(0, 0, 0, 28);
        _toast.Background = U.B("Surface3"); _toast.BorderBrush = U.B("StrokeStrong"); _toast.BorderThickness = new Thickness(1);
        _toast.CornerRadius = new CornerRadius(12); _toast.Padding = new Thickness(18, 10); _toast.Opacity = 0; _toast.IsHitTestVisible = false;
        _toast.BoxShadow = BoxShadows.Parse("0 6 24 0 #59000000");
        _toast.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _toastJp, _toastText } };
        return _toast;
    }

    // ─────────── Телефон: нижние вкладки ───────────
    static readonly (string key, string glyph, string label)[] Tabs =
    {
        ("home", "家", "Главная"), ("path", "道", "Путь"), ("review", "復", "Повтор"), ("speech", "話", "Говорить"), ("more", "\u2630", "Ещё"),
    };

    Control BuildTabs()
    {
        _tabs.ColumnDefinitions = new ColumnDefinitions(string.Join(",", Tabs.Select(_ => "*")));
        _tabs.Background = U.B("Sidebar");
        for (int i = 0; i < Tabs.Length; i++)
        {
            var (key, glyph, label) = Tabs[i];
            var ico = new TextBlock { Text = glyph, FontFamily = U.R<FontFamily>(key == "more" ? "UiFont" : "JpSerif"), FontSize = 19, HorizontalAlignment = HorizontalAlignment.Center };
            var lab = new TextBlock { Text = label, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            var cell = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Height = 58 };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { ico, lab } };
            cell.Children.Add(stack);
            if (key == "review")
            {
                // число карточек к повторению — на вкладке
                var badge = new Border { Background = U.B("Accent"), CornerRadius = new CornerRadius(8), Padding = new Thickness(5, 0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(26, 6, 0, 0), IsVisible = false,
                    Child = new TextBlock { FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = U.B("OnAccent") } };
                _tabBadge = badge;
                cell.Children.Add(badge);
            }
            var k = key;
            cell.PointerReleased += (_, _) => { if (_rootKey == k && _stack.Count > 1) ShowRoot(k); else NavigateRoot(k); };
            _tabItems[key] = (ico, lab);
            _tabs.Children.Add(cell.Col(i));
        }
        HighlightTab();
        return new Border { BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(0, 1, 0, 0), Child = _tabs };
    }

    Border? _tabBadge;

    void HighlightTab()
    {
        string active = Tabs.Any(t => t.key == _rootKey) ? _rootKey : "more";
        foreach (var (k, (ico, lab)) in _tabItems)
        {
            var b = U.B(k == active ? "Accent" : "Muted");
            ico.Foreground = b; lab.Foreground = k == active ? U.B("Text") : U.B("Muted");
        }
    }

    // ─────────── Навигация ───────────
    static readonly Dictionary<string, Func<Control>> Factories = new()
    {
        ["home"] = () => new HomePage(),
        ["path"] = () => new PathPage(),
        ["dialogs"] = () => new DialogsPage(),
        ["dict"] = () => new DictPage(),
        ["kana"] = () => new KanaPage(),
        ["words"] = () => new WordsPage(),
        ["kanji"] = () => new KanjiPage(),
        ["grammar"] = () => new GrammarPage(),
        ["speech"] = () => new SpeechPage(),
        ["review"] = ReviewPage.Create,
        ["settings"] = () => new SettingsPage(),
        ["trainers"] = () => new TrainersPage(),
        ["reader"] = () => new ReaderPage(),
        ["more"] = () => new MorePage(),
#if !MOBILE
        ["chat"] = () => new ChatPage(),
#endif
    };

    public void NavigateRoot(string key)
    {
        if (key == "more" || (Shell.Compact && !_navItems.ContainsKey(key) && Factories.ContainsKey(key))) { ShowRoot(key); return; }
        if (!_navItems.TryGetValue(key, out var rb)) { key = "home"; rb = _navItems[key]; }
        if (rb.IsChecked == true) ShowRoot(key); else rb.IsChecked = true;
    }

    void ShowRoot(string key)
    {
        // Страница повторения всегда пересоздаётся — очередь зависит от времени
        if (key == "review" || !_roots.TryGetValue(key, out var page))
        {
            page = Factories[key]();
            if (key != "review") _roots[key] = page;
        }
        _rootKey = key;
        HighlightTab();
        _stack.Clear();
        _stack.Add(page);
        Show(page, 1);
    }

    public Control? PendingExpand;

    public void Push(Control page)
    {
        if (PendingExpand is { } src) { PendingExpand = null; ExpandFrom(src, page); return; }
        _stack.Add(page);
        Show(page, 1);
    }

    public void Pop()
    {
        if (_stack.Count <= 1) { NavigateRoot("home"); return; }
        _stack.RemoveAt(_stack.Count - 1);
        Show(_stack[^1], -1);
    }

    void Show(Control page, int dir)
    {
        _host.Content = null; // страница может быть закэширована — сначала отцепляем
        _host.Content = page;
        // Режим фокуса: во время упражнений боковое меню скрывается
        bool focus = page is Controls.QuizSession or Controls.FlashSession or BuilderSession or SpeechLessonPage or DialogPage or WritingPage or KanaRainGame or MemoryGame or SprintGame or OnboardingPage;
        _sideCol.Width = new GridLength(focus || Shell.Compact ? 0 : 232);
        _sidebar.IsVisible = !focus && !Shell.Compact;
        _tabs.IsVisible = Shell.Compact && !focus;
        _backBtn.IsVisible = _stack.Count > 1;
        _crumb.Text = Shell.Compact ? (_stack.Count > 1 ? (_stack[^1] as IPage)?.Title ?? "" : (_stack[^1] as IPage)?.Title ?? "Kotodama")
            : string.Join("  ›  ", _stack.OfType<IPage>().Select(p => p.Title));
        (page as IPage)?.OnShown();
        Anim.Opacity(_host, 0, 1, 220);
        Anim.Run((_hostShift, "y"), dir > 0 ? 10 : -10, 0, 380, v => _hostShift.Y = v, Ease.QuinticOut);
        UpdateStats();
    }

    void OnPreviewKey(object? sender, KeyEventArgs e)
    {
        if (_intro.IsVisible) { CloseIntro(fast: true); e.Handled = true; return; }
        if (_overlay != null && e.Key == Key.Escape) { CloseOverlay(); e.Handled = true; return; }
        if (e.Key == Key.K && e.KeyModifiers == KeyModifiers.Control) { OpenPalette(); e.Handled = true; return; }
        if (_palette != null) return;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focused is TextBox && e.Key is not (Key.Enter or Key.Escape)) return;
        if (_host.Content is IKeyTarget kt && kt.HandleKey(e.Key)) { e.Handled = true; return; }
        if (e.Key == Key.Escape && _stack.Count > 1) { Pop(); e.Handled = true; }
        else if (e.Key == Key.Back && _stack.Count > 1 && focused is not TextBox) { Pop(); e.Handled = true; }
    }

    // ─────────── Статистика в сайдбаре ───────────
    public void UpdateStats()
    {
        Achievements.CheckNew();
        var p = ProgressStore.P;
        _streak.Text = $"{p.Streak} {Plural(p.Streak, "день", "дня", "дней")}";
        int lvl = ProgressStore.Level;
        _levelText.Text = $"Ур. {lvl}";
        int from = ProgressStore.XpForLevel(lvl), to = ProgressStore.XpForLevel(lvl + 1);
        _xpBar.Maximum = Math.Max(1, to - from);
        _xpBar.Value = p.Xp - from;
        _goalBar.Maximum = Math.Max(1, p.DailyGoal);
        _goalBar.Value = Math.Min(p.DailyGoal, ProgressStore.TodayXp);
        _goalText.Text = $"{ProgressStore.TodayXp} / {p.DailyGoal} XP";
        int due = p.Cards.Count(kv => kv.Value.Due <= DateTime.Now);
        _dueBadge.IsVisible = due > 0;
        _dueText.Text = due > 99 ? "99+" : due.ToString();
        if (_tabBadge != null) { _tabBadge.IsVisible = due > 0; ((TextBlock)_tabBadge.Child!).Text = _dueText.Text; }
        if (Win is { IsVisible: true } bw) Badge.Update(bw);
    }

    public static string Plural(int n, string one, string few, string many)
    {
        n = Math.Abs(n) % 100;
        if (n is >= 11 and <= 14) return many;
        return (n % 10) switch { 1 => one, 2 or 3 or 4 => few, _ => many };
    }

    // ─────────── Тост ───────────
    public void ShowToast(string text, string? jp)
    {
        _toastText.Text = text;
        _toastJp.Text = jp ?? "";
        _toastJp.IsVisible = !string.IsNullOrEmpty(jp);
        // появление 0,2 с → 2,2 с на экране → исчезновение 0,4 с
        Anim.Run((_toast, "op"), 0, 1, 2800, k => _toast.Opacity = k < 0.07 ? k / 0.07 : k < 0.857 ? 1 : (1 - k) / 0.143, Ease.Linear);
        Anim.Pop(_toast, 0.9);
    }

    /// <summary>Лепестки сакуры отключены в кроссплатформенной версии (дорогой декор); настройка сохраняется.</summary>
    public void SetPetals(bool on) { }

    // ─────────── «Раскрытие» карточки в страницу ───────────
    bool _expanding;
    public void ExpandFrom(Control source, Control page)
    {
        if (_expanding || !source.IsEffectivelyVisible) { Push(page); return; }
        var a = source.TranslatePoint(new Point(0, 0), _root);
        var b = _host.TranslatePoint(new Point(0, 0), _root);
        if (a == null || b == null || _host.Bounds.Width < 1) { Push(page); return; }
        _expanding = true;
        Rect from = new(a.Value, source.Bounds.Size), to = new(b.Value, _host.Bounds.Size);
        var ghost = new Border
        {
            Width = to.Width, Height = to.Height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(to.X, to.Y, 0, 0), Background = U.B("Surface"), BorderBrush = U.B("StrokeStrong"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), IsHitTestVisible = false, RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute),
        };
        var sc = new ScaleTransform(from.Width / to.Width, from.Height / to.Height);
        var tr = new TranslateTransform(from.X - to.X, from.Y - to.Y);
        ghost.RenderTransform = new TransformGroup { Children = { sc, tr } };
        _root.Children.Add(ghost);
        double sx = sc.ScaleX, sy = sc.ScaleY, tx = tr.X, ty = tr.Y;
        Anim.Run((ghost, "expand"), 0, 1, 300, k =>
        {
            sc.ScaleX = sx + (1 - sx) * k; sc.ScaleY = sy + (1 - sy) * k;
            tr.X = tx * (1 - k); tr.Y = ty * (1 - k);
        }, Ease.QuinticInOut, 0, () =>
        {
            Push(page);
            Anim.Opacity(ghost, 1, 0, 220, 0, () => { _root.Children.Remove(ghost); _expanding = false; });
        });
    }

    // ─────────── Карточка поверх окна (слово дня и т.п.) ───────────
    Grid? _overlay;
    public void ShowOverlay(Control content)
    {
        CloseOverlay();
        var overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)) };
        var close = new Button { Content = "", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Classes = { "IconButton" } };
        close.Click += (_, _) => CloseOverlay();
        var card = new Border
        {
            Classes = { "Card" }, MinWidth = 440, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(30, 22), BoxShadow = BoxShadows.Parse("0 6 24 0 #59000000"),
            Child = new Grid { Children = { content, close } },
        };
        overlay.Children.Add(card);
        overlay.PointerPressed += (_, e) => { if (e.Source == overlay) CloseOverlay(); };
        _overlay = overlay;
        _root.Children.Add(overlay);
        Anim.FadeUp(card, 0, 12, 260);
    }

    public void CloseOverlay() { if (_overlay != null) { _root.Children.Remove(_overlay); _overlay = null; } }

    // ─────────── Диалог «да/нет» ───────────
    public Task<bool> Confirm(string text, string yes, string no)
    {
        var tcs = new TaskCompletionSource<bool>();
        var overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)) };
        void Done(bool r) { _root.Children.Remove(overlay); tcs.TrySetResult(r); }
        var card = new Border
        {
            Classes = { "Card" }, Width = 460, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(26, 22),
            BoxShadow = BoxShadows.Parse("0 6 24 0 #59000000"),
            Child = U.Stack(
                U.T("Kotodama", "Overline"),
                U.T(text, "Body", 15).M(0, 8, 0, 20),
                U.Row(10, U.Btn(yes, () => Done(true), "Primary"), U.Btn(no, () => Done(false))).Also(r => r.HorizontalAlignment = HorizontalAlignment.Right)),
        };
        overlay.Children.Add(card);
        overlay.PointerPressed += (_, e) => { if (e.Source == overlay) Done(false); };
        overlay.KeyDown += (_, e) => { if (e.Key == Key.Escape) Done(false); else if (e.Key == Key.Enter) Done(true); };
        _root.Children.Add(overlay);
        Anim.FadeUp(card, 0, 10, 220);
        return tcs.Task;
    }

    // ─────────── Ctrl+K ───────────
    Controls.Palette? _palette;
    public void OpenPalette()
    {
        if (_palette != null) return;
        _palette = new Controls.Palette(() => { _root.Children.Remove(_palette!); _palette = null; });
        _root.Children.Add(_palette);
    }

    // ─────────── Интро ───────────
    bool _introClosing;

    /// <summary>Черты 言霊 прорисовываются кистью, затем появляется название, и экран растворяется (~3 с, пропуск — клик/клавиша).</summary>
    void PlayIntro()
    {
        var cl = Environment.GetCommandLineArgs();
        if (!cl.Contains("--intro") && (!ProgressStore.P.Intro || cl.Any(a => a.StartsWith("--")))) return;
        _intro.IsVisible = true;
        _intro.Opacity = 1;
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var s1 = new Controls.StrokeView("言", 150, plain: true, ink: U.B("Accent"), thick: 4.4);
        var s2 = new Controls.StrokeView("霊", 150, plain: true, ink: U.B("Text"), thick: 4.4) { Margin = new Thickness(14, 0, 0, 0) };
        row.Children.Add(s1); row.Children.Add(s2);
        stack.Children.Add(row);
        var title = new TextBlock { Text = "Kotodama", FontSize = 30, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0), Opacity = 0 };
        var sub = new TextBlock { Text = "言霊 — душа слова · японский шаг за шагом", FontSize = 14, Foreground = U.B("Muted"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), Opacity = 0 };
        var line = new Border { Height = 2, Width = 0, CornerRadius = new CornerRadius(1), Background = U.B("Accent"), Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(title); stack.Children.Add(sub); stack.Children.Add(line);
        _intro.Children.Clear();
        _intro.Children.Add(stack);
        _intro.PointerPressed += (_, _) => CloseIntro(fast: true);

        double end = Math.Max(s1.Play(2.6, 250), s2.Play(3.2, 550));
        Anim.FadeUp(title, (int)Math.Max(0, end - 350), 14, 520);
        Anim.FadeUp(sub, (int)Math.Max(0, end - 180), 10, 520);
        Anim.Run((line, "w"), 0, 120, 600, v => line.Width = v, Ease.QuinticOut, (int)end);
        DispatcherTimer.RunOnce(() => CloseIntro(fast: false), TimeSpan.FromMilliseconds(end + 900));
    }

    void CloseIntro(bool fast)
    {
        if (_introClosing || !_intro.IsVisible) return;
        _introClosing = true;
        if (_intro.Children.Count > 0 && _intro.Children[0] is Control st)
        {
            var (s, _) = Anim.Transforms(st);
            Anim.Run((s, "sc"), 1, 1.05, 520, v => { s.ScaleX = v; s.ScaleY = v; });
        }
        Anim.Opacity(_intro, 1, 0, fast ? 220 : 520, 0, () => { _intro.IsVisible = false; _intro.Children.Clear(); }, Ease.CubicInOut);
        Anim.Opacity(_host, 0, 1, 500);
    }

    // ─────────── Окно ───────────
    public void OnStateChanged(WindowState state)
    {
        // Развёрнутое окно без рамки выходит за край экрана на толщину рамки — компенсируем
        if (CustomChrome) _root.Margin = state == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        if (_maxBtn != null) _maxBtn.Content = state == WindowState.Maximized ? "" : "";
    }

#if DEBUG
    // Рендер собственного окна в PNG (без захвата экрана) и выход — для визуальной проверки
    void DebugSnap(string path)
    {
        DispatcherTimer.RunOnce(() =>
        {
            var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
            using var rtb = new RenderTargetBitmap(size);
            rtb.Render(this);
            rtb.Save(path);
            if (Win is { } w) w.Close(); else Environment.Exit(0);
        }, TimeSpan.FromSeconds(double.TryParse(Environment.GetEnvironmentVariable("KOTODAMA_SNAP_DELAY"), out var sd) ? sd : 2.5));
    }

    /// <summary>Сравнение моделей распознавания на фразах уроков (озвучка из кэша): время и баллы обоих проходов.</summary>
    async void DebugAsrBench(string model, string outPath)
    {
        var log = new List<string>();
        void L(string x) { log.Add(x); System.IO.File.WriteAllLines(outPath, log); }
        Asr.OverridePath = model;
        await JDict.LoadAsync();
        var items = Db.SpeechLessons.SelectMany(l => l.Items.Select(i => (i.Jp, i.KanaPlain))).Distinct().ToList();
        var sw = new System.Diagnostics.Stopwatch();
        long t1 = 0, tAll = 0; int n = 0, fails = 0, second = 0; long sum = 0;
        foreach (var (jp, kana) in items)
        {
            var mp3 = await Tts.GetAsync(jp);
            if (mp3 == null) continue;
            var (x, rate) = Platform.Pcm.Decode(mp3);
            var pcm = Platform.Pcm.ToPcm16(Platform.Pcm.Resample(x, rate, 16000));
            var rec = new Recording(Recorder.ToWav(pcm), true, Array.Empty<float>(), pcm.Length / 32000.0, 0.5f, 0.001f);
            sw.Restart();
            var r1 = await Asr.TranscribeDetailedAsync(rec.Wav, CancellationToken.None);
            t1 += sw.ElapsedMilliseconds;
            var s1 = Speech.Score(r1.Text, r1.Confidence, jp, kana);
            sw.Restart();
            var sc = await Views.SpeechUi.ScoreRecording(jp, kana, rec, CancellationToken.None);
            tAll += sw.ElapsedMilliseconds;
            if (s1.Score < 88) second++;
            n++; sum += sc.Score;
            if (sc.Score < 70) fails++;
            if (s1.Score < 80) L($"  {s1.Score,3}->{sc.Score,3} {jp} | heard '{r1.Text}' low: {string.Join(" ", r1.Tokens.Where(t => t.p < 0.5).Select(t => $"{t.text}:{t.p:0.00}"))}");
        }
        L($"MODEL {System.IO.Path.GetFileName(model)}: n={n} avg={sum / Math.Max(1, n)} fails={fails} pass1={t1 / Math.Max(1, n)}ms full={tAll / Math.Max(1, n)}ms second-pass={second}");
        Environment.Exit(0);
    }

#if !MOBILE
    /// <summary>Проверка языковой модели: диалог, исправление, перевод — с замером времени.</summary>
    async void DebugLlm(string model, string outPath)
    {
        var log = new List<string>();
        void L(string x) { log.Add(x); System.IO.File.WriteAllLines(outPath, log); }
        try
        {
            Llm.OverridePath = model;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var cafe = Views.ChatPrompts.Scenes.First(x => x.Key == "cafe");
            var hist = new List<(string, string)> { ("system", Views.ChatPrompts.System(cafe, "N5")), ("user", Views.ChatPrompts.Opener(cafe)) };
            var r1 = await Llm.AskAsync(hist, 140, 0.7f);
            L($"load+first: {sw.ElapsedMilliseconds} ms :: {r1}");
            hist[^1] = ("assistant", r1);
            hist.Add(("user", "コーヒーをください。ミルクはいらないです。"));
            sw.Restart();
            var r2 = await Llm.AskAsync(hist, 140, 0.7f);
            L($"reply2: {sw.ElapsedMilliseconds} ms :: {r2}");
            foreach (var t in new[] { "わたしはきのう学校に行きます。", "コーヒーをください。", "わたしは日本語を勉強しますでした。" })
            {
                sw.Restart();
                L($"correct '{t}': {sw.ElapsedMilliseconds} ms :: {await Llm.AskAsync(Views.ChatPrompts.Correct(t), 80, 0.1f)}");
            }
            sw.Restart();
            L($"translate: {await Llm.AskAsync(Views.ChatPrompts.Translate(r2), 120, 0.2f)} ({sw.ElapsedMilliseconds} ms)");
            sw.Restart();
            L($"hint: {await Llm.AskAsync(Views.ChatPrompts.Hint(cafe, "N5", r2), 60, 0.5f)} ({sw.ElapsedMilliseconds} ms)");
        }
        catch (Exception ex) { L("ERROR " + ex); }
        Environment.Exit(0);
    }

    /// <summary>Беззвучная проверка воспроизведения: озвучка обычная и медленная, своя запись, звуки интерфейса.</summary>
    async void DebugPlay(string outPath)
    {
        var log = new List<string>();
        void L(string x) { log.Add(x); System.IO.File.WriteAllLines(outPath, log); }
        Platform.AudioPlayer.Muted = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        L($"say normal: {await Speech.SayAsync("こんにちは")} {sw.ElapsedMilliseconds} ms"); sw.Restart();
        L($"say slow: {await Speech.SayAsync("こんにちは", true)} {sw.ElapsedMilliseconds} ms"); sw.Restart();
        var wav = Recorder.ToWav(Platform.Pcm.ToPcm16(Enumerable.Range(0, 8000).Select(i => (float)Math.Sin(i * 0.1) * 0.2f).ToArray()));
        L($"play wav: {await Speech.PlayWavAsync(wav)} {sw.ElapsedMilliseconds} ms");
        L($"mic available: {Recorder.HasMicrophone} devices: {string.Join(", ", Recorder.Devices())}");
        Environment.Exit(0);
    }

#endif

    void DebugOpen(string what)
    {
        switch (what)
        {
            case "flash": Push(new Controls.FlashSession("Демо", Db.Words.Take(5).Select(Controls.FlashSession.FromWord))); break;
            case "kanjiflash": Push(new Controls.FlashSession("Демо", Db.Kanji.Take(5).Select(Controls.FlashSession.FromKanji))); break;
            case "grammar": Push(new GrammarLessonPage(Db.Grammar[4])); break;
            case "grammar1": Push(new GrammarLessonPage(Db.Grammar[0])); break;
            case "onboard": Push(new OnboardingPage()); break;
            case "speech": Push(new SpeechLessonPage(Db.SpeechLessons[1])); break;
            case "dialog": Push(new DialogPage(Db.Dialogs[1])); break;
            case "unit": Push(new UnitPage(Db.Course[0])); break;
            case "reader": Push(new ReaderPage()); break;
            case "writing": Push(new WritingPage()); break;
            case "mic": Push(new MicCheckPage()); break;
            case "pairs": Push(new PairsPage()); break;
            case "art": Push(new ArtDemo()); break;
            case "learn": Push(Controls.LearnSession.Kanji(Db.Kanji.Take(5))); break;
            case "learnw": Push(Controls.LearnSession.Words("Демо", Db.Words.Take(5)).Skip(1)); break;
            case "learn1": Push(Controls.LearnSession.Kanji(Db.Kanji.Skip(20).Take(5)).Skip(2)); break;
            case "learnq": Push(Controls.LearnSession.Words("Демо", Db.Words.Take(5)).Skip(7)); break;
#if !MOBILE
            case "chat": if (Environment.GetEnvironmentVariable("KOTODAMA_LLM") is { } lp) Llm.OverridePath = lp; NavigateRoot("chat"); break;
#endif
            case "wod": WordOfDay.Show(); break;
            case "quiz":
                var w = Db.Words.Take(8).ToArray();
                Push(new Controls.QuizSession("Демо", w.Select(x => new Controls.QuizQ { Prompt = x.Jp, Sub = x.Kana, Answer = x.Ru, Options = Controls.QuizSession.MakeOptions(x.Ru, w.Select(y => y.Ru)) })));
                break;
        }
    }
#endif
}

#if DEBUG
/// <summary>Витрина собственной графики (только отладка).</summary>
public sealed class ArtDemo : UserControl, IPage
{
    public string Title => "Графика";
    public void OnShown()
    {
        var root = new StackPanel();
        var row = new WrapPanel();
        foreach (Controls.Mood m in Enum.GetValues<Controls.Mood>()) row.Children.Add(Controls.Art.Kotoha(220, m).M(0, 0, 16, 16));
        root.Children.Add(row);
        var row2 = new WrapPanel();
        foreach (Controls.Mood m in Enum.GetValues<Controls.Mood>()) row2.Children.Add(Controls.Art.Tama(110, m).M(0, 0, 16, 16));
        root.Children.Add(row2);
        var icons = new WrapPanel();
        foreach (var n in new[] { "home","path","kana","words","kanji","grammar","speech","dialogs","chat","reader","books","dict","review","trainers","settings","more","phrases","voice","mic","palette","bell","target","shield","download","trophy","info","flame","games","write","storage","sparkle" })
            icons.Children.Add(Controls.Art.Icon(n, 48).M(0, 0, 12, 12));
        root.Children.Add(icons);
        root.Children.Add(Controls.Art.Say("Привет! Я Котоха. Давай учить японский вместе!", Controls.Mood.Joy));
        Content = U.Page(root);
    }
}
#endif
