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
public partial class MainView : UserControl
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
    TextBlock? _brand;
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
    readonly Dictionary<string, TabItem> _tabItems = new();
    string _rootKey = "home";

    /// <summary>Окно, в котором показан интерфейс (на телефоне — null).</summary>
    Window? Win => TopLevel.GetTopLevel(this) as Window;
    bool _opened;

    void ToggleMax() { if (Win is { } w) w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }

    public MainView()
    {
        Perf.Mark("построение интерфейса");
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
        // телефон: с самого первого кадра без бокового меню (иначе до первой страницы мелькала раскладка компьютера)
        if (Shell.Compact) { _sideCol.Width = new GridLength(0); _sidebar.IsVisible = false; }

        // ── Основная область: строка заголовка + страница
        var main = new Grid { RowDefinitions = new RowDefinitions(Shell.Compact ? "56,*,auto" : "44,*") };
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
        if (Shell.Compact)
        {
            // телефон: верхняя панель как в Android — круглая стрелка «назад» и крупный заголовок экрана
            left.Margin = new Thickness(6, 0, 0, 0);
            _crumb.FontSize = 20; _crumb.FontWeight = FontWeight.SemiBold; _crumb.Foreground = U.B("Text"); _crumb.Margin = new Thickness(10, 0, 0, 0);
            _crumb.TextTrimming = TextTrimming.CharacterEllipsis; _crumb.MaxWidth = 300;
            // узкий экран (320 dp): длинный заголовок — с многоточием, а не за краем
            SizeChanged += (_, e) => _crumb.MaxWidth = Math.Clamp(e.NewSize.Width - 130, 120, 600);
            _brand = new TextBlock { Text = "言霊", FontFamily = U.R<FontFamily>("JpSerif"), FontSize = 22, Foreground = U.B("Accent"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            left.Children.Insert(0, _brand);
            _backBtn.Content = Controls.Art.Glyph("back", 22, U.B("Text"));
            _backBtn.Width = _backBtn.Height = 44; _backBtn.Padding = new Thickness(0); _backBtn.Margin = new Thickness(0);
            _backBtn.CornerRadius = new CornerRadius(22);
            _backBtn.HorizontalContentAlignment = HorizontalAlignment.Center; _backBtn.VerticalContentAlignment = VerticalAlignment.Center;
        }
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
        double uiScale = ProgressStore.P.UiScale;
#if DEBUG
        // масштаб для снимков в высоком разрешении (видео-обзор): KOTODAMA_UISCALE=1.2
        if (double.TryParse(Environment.GetEnvironmentVariable("KOTODAMA_UISCALE"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var us)) uiScale = us;
#endif
        Content = Math.Abs(uiScale - 1) > 0.01
            ? new LayoutTransformControl { LayoutTransform = new ScaleTransform(uiScale, uiScale), Child = _root }
            : _root;

        ProgressStore.Changed += () => Dispatcher.UIThread.Post(UpdateStats);
        if (!Shell.Compact) SizeChanged += (_, e) => OnWidthChanged(e.NewSize.Width);
        // Клавиши ловим на уровне окна: если фокус ни на чём (клик по пустому месту), событие не доходит до MainView
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is { } keyTop && _keysOn != keyTop)
            {
                _keysOn?.RemoveHandler(KeyDownEvent, OnPreviewKey);
                keyTop.AddHandler(KeyDownEvent, OnPreviewKey, RoutingStrategies.Tunnel);
                _keysOn = keyTop;
            }
            if (_opened) return;
            _opened = true;
            // системная кнопка «Назад» на телефоне — шаг назад по страницам
            if (TopLevel.GetTopLevel(this) is { } tl) tl.BackRequested += (_, e) => { if (_overlay != null) { CloseOverlay(); e.Handled = true; } else if (_stack.Count > 1) { Pop(); e.Handled = true; } };
            // Android 15+ рисует приложение под строкой состояния и жестовой панелью — отступаем на их высоту
            if (TopLevel.GetTopLevel(this)?.InsetsManager is { } im)
            {
                im.DisplayEdgeToEdgePreference = true;
                im.SystemBarColor = ((ISolidColorBrush)U.B("Bg")).Color;
                Padding = im.SafeAreaPadding;
                im.SafeAreaChanged += (_, e) => Padding = e.SafeAreaPadding;
            }
            Dispatcher.UIThread.Post(OnOpened, DispatcherPriority.Loaded);
            if (ProgressStore.ShieldsUsedOnLoad > 0)
                DispatcherTimer.RunOnce(() => ShowToast(ProgressStore.ShieldsUsedOnLoad == 1 ? "Щит серии спас серию за пропущенный день!" : $"Щиты серии спасли серию за {ProgressStore.ShieldsUsedOnLoad} пропущенных дня!", "盾"), TimeSpan.FromSeconds(4));
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
        // настоящий первый кадр — после отрисовки, а не после построения дерева
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ => Perf.WarmUpAfterFirstFrame());
        UpdateStats();
        Dispatcher.UIThread.Post(() => { if (Win is { } w && !Shell.TestRun) Badge.Update(w); }, DispatcherPriority.Background);
        Dispatcher.UIThread.Post(() => { if (!Environment.GetCommandLineArgs().Skip(1).Any()) WordOfDay.Tick(); }, DispatcherPriority.Background);
        _navItems["home"].IsChecked = true;
        PlayIntro();
        if (Win is { } win && !Shell.TestRun) Tray.Init(win); // проверки не показывают значок в трее
        var args = Platform.Hooks.Args ?? Environment.GetCommandLineArgs();
        bool plain = !args.Skip(1).Any();
        if (!ProgressStore.P.Flags.Contains("onboarded") && plain) Push(new OnboardingPage());
        if (plain)
        {
            Updater.AutoCheck();
            // итоги прошлой недели — один раз в начале новой (не поверх знакомства с приложением)
            if (ProgressStore.P.Flags.Contains("onboarded")) DispatcherTimer.RunOnce(WeekSummary.MaybeShow, TimeSpan.FromSeconds(2.5));
            if (CrashReport.HasNew()) { CrashReport.MarkSeen(); Shell.Toast("В прошлый раз была ошибка — отправить отчёт можно в Настройках → «Обратная связь»", "!"); }
        }
        var remind = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        remind.Tick += (_, _) => { if (!Shell.TestRun) Tray.Tick(); };
        remind.Start();
        int pi = Array.IndexOf(args, "--page");
        if (pi > 0 && pi + 1 < args.Length) NavigateRoot(args[pi + 1]);
#if DEBUG
        DebugArgs(args);
#endif
    }

    // ─────────── Навигация ───────────
    static readonly Dictionary<string, Func<Control>> Factories = new()
    {
        ["home"] = () => new HomePage(),
        ["stats"] = () => new StatsPage(),
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
        ["japan"] = () => new JapanPage(),
        ["more"] = () => new MorePage(),
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
        Resume.Remember(page);
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
        bool focus = page is Controls.QuizSession or Controls.FlashSession { IsEmpty: false } or BuilderSession or SpeechLessonPage or DialogPage or WritingPage or KanaRainGame or MemoryGame or SprintGame or OnboardingPage
            or Controls.LearnSession or StudyPage or MatchRushGame or WordBuilderGame or ListenCatchGame;
        _sideCol.Width = new GridLength(focus || Shell.Compact ? 0 : SideWidth);
        _sidebar.IsVisible = !focus && !Shell.Compact;
        // меню снова видно (например, после упражнения) — индикатор к текущему разделу
        if (_sidebar.IsVisible && _navItems.TryGetValue(_rootKey, out var curItem)) Dispatcher.UIThread.Post(() => MoveNavIndicator(curItem), DispatcherPriority.Loaded);
        _tabs.IsVisible = Shell.Compact && !focus;
        _backBtn.IsVisible = _stack.Count > 1;
        if (_brand != null) _brand.IsVisible = _stack.Count <= 1;
        // телефон: на корневых экранах — логотип (у страниц свой баннер с названием), внутри — название экрана
        _crumb.Text = Shell.Compact ? (_stack.Count > 1 ? (_stack[^1] as IPage)?.Title ?? "" : "Kotodama")
            : string.Join("  ›  ", _stack.OfType<IPage>().Select(p => p.Title));
        (page as IPage)?.OnShown();
        Anim.Opacity(_host, 0, 1, 220);
        Anim.Run((_hostShift, "y"), dir > 0 ? 10 : -10, 0, 380, v => _hostShift.Y = v, Ease.QuinticOut);
        UpdateStats();
    }

    TopLevel? _keysOn;

    void OnPreviewKey(object? sender, KeyEventArgs e)
    {
        // Ctrl+K — и в русской раскладке (Ctrl+Л): проверяем и код клавиши, и её физическое место
        if (Environment.GetEnvironmentVariable("KOTODAMA_KEYLOG") is { } kl) System.IO.File.AppendAllText(kl, $"{e.Key} {e.PhysicalKey} {e.KeyModifiers} intro={_intro.IsVisible}" + Environment.NewLine);
        bool ctrlK = e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && (e.Key == Key.K || e.PhysicalKey == PhysicalKey.K);
        if (ctrlK && !_intro.IsVisible) { if (_palette == null) OpenPalette(); e.Handled = true; return; }
        if (_intro.IsVisible) { CloseIntro(fast: true); e.Handled = true; return; }
        if (_overlay != null && e.Key == Key.Escape) { CloseOverlay(); e.Handled = true; return; }
        if (_palette != null) return;
        if (!Shell.Compact && HandleHotkey(e)) { e.Handled = true; return; }
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
        Challenges.Check();
        var p = ProgressStore.P;
        _streak.Text = $"{p.Streak} {Plural(p.Streak, "день", "дня", "дней")}";
        _railStreak.Text = p.Streak.ToString();
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
        if (Win is { IsVisible: true } bw && !Shell.TestRun) Badge.Update(bw);
    }

    public static string Plural(int n, string one, string few, string many)
    {
        n = Math.Abs(n) % 100;
        if (n is >= 11 and <= 14) return many;
        return (n % 10) switch { 1 => one, 2 or 3 or 4 => few, _ => many };
    }

    // ─────────── Окно ───────────
    public void OnStateChanged(WindowState state)
    {
        // Развёрнутое окно без рамки выходит за край экрана на толщину рамки — компенсируем
        if (CustomChrome) _root.Margin = state == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        if (_maxBtn != null) _maxBtn.Content = state == WindowState.Maximized ? "" : "";
    }

}
