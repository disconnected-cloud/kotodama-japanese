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

// Боковая панель, тост и нижние вкладки телефона (часть MainView)
public partial class MainView : UserControl
{
    // ─────────── Боковая панель ───────────
    Control BuildSidebar()
    {
        var dock = new DockPanel();
        var logo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 20, 0, 22) };
        logo.Children.Add(new TextBlock { Text = "言", FontFamily = U.R<FontFamily>("JpSerif"), FontSize = 24, Foreground = U.B("Accent"), VerticalAlignment = VerticalAlignment.Center });
        logo.Children.Add(_logoText = new StackPanel
        {
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "Kotodama", FontSize = 15, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "日本語を学ぼう", FontFamily = U.R<FontFamily>("JpFont"), FontSize = 12, Foreground = U.B("Faint") },
            },
        });
        DockPanel.SetDock(logo, Dock.Top);
        dock.Children.Add(logo);

        // Сводка внизу: серия, уровень, цель дня
        _streak.FontWeight = FontWeight.SemiBold; _streak.FontSize = 13; _streak.Margin = new Thickness(8, 0, 0, 0); _streak.VerticalAlignment = VerticalAlignment.Center;
        _levelText.FontSize = 12; _levelText.Foreground = U.B("Muted"); _levelText.HorizontalAlignment = HorizontalAlignment.Right; _levelText.VerticalAlignment = VerticalAlignment.Center;
        _goalText.FontSize = 12; _goalText.Foreground = U.B("Faint"); _goalText.HorizontalAlignment = HorizontalAlignment.Right;
        _goalBar.Foreground = U.B("Gold");
        // толще и со скруглением: тонкие полоски на тёмном фоне почти не читались (7.1)
        foreach (var bar in new[] { _goalBar, _xpBar }) { bar.Height = 6; bar.MinHeight = 6; bar.CornerRadius = new CornerRadius(3); }
        _xpBar.Margin = new Thickness(0, 5, 0, 0);
        _xpBar.Tip("Прогресс до следующего уровня");
        var top = new Grid();
        top.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { Controls.Art.Icon("flame", 22), _streak } });
        top.Children.Add(_levelText);
        var goalRow = new Grid { Margin = new Thickness(0, 8, 0, 5) };
        goalRow.Children.Add(new TextBlock { Text = "Сегодня", FontSize = 12, Foreground = U.B("Faint") });
        goalRow.Children.Add(_goalText);
        var summary = new Border
        {
            Margin = new Thickness(14, 6, 14, 12), Padding = new Thickness(12, 10), CornerRadius = new CornerRadius(14),
            Background = U.B("Surface"), BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { top, goalRow, _goalBar, _xpBar } },
        };
        _summary = summary;
        // свёрнутое меню: вместо сводки — огонёк серии с числом дней
        _railStreak.FontSize = 12; _railStreak.FontWeight = FontWeight.SemiBold; _railStreak.HorizontalAlignment = HorizontalAlignment.Center;
        _summaryRail = new StackPanel { IsVisible = false, Margin = new Thickness(0, 6, 0, 14), HorizontalAlignment = HorizontalAlignment.Center, Children = { Controls.Art.Icon("flame", 26).Center(), _railStreak.M(0, 4, 0, 0) } };
        ToolTip.SetTip(_summaryRail, "Серия дней подряд");
        var bottom = new StackPanel();
        // кнопка «свернуть меню» (Ctrl+B)
        _railBtn = new Button { Classes = { "Ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(12, 0, 12, 2), Padding = new Thickness(14, 6) };
        _railBtn.Click += (_, _) => ToggleRail();
        bottom.Children.Add(_railBtn);
        bottom.Children.Add(summary);
        bottom.Children.Add(_summaryRail);
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(bottom);

        // Меню со скользящей подложкой выбранного пункта
        _navIndicator.VerticalAlignment = VerticalAlignment.Top; _navIndicator.Height = 34; _navIndicator.Margin = new Thickness(12, 0);
        _navIndicator.CornerRadius = new CornerRadius(10); _navIndicator.Background = U.B("SelectedTint"); _navIndicator.Opacity = 0; _navIndicator.IsHitTestVisible = false;
        _navIndicator.RenderTransform = _navShift;
        _navIndicator.Child = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(2), Background = U.B("Accent"), HorizontalAlignment = HorizontalAlignment.Left };
        string? group = null;
        void Item(string key, string icon, object content)
        {
            // своя кисть у каждого пункта: серая, а у выбранного — цвет акцента
            var brush = new SolidColorBrush(((ISolidColorBrush)U.B("Muted")).Color);
            var rb = new RadioButton { Tag = Controls.Art.MonoIcon(icon, 24, brush), Content = content, GroupName = "nav" };
            _navLabels[rb] = content as string ?? "Повторение";
            rb.Classes.Add("NavItem");
            rb.Checked += (_, _) => { brush.Color = ((ISolidColorBrush)U.B("Accent")).Color; MoveNavIndicator(rb); ShowRoot(key); };
            rb.Unchecked += (_, _) => brush.Color = ((ISolidColorBrush)U.B("Muted")).Color;
            _navItems[key] = rb;
            if (group != null) _navGroups[group].Add(rb);
            _nav.Children.Add(rb);
        }
        // Заголовок группы сворачивает её; состояние запоминается (7.1)
        void Header(string t)
        {
            group = t; _navGroups[t] = new();
            var chevron = Controls.Art.Glyph("next", 9, U.B("Faint"));
            var h = new Button
            {
                Classes = { "Ghost" }, Padding = new Thickness(0), Margin = new Thickness(16, 7, 12, 2), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { U.T(t, "Overline").M(12, 0, 0, 0), chevron.Also(g => { g.VerticalAlignment = VerticalAlignment.Center; g.RenderTransformOrigin = RelativePoint.Center; }) } },
            };
            ToolTip.SetTip(h, "Свернуть или развернуть группу");
            void Apply() { chevron.RenderTransform = new RotateTransform(Folded(t) ? 0 : 90); ApplyFolds(); }
            h.Click += (_, _) =>
            {
                if (!ProgressStore.P.Flags.Add("navfold:" + t)) ProgressStore.P.Flags.Remove("navfold:" + t);
                ProgressStore.Notify();
                Apply();
            };
            Apply();
            _navHeaders.Add(h); _nav.Children.Add(h);
        }
        _dueText.FontSize = 12; _dueText.FontWeight = FontWeight.SemiBold; _dueText.Foreground = U.B("OnAccent");
        _dueBadge.HorizontalAlignment = HorizontalAlignment.Right; _dueBadge.Background = U.B("Accent"); _dueBadge.CornerRadius = new CornerRadius(8);
        _dueBadge.Padding = new Thickness(6, 0); _dueBadge.IsVisible = false; _dueBadge.Child = _dueText;
        Item("home", "home", "Главная");
        Item("path", "path", "Путь обучения");
        Item("review", "review", new Grid { Width = 128, Children = { new TextBlock { Text = "Повторение" }, _dueBadge } });
        Item("stats", "trophy", "Прогресс");
        Header("УЧИТЬ");
        Item("kana", "kana", "Азбука кана");
        Item("words", "words", "Слова");
        Item("kanji", "kanji", "Кандзи");
        Item("grammar", "grammar", "Грамматика");
        Header("ГОВОРИТЬ");
        Item("speech", "speech", "Говорение");
        Item("dialogs", "dialogs", "Диалоги");
        Item("quests", "quest", "Приключения");
        Header("ЧИТАТЬ И ИГРАТЬ");
        Item("reader", "books", "Книги и тексты");
        Item("japan", "torii", "Япония");
        Item("journal", "journal", "Дневник");
        Item("dict", "dict", "Словарь");
        Item("trainers", "trainers", "Тренажёры и игры");
        Item("settings", "settings", "Настройки");

        var navGrid = new Grid { Children = { _navIndicator, _nav } };
        dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = navGrid });
        ApplyRail(false);
        return dock;
    }

    // ─────────── Свёрнутое меню («рельс»): только иконки ───────────
    StackPanel? _logoText, _summaryRail;
    Border? _summary;
    Button? _railBtn;
    readonly TextBlock _railStreak = new();
    readonly List<Control> _navHeaders = new();
    readonly Dictionary<RadioButton, string> _navLabels = new();
    readonly Dictionary<string, List<RadioButton>> _navGroups = new();

    static bool Folded(string group) => ProgressStore.P.Flags.Contains("navfold:" + group);

    /// <summary>Свёрнутая группа прячет пункты (кроме выбранного); в узком меню-«рельсе» видны все иконки.</summary>
    void ApplyFolds()
    {
        foreach (var (g, items) in _navGroups)
            foreach (var rb in items) rb.IsVisible = Rail || !Folded(g) || rb.IsChecked == true;
        if (_navItems.TryGetValue(_rootKey, out var cur)) Dispatcher.UIThread.Post(() => MoveNavIndicator(cur), DispatcherPriority.Loaded);
    }
    bool _narrow;   // окно уже 980 px — меню сворачивается само
    const double SideFull = 232, SideRail = 76;

    bool Rail => ProgressStore.P.SidebarRail || _narrow;
    double SideWidth => Rail ? SideRail : SideFull;

    public void ToggleRail()
    {
        if (Shell.Compact) return;
        if (_narrow && !ProgressStore.P.SidebarRail) { Shell.Toast("Окно узкое — меню свёрнуто автоматически. Расширь окно, чтобы развернуть.", "≡"); return; }
        ProgressStore.P.SidebarRail = !ProgressStore.P.SidebarRail;
        ProgressStore.Notify();
        ApplyRail(true);
    }

    void OnWidthChanged(double w)
    {
        bool narrow = w < 980;
        if (narrow == _narrow) return;
        _narrow = narrow;
        ApplyRail(true);
    }

    void ApplyRail(bool animate)
    {
        bool rail = Rail;
        foreach (var (rb, label) in _navLabels)
        {
            if (rail) rb.Classes.Add("Rail"); else rb.Classes.Remove("Rail");
            ToolTip.SetTip(rb, rail ? label : null);
            ToolTip.SetPlacement(rb, PlacementMode.Right);
        }
        foreach (var h in _navHeaders) h.IsVisible = !rail;
        ApplyFolds();
        if (_logoText != null) _logoText.IsVisible = !rail;
        if (_summary != null) _summary.IsVisible = !rail;
        if (_summaryRail != null) _summaryRail.IsVisible = rail;
        if (_railBtn != null)
        {
            var g = new StackPanel { Orientation = Orientation.Horizontal };
            g.Children.Add(Controls.Art.Glyph(rail ? "next" : "prev", 12, U.B("Faint")).Also(c => c.Margin = new Thickness(6, 0, 0, 0)));
            if (!rail) g.Children.Add(new TextBlock { Text = "Свернуть меню", FontSize = 12, Foreground = U.B("Faint"), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            _railBtn.Content = g;
            ToolTip.SetTip(_railBtn, rail ? "Развернуть меню (Ctrl+B)" : "Ctrl+B");
        }
        if (Shell.Compact || !_sidebar.IsVisible) return;
        double from = _sideCol.Width.Value, to = SideWidth;
        if (animate && Math.Abs(from - to) > 1) Anim.Run((_sideCol, "w"), from, to, 220, v => _sideCol.Width = new GridLength(v), Ease.QuinticOut);
        else _sideCol.Width = new GridLength(to);
        if (_navItems.TryGetValue(_rootKey, out var cur)) Dispatcher.UIThread.Post(() => MoveNavIndicator(cur), DispatcherPriority.Loaded);
    }

    /// <summary>Подложка выбранного пункта плавно «переезжает» к новому пункту.</summary>
    void MoveNavIndicator(Control item, int tries = 0)
    {
        // скрытое меню (телефон, режим фокуса) никогда не получит размер — без этой проверки вызов
        // бесконечно перепланировал сам себя и занимал поток интерфейса
        if (Shell.Compact || !_sidebar.IsVisible) return;
        if (!item.IsArrangeValid || item.Bounds.Height == 0)
        {
            if (tries < 20) Dispatcher.UIThread.Post(() => MoveNavIndicator(item, tries + 1), DispatcherPriority.Loaded);
            return;
        }
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
    static readonly (string key, string icon, string label)[] Tabs =
    {
        ("home", "home", "Главная"), ("path", "path", "Путь"), ("review", "review", "Повтор"), ("speech", "speech", "Говорить"), ("more", "grid", "Ещё"),
    };

    /// <summary>Нижняя панель в духе Material 3: иконка в «пилюле» у активной вкладки, подпись снизу.</summary>
    Control BuildTabs()
    {
        _tabs.ColumnDefinitions = new ColumnDefinitions(string.Join(",", Tabs.Select(_ => "*")));
        _tabs.Background = U.B("Surface");
        for (int i = 0; i < Tabs.Length; i++)
        {
            var (key, icon, label) = Tabs[i];
            var off = Controls.Art.Glyph(icon, 22, U.B("Muted")).Also(g => g.HorizontalAlignment = HorizontalAlignment.Center);
            var on = Controls.Art.Glyph(icon, 22, U.B("Accent")).Also(g => { g.HorizontalAlignment = HorizontalAlignment.Center; g.IsVisible = false; });
            var pillScale = new ScaleTransform(1, 1);
            var pill = new Border { Width = 60, Height = 32, CornerRadius = new CornerRadius(16), Background = U.B("AccentSoft"), Opacity = 0, RenderTransform = pillScale };
            var top = new Grid { Width = 60, Height = 32, HorizontalAlignment = HorizontalAlignment.Center, Children = { pill, off, on } };
            var lab = new TextBlock { Text = label, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            var cell = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Height = 72 };
            cell.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { top, lab } });
            if (key == "review")
            {
                // число карточек к повторению — на вкладке
                var badge = new Border { Background = U.B("Accent"), CornerRadius = new CornerRadius(8), Padding = new Thickness(5, 0), MinWidth = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(30, 8, 0, 0), IsVisible = false,
                    Child = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = U.B("OnAccent"), HorizontalAlignment = HorizontalAlignment.Center } };
                _tabBadge = badge;
                cell.Children.Add(badge);
            }
            var k = key;
            cell.OnClick(() => { if (_rootKey == k && _stack.Count > 1) ShowRoot(k); else NavigateRoot(k); });
            _tabItems[key] = new TabItem(pill, pillScale, off, on, lab);
            _tabs.Children.Add(cell.Col(i));
        }
        HighlightTab();
        return new Border { BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(0, 1, 0, 0), Child = _tabs };
    }

    sealed record TabItem(Border Pill, ScaleTransform Scale, Control Off, Control On, TextBlock Label) { public bool Active; }
    Border? _tabBadge;

    void HighlightTab()
    {
        string active = Tabs.Any(t => t.key == _rootKey) ? _rootKey : "more";
        foreach (var (k, t) in _tabItems)
        {
            bool on = k == active;
            t.Label.Foreground = U.B(on ? "Text" : "Muted");
            t.Label.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            t.On.IsVisible = on; t.Off.IsVisible = !on;
            if (on && !t.Active)
            {
                // «пилюля» раскрывается от центра — только transform и прозрачность, без перерасчёта разметки
                Anim.Run((t.Scale, "x"), 0.5, 1, 260, v => t.Scale.ScaleX = v, Ease.QuinticOut);
                Anim.Opacity(t.Pill, 0, 1, 180);
            }
            else if (!on) { Anim.Stop((t.Scale, "x")); t.Pill.Opacity = 0; }
            t.Active = on;
        }
    }
}
