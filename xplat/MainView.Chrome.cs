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
        top.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { Controls.Art.Icon("flame", 22), _streak } });
        top.Children.Add(_levelText);
        var goalRow = new Grid { Margin = new Thickness(0, 8, 0, 5) };
        goalRow.Children.Add(new TextBlock { Text = "Сегодня", FontSize = 11.5, Foreground = U.B("Faint") });
        goalRow.Children.Add(_goalText);
        var summary = new Border
        {
            Margin = new Thickness(14, 6, 14, 12), Padding = new Thickness(12, 10), CornerRadius = new CornerRadius(14),
            Background = U.B("Surface"), BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { top, goalRow, _goalBar, _xpBar } },
        };
        DockPanel.SetDock(summary, Dock.Bottom);
        dock.Children.Add(summary);

        // Меню со скользящей подложкой выбранного пункта
        _navIndicator.VerticalAlignment = VerticalAlignment.Top; _navIndicator.Height = 34; _navIndicator.Margin = new Thickness(12, 0);
        _navIndicator.CornerRadius = new CornerRadius(10); _navIndicator.Background = U.B("SelectedTint"); _navIndicator.Opacity = 0; _navIndicator.IsHitTestVisible = false;
        _navIndicator.RenderTransform = _navShift;
        _navIndicator.Child = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(2), Background = U.B("Accent"), HorizontalAlignment = HorizontalAlignment.Left };
        void Item(string key, string icon, object content)
        {
            var rb = new RadioButton { Tag = Controls.Art.Icon(icon, 24), Content = content, GroupName = "nav" };
            rb.Classes.Add("NavItem");
            rb.Checked += (_, _) => { MoveNavIndicator(rb); ShowRoot(key); };
            _navItems[key] = rb;
            _nav.Children.Add(rb);
        }
        void Header(string t) => _nav.Children.Add(U.T(t, "Overline").M(28, 7, 0, 2));
        _dueText.FontSize = 11; _dueText.FontWeight = FontWeight.SemiBold; _dueText.Foreground = U.B("OnAccent");
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
        Header("ЧИТАТЬ И ИГРАТЬ");
        Item("reader", "books", "Книги и тексты");
        Item("japan", "torii", "Япония");
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
            var lab = new TextBlock { Text = label, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            var cell = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Height = 72 };
            cell.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { top, lab } });
            if (key == "review")
            {
                // число карточек к повторению — на вкладке
                var badge = new Border { Background = U.B("Accent"), CornerRadius = new CornerRadius(8), Padding = new Thickness(5, 0), MinWidth = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(30, 8, 0, 0), IsVisible = false,
                    Child = new TextBlock { FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = U.B("OnAccent"), HorizontalAlignment = HorizontalAlignment.Center } };
                _tabBadge = badge;
                cell.Children.Add(badge);
            }
            var k = key;
            cell.PointerReleased += (_, _) => { if (_rootKey == k && _stack.Count > 1) ShowRoot(k); else NavigateRoot(k); };
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
