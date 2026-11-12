using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Matsubakira.Services;

namespace Matsubakira.Controls;

/// <summary>Что изучаем: слово или кандзи — всё, что нужно показать до проверки.</summary>
public sealed class LearnItem
{
    public string Id = "", Front = "", Reading = "", Romaji = "", Meaning = "", Speak = "";
    public bool IsKanji, IsKana;
    public string? On, Kun, Mnemonic, Topic;
    public string[][] Parts = Array.Empty<string[]>();
    public Example[] Examples = Array.Empty<Example>();

    public static LearnItem From(Word w) => new()
    {
        Id = w.Id, Front = w.Jp, Reading = w.Kana, Romaji = w.Romaji, Meaning = w.Ru, Speak = w.Kana, Topic = w.Topic,
        Examples = w.ExJp.Length > 0 ? new[] { new Example(w.ExJp, "", w.ExRu) } : Array.Empty<Example>(),
    };

    public static LearnItem From(KanaCell c, bool kata) => new()
    {
        Id = (kata ? "k:" : "h:") + c.Hira, Front = kata ? c.Kata : c.Hira, Reading = kata ? c.Kata : c.Hira, Romaji = c.Romaji,
        Meaning = c.Romaji, Speak = c.Hira, IsKana = true,
        // пример: короткое частое слово, которое начинается с этого знака
        Examples = Db.Words.Where(w => w.Level == "N5" && w.Kana.Length <= 5 && (kata ? w.Jp.StartsWith(c.Kata) : w.Kana.StartsWith(c.Hira) && !w.Jp.Any(ch => ch >= 'ァ' && ch <= 'ヶ')))
            .Take(2).Select(w => new Example(w.Jp, w.Kana, w.Ru)).ToArray(),
    };

    public static LearnItem From(KanjiInfo k) => new()
    {
        Id = k.Id, Front = k.Char, IsKanji = true, Meaning = k.Ru, On = k.On, Kun = k.Kun.Length > 0 ? k.Kun : null,
        Reading = k.Kun.Length > 0 ? k.Kun : k.On, Speak = k.Examples.Length > 0 ? k.Examples[0].Kana : k.Char,
        Mnemonic = Mnemonics.Hint(k.Char, k.Ru) is { Length: > 0 } h ? h : null,
        Parts = Mnemonics.For(k.Char)?.Parts ?? Array.Empty<string[]>(), Examples = k.Examples,
    };
}

/// <summary>
/// Урок «как в Duolingo»: сначала обзор всех новых знаков, затем знакомство с каждым по очереди
/// (порядок черт, чтения, подсказка-мнемоника, примеры с озвучкой), и только потом — проверка
/// исключительно по тому, что только что показали. Ошибки возвращаются, пока всё не будет верно.
/// </summary>
public sealed class LearnSession : Grid, IPage, IKeyTarget
{
    public string Title { get; }
    readonly List<LearnItem> _items;
    readonly Action? _done;
    int _i = -1;                 // -1 — обзор, Count — итоговый обзор перед проверкой
    Control? _quiz;
    readonly Func<Control>? _customQuiz;
    bool Bulk => _items.Count > 15; // больше 15 знаков: обзор таблицей, без показа по одному
    readonly ContentControl _body = new();
    readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };

    public LearnSession(string title, IEnumerable<LearnItem> items, Action? done = null, Func<Control>? quiz = null)
    {
        Title = title;
        _items = items.ToList();
        _done = done;
        _customQuiz = quiz;
        Margin = new Thickness(40, 8, 40, 24);
        Children.Add(U.Page(new StackPanel { MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Center, Children = { _dots, _body } }));
        Render();
    }

    public static LearnSession Kanji(IEnumerable<KanjiInfo> ks, Action? done = null) => new("Новые кандзи", ks.Select(LearnItem.From), done);
    public static LearnSession Words(string title, IEnumerable<Word> ws, Action? done = null) => new(title, ws.Select(LearnItem.From), done);

    string Noun(int n) => _items.All(x => x.IsKana) ? MainView.Plural(n, "знак", "знака", "знаков") : _items.All(x => x.IsKanji)
        ? MainView.Plural(n, "иероглиф", "иероглифа", "иероглифов")
        : MainView.Plural(n, "слово", "слова", "слов");

    void Render()
    {
        _dots.Children.Clear();
        for (int k = 0; k < (Bulk ? 0 : _items.Count); k++)
            _dots.Children.Add(new Border
            {
                Width = k == _i ? 22 : 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(3),
                Background = k == _i ? U.B("Accent") : k < _i || _i >= _items.Count ? U.B("Good") : U.B("Surface3"),
            });
        Control view = _i < 0 ? Overview(false) : _i >= _items.Count ? Overview(true) : ItemCard(_items[_i]);
        _body.Content = view;
        Anim.FadeUp(view, 0, 14, 300);
        if (_i >= 0 && _i < _items.Count && ProgressStore.AutoSpeakOn) Speech.Say(_items[_i].Speak);
    }

    // ─────────── Обзор: все знаки урока сразу ───────────
    Control Overview(bool final)
    {
        var sp = new StackPanel();
        sp.Children.Add(Art.Say(final
                ? "Отлично! Взгляни на них ещё раз все вместе — и проверим, что запомнилось. Ошибки не страшны: я покажу их снова."
                : Bulk ? $"Здесь {_items.Count} {Noun(_items.Count)}. Нажимай на любой — услышишь, как он звучит. Когда будешь готов, начнём проверку."
                : $"Сегодня {_items.Count} {Noun(_items.Count)}. Сначала просто посмотри на все, потом разберём каждый по очереди — и только после этого проверка.",
            final ? Mood.Joy : Mood.Happy, 120).M(0, 0, 0, 18));
        var grid = new UniformGrid { Columns = U.Columns(_items.Count <= 5 ? _items.Count : _items.Count <= 6 ? 3 : 5, 2), Margin = new Thickness(-6, 0, -6, 0) };
        bool kana = _items.All(x => x.IsKana);
        for (int k = 0; k < _items.Count; k++)
        {
            var it = _items[k];
            int idx = k;
            var tile = new Border
            {
                Classes = { "Card" }, Margin = new Thickness(6), Padding = new Thickness(12, 16), Cursor = Cursors.Hand,
                Child = U.Stack(
                    U.Jp(it.Front, it.IsKanji || it.IsKana ? (Bulk ? 40 : 56) : it.Front.Length > 4 ? 26 : 34, null, true).Center(),
                    U.Jp(it.Reading, 13, U.B("Accent")).Center().M(0, 6, 0, 0).Also(t => t.IsVisible = it.Reading != it.Front),
                    U.T(it.Meaning, "Body", 13).Center().M(0, 4, 0, 0).Also(t => { t.TextAlignment = TextAlignment.Center; t.TextWrapping = TextWrapping.Wrap; t.MaxHeight = 40; })),
            }.Tip("Нажми — послушать");
            tile.PointerReleased += (_, _) => Speech.Say(it.Speak);
            Views.HomePage.Hover(tile);
            grid.Children.Add(tile);
        }
        sp.Children.Add(grid);
        Anim.Stagger(grid, 40, 80);
        var acts = U.Row(10,
            final && !Bulk ? U.Btn("Посмотреть ещё раз", () => { _i = 0; Render(); }, "", "") : new Border(),
            U.Btn(final || Bulk ? "Проверить себя  →" : "Изучить по одному  →", Bulk ? StartQuiz : Next, "Primary", ""));
        acts.HorizontalAlignment = HorizontalAlignment.Center; acts.Margin = new Thickness(0, 22, 0, 0);
        sp.Children.Add(acts);
        sp.Children.Add(U.T("Пробел / → — дальше", "Muted", 12).Center().M(0, 10, 0, 0).DesktopOnly());
        return sp;
    }

    // ─────────── Знакомство с одним знаком ───────────
    Control ItemCard(LearnItem it)
    {
        var g = U.Cols("auto", "28", "*");
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (it.IsKanji || it.IsKana)
        {
            var sv = new StrokeView(it.Front, 230) { HorizontalAlignment = HorizontalAlignment.Center };
            left.Children.Add(sv);
            if (sv.HasData)
            {
                sv.Loaded += (_, _) => sv.Play(1.2);
                var ctl = U.Row(6, U.Btn("Порядок черт", () => sv.Play(), "", ""), U.Btn("Медленно", () => sv.Play(0.5), "Ghost", ""));
                ctl.HorizontalAlignment = HorizontalAlignment.Center; ctl.Margin = new Thickness(0, 10, 0, 0);
                left.Children.Add(ctl);
            }
            else left.Children.Add(U.Jp(it.Front, 150, null, true).Center());
        }
        else
        {
            if (it.Topic != null && Emoji.ForTopic(it.Topic) is { Length: > 0 } art) left.Children.Add(Emoji.Img(art, 64).Center().M(0, 0, 0, 12));
            left.Children.Add(U.Jp(it.Front, it.Front.Length > 5 ? 52 : 76, null, true).Center());
            if (it.Reading != it.Front) left.Children.Add(U.Jp(it.Reading, 22, U.B("Accent")).Center().M(0, 6, 0, 0));
            if (ProgressStore.P.ShowRomaji && it.Romaji.Length > 0) left.Children.Add(U.T(it.Romaji, "Muted", 14).Center());
            if (Pitch.Pattern(it.Front, it.Reading) is { } pat)
                left.Children.Add(new AccentView(pat) { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
            left.MinWidth = 260;
        }
        g.Children.Add(left);

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var head = U.Cols("*", "auto");
        head.Children.Add(it.IsKana ? U.T(it.Meaning, "H1", 64, U.B("Accent")) : U.T(it.Meaning, "H1", 28).Also(t => t.TextWrapping = TextWrapping.Wrap));
        head.Children.Add(U.Row(4, U.SpeakBtn(() => it.Speak, true), U.SpeakBtn(() => it.Speak)).Col(1));
        right.Children.Add(head);
        if (it.IsKana)
            right.Children.Add(U.T($"Читается «{it.Romaji}». Нажми на динамик и повтори вслух, затем обведи взглядом порядок черт.", "Muted", 14).Also(t => t.TextWrapping = TextWrapping.Wrap).M(0, 10, 0, 0));
        if (it.IsKanji)
        {
            var rd = U.Row(18,
                U.Stack(U.T("ОН · китайское", "Overline"), U.Jp(it.On ?? "—", 18, U.B("Accent2")).M(0, 3, 0, 0)),
                U.Stack(U.T("КУН · японское", "Overline"), U.Jp(it.Kun ?? "—", 18, U.B("Accent")).M(0, 3, 0, 0)));
            right.Children.Add(rd.M(0, 12, 0, 0));
            if (it.Parts.Length > 0)
            {
                var parts = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
                foreach (var part in it.Parts)
                    parts.Children.Add(new Border
                    {
                        Background = U.B("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 4), Margin = new Thickness(0, 0, 6, 6),
                        Child = U.Row(6, U.Jp(part[0], 18, U.B("Accent"), true).CenterV(), U.T(part.Length > 1 && part[1].Length > 0 ? part[1] : "часть", "Muted", 12).CenterV()),
                    });
                right.Children.Add(parts);
            }
        }
        if (it.Mnemonic != null)
            right.Children.Add(new Border
            {
                Background = U.B("AccentSoft"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10), Margin = new Thickness(0, 10, 0, 0),
                Child = U.Row(10, Art.Glyph("sparkle", 18, U.B("Gold")), U.T("Как запомнить: " + it.Mnemonic, "Body", 14).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 440; })),
            });
        if (it.Examples.Length > 0)
        {
            right.Children.Add(U.T("ПРИМЕРЫ", "Overline").M(0, 16, 0, 6));
            foreach (var ex in it.Examples.Take(3))
            {
                var row = U.Cols("*", "auto");
                row.Children.Add(U.Stack(U.Jp(ex.Jp, 18),
                    U.T(ex.Kana.Length > 0 ? $"{ex.Kana} · {ex.Ru}" : ex.Ru, "Muted", 12.5).Also(t => t.TextWrapping = TextWrapping.Wrap)));
                row.Children.Add(U.SpeakBtnSmall(ex.Kana.Length > 0 ? ex.Kana : ex.Jp).Col(1));
                right.Children.Add(new Border { Background = U.B("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8, 8, 8), Margin = new Thickness(0, 0, 0, 6), Child = row });
            }
        }
        g.Children.Add(right.Col(2));

        var card = U.Card(U.Stackify(g), new Thickness(28, 24, 28, 24));
        var nav = U.Cols("auto", "*", "auto");
        nav.Margin = new Thickness(0, 18, 0, 0);
        nav.Children.Add(U.Btn("Назад", () => { _i--; Render(); }, "Ghost", ""));
        nav.Children.Add(U.T($"{_i + 1} из {_items.Count}", "Muted", 13).Center().CenterV().Col(1));
        nav.Children.Add(U.Btn(_i + 1 < _items.Count ? "Запомнил  →" : "Все просмотрены  →", Next, "Primary", "").Col(2));
        return U.Stack(card, nav);
    }

    internal LearnSession Skip(int n) { for (int k = 0; k < n; k++) Next(); return this; }

    void Next()
    {
        if (_i >= _items.Count || Bulk) { StartQuiz(); return; }
        _i++;
        Render();
    }

    // ─────────── Проверка только по изученным ───────────
    void StartQuiz()
    {
        if (_customQuiz != null)
        {
            _quiz = _customQuiz();
            Children.Clear();
            Children.Add(_quiz);
            Margin = new Thickness(0);
            return;
        }
        var wrong = new HashSet<string>();
        var meanings = _items.Select(x => x.Meaning).ToArray();
        var fronts = _items.Select(x => x.Front).ToArray();
        var readings = _items.Select(x => x.Reading).Distinct().ToArray();
        // если знаков мало — добавим отвлекающих вариантов из того же типа
        var poolRu = meanings.Concat(_items.All(x => x.IsKanji) ? Db.Kanji.Select(k => k.Ru) : Db.Words.Select(w => w.Ru)).Distinct().ToArray();
        var poolFront = fronts.Concat(_items.All(x => x.IsKanji) ? Db.Kanji.Select(k => k.Char) : Db.Words.Select(w => w.Jp)).Distinct().ToArray();
        var poolKana = readings.Concat(Db.Words.Select(w => w.Kana)).Distinct().ToArray();
        string[] Opts(string ans, string[] local, string[] pool) =>
            QuizSession.MakeOptions(ans, local.Length >= 4 ? local : local.Concat(pool.Shuffled().Take(12)).Distinct());

        var easy = new List<QuizQ>();
        var qs = new List<QuizQ>();
        foreach (var it in _items)
        {
            var key = it.Id;
            Action<bool> mark = ok => { if (!ok) wrong.Add(key); };
            easy.Add(new QuizQ
            {
                Prompt = it.Front, PromptSize = it.IsKanji ? 120 : it.Front.Length > 5 ? 56 : 80, Sub = "Что это значит?", Answer = it.Meaning,
                Options = Opts(it.Meaning, meanings, poolRu), Speak = it.Speak, Key = key, OnResult = mark,
                Explain = it.Reading != it.Front ? $"{it.Reading} — {it.Meaning}" : it.Meaning,
            });
            qs.Add(new QuizQ
            {
                Prompt = it.Meaning, PromptSize = 34, PromptJp = false, Sub = it.IsKanji ? "Найди этот иероглиф" : "Как это будет по-японски?", Answer = it.Front,
                Options = Opts(it.Front, fronts, poolFront), OptionsJp = true, Speak = it.Speak, Key = key, OnResult = mark,
            });
            if (!it.IsKanji && !ProgressStore.P.QuietMode) // в тихом режиме — без вопросов на слух
                qs.Add(new QuizQ
                {
                    Prompt = "🔊", PromptSize = 64, PromptJp = false, Sub = "Послушай и выбери перевод (R — ещё раз)", Answer = it.Meaning, SpeakOnShow = true,
                    Options = Opts(it.Meaning, meanings, poolRu), Speak = it.Speak, Key = key, OnResult = mark,
                });
            else if (it.Examples.Length > 0)
            {
                var ex = it.Examples[0];
                qs.Add(new QuizQ
                {
                    Prompt = ex.Jp, PromptSize = 60, Sub = "Как читается это слово?", Answer = ex.Kana, OptionsJp = true,
                    Options = Opts(ex.Kana, _items.SelectMany(x => x.Examples.Take(1)).Select(e => e.Kana).Distinct().ToArray(), poolKana),
                    Speak = ex.Kana, Explain = $"{ex.Kana} — {ex.Ru}", Key = key, OnResult = mark,
                });
            }
        }
        // сначала простые вопросы (значение), потом обратные — от узнавания к воспроизведению
        var ordered = easy.Shuffled().Concat(qs.Shuffled()).ToList();
        _quiz = new QuizSession(Title, ordered, (_, _) =>
        {
            foreach (var it in _items)
                if (ProgressStore.Card(it.Id) == null) ProgressStore.Rate(it.Id, wrong.Contains(it.Id) ? Rating.Hard : Rating.Good);
            ProgressStore.Notify();
            _done?.Invoke();
        });
        Children.Clear();
        Children.Add(_quiz);
        Margin = new Thickness(0);
    }

    public bool HandleKey(Key key)
    {
        if (_quiz != null) return _quiz is IKeyTarget kt && kt.HandleKey(key);
        if (key is Key.Space or Key.Enter or Key.Right) { Next(); return true; }
        if (key == Key.Left && _i >= 0) { _i--; Render(); return true; }
        if (key == Key.R && _i >= 0 && _i < _items.Count) { Speech.Say(_items[_i].Speak); return true; }
        return false;
    }
}
