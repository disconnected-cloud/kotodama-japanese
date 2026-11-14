using Avalonia;
using Avalonia.VisualTree;
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

namespace Matsubakira.Views;

public sealed record DictRow(DictEntry E)
{
    public string Jp => E.Jp;
    public string Kana => E.Kanji.Length > 0 ? E.Kana : "";
    public string Romaji => E.Romaji;
    // Убираем служебную разметку JMdict: «: {～な} ясный» → «(～な) ясный»
    public string Ru { get; } = E.Pretty;
    public bool CommonVis => E.Common;
    public string AddGlyph => JDict.InStudy(E) ? "" : "";
    Control? _accent;
    public Control? AccentEl => _accent ??= Pitch.Pattern(E.Jp, E.Kana) is { } p ? new Controls.AccentView(p, compact: true) { HorizontalAlignment = HorizontalAlignment.Left } : null;
}

/// <summary>Большой словарь: ~69 тыс. статей, поиск по-русски, латиницей, каной и иероглифами.</summary>
public sealed class DictPage : UserControl, IPage
{
    public string Title => "Словарь";
    readonly ListBox _list;
    // телефон: страница прокручивается целиком, результаты — порциями (см. WordsPage)
    readonly StackPanel _plain = new();
    List<DictRow> _rows = new();
    int _shown = 30;
    readonly StackPanel _empty = new();
    readonly StackPanel _recent = new();

    /// <summary>Недавно открытые слова — одним нажатием обратно к ним.</summary>
    void FillRecent()
    {
        _recent.Children.Clear();
        var h = ProgressStore.P.DictHistory;
        if (h.Count == 0 || !JDict.Loaded) return;
        _recent.Children.Add(U.T("НЕДАВНО СМОТРЕЛ", "Overline").M(0, 10, 0, 6));
        var wrap = new WrapPanel();
        foreach (var w in h.Take(12))
        {
            var e = JDict.Lookup(w);
            if (e == null) continue;
            var b = new Button { Content = U.Row(6, U.Jp(e.Jp, 16), U.T(e.Ru.Split(';')[0].Trim() is var s && s.Length > 18 ? s[..18] + "…" : s, "Muted", 12).CenterV()), Margin = new Thickness(0, 0, 8, 8) };
            b.Click += (_, _) => Shell.Open(new WordPage(e));
            wrap.Children.Add(b);
        }
        _recent.Children.Add(wrap);
    }
    readonly TextBox _box = new() { FontSize = 18, Padding = new Thickness(44, 10, 16, 10), VerticalContentAlignment = VerticalAlignment.Center };
    readonly TextBlock _info = U.T("", "Muted");
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(160) };
    int _searchId;

    public DictPage()
    {
        _list = new ListBox { Classes = { "Plain" }, ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<DictRow>((r, _) => RowView(r), true) };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);

        _debounce.Tick += (_, _) => { _debounce.Stop(); Search(); };
        _box.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };

        var root = new Grid { Margin = (Shell.Compact ? new Thickness(12, 12, 12, 16) : new Thickness(40, 12, 40, 16)), MaxWidth = 1240 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(Shell.Compact ? new RowDefinition { Height = GridLength.Auto } : new RowDefinition());
        root.Children.Add(U.Header("辞", "Словарь", Shell.Compact ? $"JMdict — {JDict.CountLabel} статей. Ищи по-русски, латиницей, каной или кандзи."
            : $"Справочник JMdict — {JDict.CountLabel} статей, чтобы найти любое слово. Учебные слова с карточками — в разделе «Слова». Ищи по-русски, латиницей (taberu), каной или кандзи."));

        var search = new Grid();
        search.Children.Add(_box);
        var ph = U.T("Например: кошка · neko · ねこ · 猫", "Muted", 16).Also(t => { t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis; });
        ph.IsHitTestVisible = false; ph.Margin = new Thickness(46, 0, 0, 0); ph.VerticalAlignment = VerticalAlignment.Center;
        _box.TextChanged += (_, _) => ph.IsVisible = string.IsNullOrEmpty(_box.Text);
        search.Children.Add(ph);
        search.Children.Add(new TextBlock { Text = "", FontFamily = U.R<FontFamily>("Icons"), FontSize = 16, Foreground = U.B("Muted"), Margin = new Thickness(16, 0, 0, 0), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(search.RowAt(1));
        root.Children.Add(_info.M(2, 10, 0, 10).RowAt(2));
        root.Children.Add((Shell.Compact ? (Control)_plain : _list).RowAt(3));
        // Пустой экран: подсказка и примеры запросов
        var chips = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var q in new[] { "кошка", "taberu", "ありがとう", "猫", "вкусный", "benkyou", "友達", "путешествие" })
        {
            var b = new Button { Content = q, Classes = { "Chip" }, Margin = new Thickness(0, 0, 8, 8) };
            var qq = q;
            b.Click += (_, _) => { _box.Text = qq; _box.CaretIndex = qq.Length; };
            chips.Children.Add(b);
        }
        _empty.Children.Add(Controls.Art.Say("Ищи как удобно: по-русски, латиницей, каной или иероглифами. Нажми на слово — откроется разбор иероглифов, примеры и похожие слова.", Controls.Mood.Wink, 130));
        _empty.Children.Add(U.T("Попробуй", "Overline").M(0, 14, 0, 0));
        _empty.Children.Add(chips.M(0, 6, 0, 0));
        _empty.Children.Add(_recent);
        _empty.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(_empty.RowAt(3));
        Content = Shell.Compact ? new ScrollViewer { Content = root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } : root;
    }

    void RenderPlain()
    {
        _plain.Children.Clear();
        foreach (var r in _rows.Take(_shown)) _plain.Children.Add(RowView(r));
        if (_rows.Count > _shown)
        {
            var more = U.Btn($"Показать ещё (осталось {_rows.Count - _shown})", () => { _shown += 30; RenderPlain(); }, "", "");
            more.HorizontalAlignment = HorizontalAlignment.Stretch;
            _plain.Children.Add(more.M(0, 4, 0, 12));
        }
    }

    /// <summary>Запрос, переданный из палитры Ctrl+K.</summary>
    public static string? PendingQuery;

    public async void OnShown()
    {
        if (PendingQuery != null) { _box.Text = PendingQuery; PendingQuery = null; }
        Dispatcher.UIThread.Post(() => _box.Focus(), DispatcherPriority.Input);
        if (!JDict.Loaded)
        {
            _info.Text = "Загружаю словарь…";
            try { await JDict.LoadAsync(); }
            catch
            {
                _info.Text = "Словарь не загрузился. Подробности — в журнале (Настройки → «Обратная связь»).";
                _plain.Children.Clear();
                _plain.Children.Add(U.Btn("Попробовать ещё раз", OnShown, "Primary", "").Also(b => b.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left));
                _empty.IsVisible = false;
                return;
            }
        }
        FillRecent();
        Search();
    }

    CancellationTokenSource? _searchCts;

    async void Search()
    {
        int id = ++_searchId;
        var q = _box.Text ?? "";
        if (q.Trim().Length == 0)
        {
            _list.ItemsSource = null;
            _plain.Children.Clear();
            _empty.IsVisible = true;
            _info.Text = $"{JDict.Count:N0} статей. Введи слово — по-русски, латиницей, каной или кандзи. Кнопка «+» добавит слово в твои карточки.";
            return;
        }
        _empty.IsVisible = false;
        _searchCts?.Cancel(); // прежний запрос ещё ищет — останавливаем, не тратим процессор
        var cts = _searchCts = new CancellationTokenSource();
        List<DictEntry> res;
        try { res = await Task.Run(() => JDict.Search(q, ct: cts.Token)); }
        catch (OperationCanceledException) { return; }
        if (id != _searchId) return; // пришёл более новый запрос
        if (Shell.Compact) { _rows = res.Select(e => new DictRow(e)).ToList(); _shown = 30; RenderPlain(); }
        else _list.ItemsSource = res.Select(e => new DictRow(e)).ToList();
        _info.Text = q.Trim().Length == 0
            ? $"{JDict.Count:N0} статей · показаны самые частые слова"
            : res.Count == 0 ? "Ничего не найдено. Попробуй другое написание или более короткий запрос."
            : $"Найдено: {res.Count}{(res.Count >= 120 ? "+" : "")}";
    }

    /// <summary>Телефон: слово с чтением и кнопками сверху, перевод ниже.</summary>
    static Control CompactRow(DictRow r)
    {
        var top = U.Cols("*", "auto");
        top.Children.Add(U.Row(10, new TextBlock { Text = r.Jp, FontFamily = U.R<FontFamily>("JpFont"), FontSize = 20 },
            U.Stack(new TextBlock { Text = r.Kana, FontFamily = U.R<FontFamily>("JpFont"), FontSize = 13, Foreground = U.B("Accent") }, new TextBlock { Text = r.Romaji, FontSize = 12, Foreground = U.B("Muted") }).CenterV()));
        var speak = new Button { Content = "\uE767", Width = 34, Height = 34, Classes = { "IconButton" } };
        speak.Click += (_, _) => Speech.Say(r.E.Kana);
        var add = new Button { Content = r.AddGlyph, Width = 34, Height = 34, Classes = { "IconButton" } };
        add.Click += (_, _) => { if (JDict.AddToStudy(r.E)) { add.Content = "\uE73E"; Shell.Toast($"«{r.E.Jp}» добавлено в «Мои слова»", "追加"); } };
        top.Children.Add(U.Row(0, speak, add).Col(1));
        var sp = U.Stack(top, new TextBlock { Text = r.Ru, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        return new Border { Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(14, 8), CornerRadius = new CornerRadius(12), Background = U.B("Surface"), BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(1), Child = sp };
    }

    /// <summary>Строка словаря (аналог WPF DataTemplate «DictRow»).</summary>
    static Control RowView(DictRow? r)
    {
        if (r == null) return new Border();
        if (Shell.Compact) return CompactRow(r);
        var g = U.Cols("190", "190", "*", "auto");
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock { Text = r.Jp, FontFamily = U.R<FontFamily>("JpFont"), FontSize = 22, TextTrimming = TextTrimming.CharacterEllipsis });
        if (r.CommonVis)
            left.Children.Add(new Border { Background = U.B("GoodSoft"), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0),
                Child = new TextBlock { Text = "частое", FontSize = 12, Foreground = U.B("Good") } });
        g.Children.Add(left);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock { Text = r.Kana, FontFamily = U.R<FontFamily>("JpFont"), FontSize = 14, Foreground = U.B("Accent"), TextTrimming = TextTrimming.CharacterEllipsis });
        mid.Children.Add(new TextBlock { Text = r.Romaji, FontSize = 12, Foreground = U.B("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
        if (r.AccentEl is { } acc) { if (acc.Parent is Panel pp) pp.Children.Remove(acc); acc.Margin = new Thickness(0, 2, 0, 0); mid.Children.Add(acc); }
        g.Children.Add(mid.Col(1));
        g.Children.Add(new TextBlock { Text = r.Ru, FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }.Col(2));
        var speak = new Button { Content = "", Width = 34, Height = 34, Classes = { "IconButton" } }.Tip("Произнести");
        speak.Click += (_, _) => Speech.Say(r.E.Kana);
        var add = new Button { Content = r.AddGlyph, Width = 34, Height = 34, Classes = { "IconButton" } }.Tip("Добавить в мои карточки");
        add.Click += (_, _) =>
        {
            if (JDict.AddToStudy(r.E))
            {
                add.Content = "";
                Anim.Pop(add, 0.6);
                Shell.Toast($"«{r.E.Jp}» добавлено в «Мои слова»", "追加");
            }
            else Shell.Toast("Это слово уже в твоих карточках", "✓");
        };
        g.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { speak, add } }.Col(3));
        var bd = new Border
        {
            Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(16, 10), CornerRadius = new CornerRadius(12), Background = U.B("Surface"),
            BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(1), Child = g,
        };
        bd.Cursor = Cursors.Hand;
        bd.PointerReleased += (_, e) => { if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) == null) Shell.Open(new WordPage(r.E)); };
        bd.PointerEntered += (_, _) => bd.BorderBrush = U.B("StrokeStrong");
        bd.PointerExited += (_, _) => bd.BorderBrush = U.B("Stroke");
        return bd;
    }
}
