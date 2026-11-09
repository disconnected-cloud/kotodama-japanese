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
using Matsubakira.Services;
using Matsubakira.Views;

namespace Matsubakira.Controls;

/// <summary>Командная палитра Ctrl+K: мгновенный поиск по разделам, юнитам, урокам, диалогам, словам и словарю.</summary>
public sealed class Palette : Grid
{
    sealed record Item(string Icon, string Title, string Sub, Action Go);
    readonly TextBox _box = new() { FontSize = 18, Padding = new Thickness(44, 12, 16, 12) };
    readonly StackPanel _list = new();
    List<Item> _items = new();
    int _sel;
    readonly Action _close;

    public Palette(Action close)
    {
        _close = close;
        Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));
        PointerPressed += (_, e) => { if (e.Source == this) _close(); };
        var card = new Border
        {
            Classes = { "Card" }, Width = Shell.Compact ? double.NaN : 640, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 90, 0, 0), Padding = new Thickness(12),
            BoxShadow = BoxShadows.Parse("0 6 24 0 #59000000"),
        };
        var sp = new StackPanel();
        var search = new Grid();
        search.Children.Add(_box);
        search.Children.Add(new TextBlock { Text = "", FontFamily = U.R<FontFamily>("Icons"), FontSize = 16, Foreground = U.B("Muted"), Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
        sp.Children.Add(search);
        sp.Children.Add(new ScrollViewer { Content = _list, MaxHeight = 440, Margin = new Thickness(0, 10, 0, 0) });
        sp.Children.Add(U.T("↑↓ — выбор · Enter — открыть · Esc — закрыть", "Muted", 11.5).Center().M(0, 8, 0, 0));
        card.Child = sp;
        Children.Add(card);
        _box.TextChanged += (_, _) => Refresh();
        _box.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Loaded += (_, _) => { _box.Focus(); Anim.FadeUp(card, 0, -10, 200); };
        Refresh();
    }

    void OnKey(object? s, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: _close(); e.Handled = true; break;
            case Key.Down: _sel = Math.Min(_items.Count - 1, _sel + 1); Render(); e.Handled = true; break;
            case Key.Up: _sel = Math.Max(0, _sel - 1); Render(); e.Handled = true; break;
            case Key.Enter: if (_items.Count > 0) Run(_items[_sel]); e.Handled = true; break;
        }
    }

    void Run(Item it) { _close(); it.Go(); }

    static IEnumerable<Item> All()
    {
        foreach (var (k, ic, t) in new[] { ("home", "家", "Главная"), ("path", "道", "Путь обучения"), ("kana", "あ", "Кана"), ("words", "語", "Слова"), ("kanji", "漢", "Кандзи"), ("grammar", "文", "Грамматика"), ("speech", "話", "Говорение"), ("dialogs", "対", "Диалоги"), ("dict", "辞", "Словарь"), ("reader", "読", "Чтение"), ("review", "復", "Повторение"), ("trainers", "練", "Тренажёры"), ("settings", "設", "Настройки") })
            yield return new Item(ic, t, "Раздел", () => Shell.Go(k));
        yield return new Item("耳", "Проверка микрофона", "Настройка", () => Shell.Open(new MicCheckPage()));
        yield return new Item("活", "Спряжение глаголов", "Тренажёр", () => Shell.Open(Conj.Quiz()));
        yield return new Item("数", "Счётные суффиксы", "Тренажёр", () => Shell.Open(new CountersPage()));
        yield return new Item("書", "Прописи", "Тренажёр", () => Shell.Open(new WritingPage()));
        yield return new Item("雨", "Кана-дождь", "Игра", () => Shell.Open(new KanaRainGame(false)));
        yield return new Item("対", "Найди пару", "Игра", () => Shell.Open(new MemoryGame()));
        yield return new Item("速", "Спринт 60 секунд", "Игра", () => Shell.Open(new SprintGame()));
        foreach (var u in Db.Course) yield return new Item("道", $"Юнит {u.Id}. {u.Title}", "Путь · " + u.Level, () => Shell.Open(new UnitPage(u)));
        foreach (var g in Db.Grammar) yield return new Item("文", g.Title, "Грамматика · урок " + g.Id, () => Shell.Open(new GrammarLessonPage(g)));
        foreach (var l in Db.SpeechLessons) yield return new Item("話", l.Title, "Говорение", () => Shell.Open(new SpeechLessonPage(l)));
        foreach (var d in Db.Dialogs) yield return new Item("対", d.Title, "Диалог · " + d.Level, () => Shell.Open(new DialogPage(d)));
    }

    void Refresh()
    {
        var q = _box.Text.Trim();
        var ql = q.ToLowerInvariant();
        var items = All().Where(i => q.Length == 0 || i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || i.Sub.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(q.Length == 0 ? 14 : 8).ToList();
        if (q.Length >= 1)
        {
            var words = Db.Words.Where(w => w.Ru.Contains(ql, StringComparison.OrdinalIgnoreCase) || w.Jp.Contains(q) || w.Kana.Contains(q) || w.Romaji.StartsWith(ql))
                .OrderBy(w => w.Ru.StartsWith(ql, StringComparison.OrdinalIgnoreCase) || w.Romaji == ql || w.Jp == q ? 0 : 1).Take(6);
            foreach (var w in words)
            {
                var word = w;
                items.Add(new Item("語", $"{w.Jp}  {w.Kana}", $"{w.Ru} · {w.Romaji}", () => { Speech.Say(word.Kana); Shell.Open(new FlashSession(word.Jp, new[] { FlashSession.FromWord(word) })); }));
            }
            items.Add(new Item("辞", $"Искать «{q}» в словаре", "69 000 статей JMdict", () => { Shell.Go("dict"); DictPage.PendingQuery = q; }));
        }
        _items = items;
        _sel = 0;
        Render();
    }

    void Render()
    {
        _list.Children.Clear();
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            var g = U.Cols("36", "*");
            g.Children.Add(U.Jp(it.Icon, 18, U.B("Accent"), true).CenterV());
            g.Children.Add(U.Stack(U.T(it.Title, "H3", 14), U.T(it.Sub, "Muted", 12)).Col(1));
            var row = new Border { Child = g, Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, Background = i == _sel ? U.B("AccentSoft") : Brushes.Transparent };
            row.PointerReleased += (_, _) => Run(it);
            int idx = i;
            row.PointerEntered += (_, _) => { if (_sel != idx) { _sel = idx; Render(); } };
            _list.Children.Add(row);
        }
    }
}
