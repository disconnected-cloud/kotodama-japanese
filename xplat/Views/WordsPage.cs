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
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

public sealed record WordRow(Word W, string Status, Brush StatusBrush)
{
    public bool ExVis => W.ExJp.Length > 0;
}

public sealed class WordsPage : UserControl, IPage
{
    public string Title => "Слова";
    string? _topic;
    string _search = "";
    string _level = ProgressStore.P.Level;
    readonly ListBox _list;
    // Телефон: вся страница прокручивается, список — порциями (вложенный список в узком экране получал нулевую высоту)
    readonly StackPanel _plainList = new();
    List<WordRow> _rows = new();
    int _shown = 40;
    readonly TextBlock _countText = U.T("", "Muted");

    public WordsPage()
    {
        // ListBox в Avalonia виртуализирован по умолчанию: в памяти только видимые строки
        _list = new ListBox { Classes = { "Plain" }, ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<WordRow>((r, _) => RowView(r), true) };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
    }

    /// <summary>Телефон: слово и чтение сверху, перевод и пример ниже.</summary>
    static Control CompactRow(WordRow r)
    {
        var top = U.Cols("auto", "*", "auto");
        top.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = r.StatusBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        top.Children.Add(U.Row(10, new TextBlock { Text = r.W.Jp, FontFamily = U.R<FontFamily>("JpFont"), FontSize = 22 },
            U.Stack(new TextBlock { Text = r.W.Kana, FontFamily = U.R<FontFamily>("JpFont"), FontSize = 13, Foreground = U.B("Accent") }, new TextBlock { Text = r.W.Romaji, FontSize = 11, Foreground = U.B("Muted") }).CenterV()).Col(1));
        top.Children.Add(new TextBlock { Text = "\uE767", FontFamily = U.R<FontFamily>("Icons"), FontSize = 16, Foreground = U.B("Muted"), VerticalAlignment = VerticalAlignment.Center }.Col(2));
        var sp = U.Stack(top, new TextBlock { Text = r.W.Ru, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18, 4, 0, 0) });
        if (r.ExVis) sp.Children.Add(new TextBlock { Text = r.W.ExJp, FontFamily = U.R<FontFamily>("JpFont"), FontSize = 12.5, Foreground = U.B("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18, 3, 0, 0) });
        var bd = new Border { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(14, 10), CornerRadius = new CornerRadius(14), Background = U.B("Surface"), BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(1), Child = sp };
        bd.OnClick(() => Speech.Say(r.W.Kana));
        return bd;
    }

    /// <summary>Строка списка слов (аналог WPF DataTemplate «WordRow»).</summary>
    static Control RowView(WordRow? r)
    {
        if (r == null) return new Border();
        if (Shell.Compact) return CompactRow(r);
        var g = U.Cols("22", "200", "230", "*", "auto");
        g.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = r.StatusBrush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
        g.Children.Add(new TextBlock { Text = r.W.Jp, FontFamily = U.R<FontFamily>("JpFont"), FontSize = 24, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }.Col(1));
        g.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = r.W.Kana, FontFamily = U.R<FontFamily>("JpFont"), FontSize = 14, Foreground = U.B("Accent") },
                new TextBlock { Text = r.W.Romaji, FontSize = 12, Foreground = U.B("Muted") },
            },
        }.Col(2));
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(new TextBlock { Text = r.W.Ru, FontSize = 15, TextWrapping = TextWrapping.Wrap });
        if (r.ExVis)
        {
            var ex = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            ex.Inlines!.Add(new Run(r.W.ExJp) { FontFamily = U.R<FontFamily>("JpFont"), Foreground = U.B("Muted"), FontSize = 13 });
            ex.Inlines.Add(new Run("  "));
            ex.Inlines.Add(new Run(r.W.ExRu) { Foreground = U.B("Faint"), FontSize = 12 });
            right.Children.Add(ex);
        }
        right.Children.Add(new TextBlock { Text = r.Status, FontSize = 11, Foreground = U.B("Faint"), Margin = new Thickness(0, 2, 0, 0) });
        g.Children.Add(right.Col(3));
        g.Children.Add(new TextBlock { Text = "", FontFamily = U.R<FontFamily>("Icons"), FontSize = 16, Foreground = U.B("Muted"), VerticalAlignment = VerticalAlignment.Center }.Col(4));
        var bd = new Border
        {
            Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(18, 12), CornerRadius = new CornerRadius(14), Background = U.B("Surface"),
            BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = g,
        };
        bd.PointerEntered += (_, _) => { bd.BorderBrush = U.B("Accent"); bd.Background = U.B("Surface2"); };
        bd.PointerExited += (_, _) => { bd.BorderBrush = U.B("Stroke"); bd.Background = U.B("Surface"); };
        bd.OnClick(() => Speech.Say(r.W.Kana));
        return bd;
    }

    public void OnShown()
    {
        var g = new Grid { Margin = Shell.Compact ? new Thickness(12, 8, 12, 12) : new Thickness(40, 12, 40, 16), MaxWidth = 1240 };
        for (int i = 0; i < 3; i++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(Shell.Compact ? new RowDefinition { Height = GridLength.Auto } : new RowDefinition());

        bool themes = _level == "Темы";
        var words = themes ? Themes.Words(null).ToArray() : Db.Words.Where(w => w.Level == _level).ToArray();
        int learning = words.Count(w => ProgressStore.Card(w.Id)?.Reps > 0);
        int mastered = words.Count(w => ProgressStore.Card(w.Id)?.Mastered == true);
        var lv = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var l in new[] { "N5", "N4", "N3", "N2", "1000", "Темы", "Мои" })
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = l == "Мои" ? "Мои слова" : l == "1000" ? "1000+ частых" : l, GroupName = "wlevel", IsChecked = _level == l };
            var ll = l;
            rb.Checked += (_, _) => { _level = ll; _topic = null; if (ll is not ("Мои" or "Темы" or "1000")) { ProgressStore.P.Level = ll; ProgressStore.Notify(); } OnShown(); };
            lv.Children.Add(rb);
        }
        if (_topic != null) { var art = Emoji.Img(Emoji.ForTopic(_topic), 64); art.HorizontalAlignment = HorizontalAlignment.Right; art.VerticalAlignment = VerticalAlignment.Top; art.Margin = new Thickness(0, 0, 330, 0); g.Children.Add(art); }
        g.Children.Add(U.Header("語", "Слова", _level == "Мои" ? $"{words.Length} слов, добавленных из словаря" : themes ? $"Сквозные темы для всех уровней — {words.Length} слов: выбери тему ниже" : _level == "1000" ? $"{words.Length} частых слов из живой речи — у каждого пример фразы" : _level == "N3" ? $"{words.Length} слов уровня N3 (частотные 1001–1600) с примерами фраз" : _level == "N2" ? $"{words.Length} слов начала N2 (частотные 1601–2200) с примерами фраз" : $"{words.Length} учебных слов уровня {_level} с озвучкой и карточками  ·  {learning} в изучении  ·  {mastered} выучено", lv));

        // Действия
        int due = words.Count(w => ProgressStore.IsDue(w.Id));
        var acts = new WrapPanel();
        acts.Children.Add(U.Btn($"Учить новые ({ProgressStore.P.NewPerSession})", LearnNew, "Primary", "").M(0, 0, 10, 10));
        var rev = U.Btn($"Повторить ({due})", () => Shell.Open(new FlashSession("Повторение слов", words.Where(w => ProgressStore.IsDue(w.Id)).Shuffled().Select(FlashSession.FromWord))), "", "").M(0, 0, 10, 10);
        rev.IsEnabled = due > 0;
        acts.Children.Add(rev);
        acts.Children.Add(U.Btn(Shell.Compact ? "Перевод" : "Квиз: перевод", () => Quiz(0), "", "").M(0, 0, 10, 10));
        acts.Children.Add(U.Btn(Shell.Compact ? "Обратный" : "Квиз: обратный", () => Quiz(1), "", "").M(0, 0, 10, 10));
        acts.Children.Add(U.Btn(Shell.Compact ? "На слух" : "Аудирование", () => Quiz(2), "", "").M(0, 0, 10, 10));
        acts.Children.Add(U.Btn(Shell.Compact ? "Ромадзи" : "Письмо (ромадзи)", () => Quiz(3), "", "").M(0, 0, 10, 10));
        Control actBody = acts;
        if (Shell.Compact)
        {
            // телефон: главное — в строку, четыре квиза — сеткой 2×2 (столбиком они занимали полэкрана)
            var all = acts.Children.ToList(); acts.Children.Clear();
            var top = new WrapPanel(); var quiz = new TileGrid { MinTile = 140, MaxColumns = 2 };
            foreach (var c in all.Take(2)) top.Children.Add(c);
            foreach (var c in all.Skip(2)) { c.Margin = new Thickness(0, 0, 8, 8); c.HorizontalAlignment = HorizontalAlignment.Stretch; quiz.Children.Add(c); }
            actBody = U.Stack(top, quiz);
        }
        var actCard = U.Card(actBody, Shell.Compact ? new Thickness(12, 12, 4, 4) : new Thickness(18, 18, 8, 8));
        g.Children.Add(actCard.RowAt(1));

        // Фильтр по темам + поиск
        var filter = Shell.Compact ? new Grid { RowDefinitions = new RowDefinitions("auto,auto") } : U.Cols("*", "auto");
        filter.Margin = new Thickness(0, 18, 0, 12);
        var chips = new WrapPanel();
        foreach (var t in new string?[] { null }.Concat(themes ? Themes.All.Select(x => x.name) : words.Select(w => w.Topic).Distinct()))
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = t ?? "Все темы", GroupName = "topic", IsChecked = _topic == t, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 6, 14, 6) };
            var topic = t;
            rb.Checked += (_, _) => { _topic = topic; Refill(); };
            chips.Children.Add(rb);
        }
        filter.Children.Add(Shell.Compact ? chips.RowAt(1).M(0, 10, 0, 0) : chips);
        var search = SearchBox();
        filter.Children.Add(Shell.Compact ? search : search.Col(1));
        g.Children.Add(filter.RowAt(2));

        var listWrap = U.Cols("*");
        if (_list.Parent is Panel oldParent) oldParent.Children.Remove(_list); // список переиспользуется между показами
        if (_plainList.Parent is Panel oldPlain) oldPlain.Children.Remove(_plainList);
        listWrap.Children.Add(Shell.Compact ? _plainList : _list);
        _countText.HorizontalAlignment = HorizontalAlignment.Right; _countText.VerticalAlignment = VerticalAlignment.Bottom; _countText.IsHitTestVisible = false;
        g.Children.Add(listWrap.RowAt(3));
        Content = Shell.Compact ? new ScrollViewer { Content = g, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } : g;
        Refill();
        Anim.Stagger(g, 60, 0, 4);
    }

    Control SearchBox()
    {
        var box = new TextBox { Width = Shell.Compact ? double.NaN : 240, FontSize = 14, Padding = new Thickness(36, 8, 12, 8), Text = _search, VerticalAlignment = VerticalAlignment.Top };
        var ph = U.T("Поиск: русский, кана, romaji", "Muted", 13).Also(t => { t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis; });
        ph.IsHitTestVisible = false; ph.Margin = new Thickness(38, 9, 0, 0); ph.VerticalAlignment = VerticalAlignment.Top;
        ph.IsVisible = _search.Length == 0 ? true : false;
        box.TextChanged += (_, _) =>
        {
            _search = (box.Text ?? "").Trim();
            ph.IsVisible = _search.Length == 0 ? true : false;
            Refill();
        };
        var g = new Grid();
        g.Children.Add(box);
        g.Children.Add(ph);
        g.Children.Add(new TextBlock { Text = "", FontFamily = U.R<FontFamily>("Icons"), Foreground = U.B("Muted"), Margin = new Thickness(13, 11, 0, 0), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top });
        return g;
    }

    IEnumerable<Word> Filtered()
    {
        IEnumerable<Word> q = _level == "Темы" ? Themes.Words(_topic) : Db.Words.Where(w => w.Level == _level);
        if (_topic != null && _level != "Темы") q = q.Where(w => w.Topic == _topic);
        if (_search.Length > 0)
        {
            var s = _search.ToLowerInvariant();
            q = q.Where(w => w.Ru.Contains(s, StringComparison.OrdinalIgnoreCase) || w.Jp.Contains(s) || w.Kana.Contains(s) || w.Romaji.Contains(s));
        }
        return q;
    }

    void Refill()
    {
        var rows = Filtered().Select(w =>
        {
            var c = ProgressStore.Card(w.Id);
            return c == null ? new WordRow(w, "новое", U.B("Faint"))
                : c.Mastered ? new WordRow(w, "выучено · повтор через " + ProgressStore.FormatInterval((c.Due - DateTime.Now).TotalDays), U.B("Good"))
                : new WordRow(w, c.Due <= DateTime.Now ? "пора повторить" : "в изучении · через " + ProgressStore.FormatInterval(Math.Max(0, (c.Due - DateTime.Now).TotalDays)), c.Due <= DateTime.Now ? U.B("Accent") : U.B("Gold"));
        }).ToList();
        if (!Shell.Compact) { _list.ItemsSource = rows; return; }
        _rows = rows; _shown = 40;
        RenderPlain();
    }

    void RenderPlain()
    {
        _plainList.Children.Clear();
        foreach (var r in _rows.Take(_shown)) _plainList.Children.Add(RowView(r));
        if (_rows.Count > _shown)
        {
            int left = _rows.Count - _shown;
            var more = U.Btn($"Показать ещё {Math.Min(40, left)} (осталось {left})", () => { _shown += 40; RenderPlain(); }, "", "");
            more.HorizontalAlignment = HorizontalAlignment.Stretch;
            _plainList.Children.Add(more.M(0, 4, 0, 12));
        }
        if (_rows.Count == 0) _plainList.Children.Add(U.T("Ничего не найдено", "Muted").M(4, 8, 0, 0));
    }

    void LearnNew()
    {
        var fresh = Filtered().Where(w => ProgressStore.Card(w.Id) == null).Take(ProgressStore.P.NewPerSession).ToList();
        if (fresh.Count == 0) { Shell.Toast("В этой теме все слова уже начаты!", "完了"); return; }
        Shell.Open(LearnSession.Words("Новые слова", fresh, OnShown));
    }

    void Quiz(int mode)
    {
        // Квиз — только по уже изученным словам; новые сначала проходят знакомство
        var pool = Filtered().Where(w => ProgressStore.Card(w.Id) != null).ToList();
        if (pool.Count < 4) pool = Db.Words.Where(w => ProgressStore.Card(w.Id) != null).ToList();
        if (pool.Count < 4) { Shell.Toast("Сначала выучи хотя бы 4 слова — кнопка «Учить новые»", "学"); return; }
        Shell.Open(QuizFor(pool, mode));
    }

    /// <summary>0 — jp→ru, 1 — ru→jp, 2 — на слух, 3 — ввод ромадзи.</summary>
    public static QuizSession QuizFor(List<Word> pool, int mode)
    {
        if (pool.Count < 4) pool = pool.Concat(Db.Words.Where(w => w.Level == "N5").Take(20)).ToList();
        var chosen = pool.OrderBy(w => (ProgressStore.Card(w.Id) == null ? 1 : 0) + U.Rng.NextDouble() * 1.5).Take(15).ToList();
        var ruPool = pool.Select(w => w.Ru).ToArray();
        var jpPool = pool.Select(w => w.Jp).ToArray();
        var qs = chosen.Select(w => mode switch
        {
            1 => new QuizQ { Prompt = w.Ru, PromptJp = false, PromptSize = 32, Sub = "Выбери японское слово", Answer = w.Jp, OptionsJp = true, Options = QuizSession.MakeOptions(w.Jp, jpPool), Speak = w.Kana, Explain = $"{w.Jp} — {w.Kana} ({w.Romaji})", Key = w.Id },
            2 => new QuizQ { Prompt = "聞いて", PromptSize = 56, Sub = "Послушай и выбери перевод · 🔊 повторить", Answer = w.Ru, Options = QuizSession.MakeOptions(w.Ru, ruPool), Speak = w.Kana, SpeakOnShow = true, Explain = $"{w.Jp} — {w.Kana} ({w.Romaji})", Key = w.Id },
            3 => new QuizQ { Prompt = w.Jp, PromptSize = w.Jp.Length > 5 ? 52 : 76, Sub = $"«{w.Ru}» — напиши чтение латиницей", Typing = true, Answer = w.Romaji.Replace("'", "") + "|" + w.Romaji, Speak = w.Kana, Explain = $"{w.Kana} — {w.Romaji}", Key = w.Id },
            _ => new QuizQ { Prompt = w.Jp, PromptSize = w.Jp.Length > 5 ? 52 : 76, Sub = w.HasKanji ? $"{w.Kana} · {w.Romaji}" : w.Romaji, Answer = w.Ru, Options = QuizSession.MakeOptions(w.Ru, ruPool), Speak = w.Kana, Key = w.Id },
        });
        return new QuizSession(mode switch { 1 => "Обратный перевод", 2 => "Аудирование", 3 => "Письмо", _ => "Перевод" }, qs);
    }
}
