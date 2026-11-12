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

/// <summary>Спряжение японских глаголов по правилам групп (годан / итидан / неправильные).</summary>
public static class Conj
{
    static readonly HashSet<string> GodanRu = new() { "帰る", "入る", "走る", "知る", "切る", "要る", "減る", "喋る", "滑る", "蹴る", "焦る", "限る", "参る", "握る", "散る", "茂る" };
    const string U_ = "うくぐすつぬぶむる", I_ = "いきぎしちにびみり", A_ = "わかがさたなばまら", E_ = "えけげせてねべめれ", O_ = "おこごそとのぼもろ";
    static readonly string IRow = "いきぎしじちぢにひびぴみりゐ", ERow = "えけげせぜてでねへべぺめれゑ";

    public static readonly (string key, string name)[] Forms =
    {
        ("masu", "вежливая 〜ます"), ("masen", "отрицание 〜ません"), ("mashita", "прошедшее 〜ました"),
        ("te", "て-форма"), ("ta", "た-форма (прош.)"), ("nai", "ない-форма"), ("nakatta", "〜なかった"),
        ("tai", "хочу 〜たい"), ("pot", "могу (потенц.)"), ("vol", "давай (волевая)"), ("ba", "если 〜ば"),
    };

    public static string? Group(string jp, string kana)
    {
        if (jp.EndsWith("する")) return "suru";
        if (jp is "来る" or "くる") return "kuru";
        if (jp.Length < 2 || !U_.Contains(jp[^1])) return null;
        if (jp[^1] == 'る' && !GodanRu.Contains(jp) && kana.Length >= 2 && (IRow.Contains(kana[^2]) || ERow.Contains(kana[^2]))) return "ichi";
        return "godan";
    }

    public static Dictionary<string, string>? All(string jp, string kana)
    {
        var g = Group(jp, kana);
        if (g == null) return null;
        var f = new Dictionary<string, string>();
        if (g == "suru")
        {
            var st = jp[..^2];
            foreach (var (k, v) in new[] { ("masu", "します"), ("masen", "しません"), ("mashita", "しました"), ("te", "して"), ("ta", "した"), ("nai", "しない"), ("nakatta", "しなかった"), ("tai", "したい"), ("pot", "できる"), ("vol", "しよう"), ("ba", "すれば") })
                f[k] = st + v;
            return f;
        }
        if (g == "kuru")
        {
            bool kj = jp == "来る";
            string K(string kanaForm) => kj ? "来" + kanaForm[1..] : kanaForm;
            foreach (var (k, v) in new[] { ("masu", "きます"), ("masen", "きません"), ("mashita", "きました"), ("te", "きて"), ("ta", "きた"), ("nai", "こない"), ("nakatta", "こなかった"), ("tai", "きたい"), ("pot", "こられる"), ("vol", "こよう"), ("ba", "くれば") })
                f[k] = K(v);
            return f;
        }
        string stem = jp[..^1];
        if (g == "ichi")
        {
            foreach (var (k, v) in new[] { ("masu", "ます"), ("masen", "ません"), ("mashita", "ました"), ("te", "て"), ("ta", "た"), ("nai", "ない"), ("nakatta", "なかった"), ("tai", "たい"), ("pot", "られる"), ("vol", "よう"), ("ba", "れば") })
                f[k] = stem + v;
            return f;
        }
        int i = U_.IndexOf(jp[^1]);
        char ii = I_[i], aa = A_[i], ee = E_[i], oo = O_[i];
        string te = jp[^1] switch { 'う' or 'つ' or 'る' => "って", 'む' or 'ぶ' or 'ぬ' => "んで", 'く' => "いて", 'ぐ' => "いで", _ => "して" };
        if (jp is "行く" or "いく") te = "って";
        string ta = te[..^1] + (te[^1] == 'で' ? "だ" : "た");
        f["masu"] = stem + ii + "ます"; f["masen"] = stem + ii + "ません"; f["mashita"] = stem + ii + "ました";
        f["te"] = stem + te; f["ta"] = stem + ta;
        f["nai"] = jp is "ある" ? "ない" : stem + aa + "ない"; f["nakatta"] = jp is "ある" ? "なかった" : stem + aa + "なかった";
        f["tai"] = stem + ii + "たい"; f["pot"] = stem + ee + "る"; f["vol"] = stem + oo + "う"; f["ba"] = stem + ee + "ば";
        return f;
    }

    public static QuizSession Quiz()
    {
        var verbs = Db.Words.Where(w => (w.Topic.StartsWith("Глаголы") && w.Level is "N5" or "N4") && All(w.Jp, w.Kana) != null).ToList();
        var qs = verbs.Shuffled().Take(15).Select(w =>
        {
            var forms = All(w.Jp, w.Kana)!;
            var (key, name) = Conj.Forms[U.Rng.Next(Conj.Forms.Length)];
            var correct = forms[key];
            var wrong = forms.Where(kv => kv.Key != key && kv.Value != correct).Select(kv => kv.Value).Distinct().Shuffled().Take(3);
            return new QuizQ
            {
                Prompt = w.Jp, PromptSize = 60, Sub = $"→ {name}   ·   «{w.Ru}»", Answer = correct, OptionsJp = true,
                Options = wrong.Append(correct).Shuffled().ToArray(), Speak = correct, Key = w.Id,
                Explain = $"{w.Jp} → {correct}  ({Kana.ToRomaji(correct)})",
            };
        });
        return new QuizSession("Спряжение глаголов", qs);
    }
}

/// <summary>Счётные суффиксы с особыми чтениями (っ, ぼん/ぽん и т.п.).</summary>
public static class Counters
{
    public static readonly (string c, string use, string[] r)[] Data =
    {
        ("本", "длинные: бутылки, ручки, зонты", new[] { "いっぽん", "にほん", "さんぼん", "よんほん", "ごほん", "ろっぽん", "ななほん", "はっぽん", "きゅうほん", "じゅっぽん" }),
        ("枚", "плоские: листы, билеты, рубашки", new[] { "いちまい", "にまい", "さんまい", "よんまい", "ごまい", "ろくまい", "ななまい", "はちまい", "きゅうまい", "じゅうまい" }),
        ("匹", "мелкие животные: кошки, рыбы", new[] { "いっぴき", "にひき", "さんびき", "よんひき", "ごひき", "ろっぴき", "ななひき", "はっぴき", "きゅうひき", "じゅっぴき" }),
        ("冊", "книги, тетради", new[] { "いっさつ", "にさつ", "さんさつ", "よんさつ", "ごさつ", "ろくさつ", "ななさつ", "はっさつ", "きゅうさつ", "じゅっさつ" }),
        ("台", "машины, техника", new[] { "いちだい", "にだい", "さんだい", "よんだい", "ごだい", "ろくだい", "ななだい", "はちだい", "きゅうだい", "じゅうだい" }),
        ("個", "мелкие предметы: яблоки, яйца", new[] { "いっこ", "にこ", "さんこ", "よんこ", "ごこ", "ろっこ", "ななこ", "はっこ", "きゅうこ", "じゅっこ" }),
        ("人", "люди", new[] { "ひとり", "ふたり", "さんにん", "よにん", "ごにん", "ろくにん", "ななにん", "はちにん", "きゅうにん", "じゅうにん" }),
        ("つ", "универсальный счёт", new[] { "ひとつ", "ふたつ", "みっつ", "よっつ", "いつつ", "むっつ", "ななつ", "やっつ", "ここのつ", "とお" }),
        ("杯", "чашки, стаканы, миски", new[] { "いっぱい", "にはい", "さんばい", "よんはい", "ごはい", "ろっぱい", "ななはい", "はっぱい", "きゅうはい", "じゅっぱい" }),
        ("回", "разы", new[] { "いっかい", "にかい", "さんかい", "よんかい", "ごかい", "ろっかい", "ななかい", "はっかい", "きゅうかい", "じゅっかい" }),
        ("歳", "возраст", new[] { "いっさい", "にさい", "さんさい", "よんさい", "ごさい", "ろくさい", "ななさい", "はっさい", "きゅうさい", "はたち/じゅっさい" }),
        ("分", "минуты", new[] { "いっぷん", "にふん", "さんぷん", "よんぷん", "ごふん", "ろっぷん", "ななふん", "はっぷん", "きゅうふん", "じゅっぷん" }),
    };

    static readonly string[] Nums = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };

    public static QuizSession Quiz()
    {
        var qs = Enumerable.Range(0, 15).Select(_ =>
        {
            var (c, use, r) = Data[U.Rng.Next(Data.Length)];
            int n = U.Rng.Next(10);
            var correct = r[n].Split('/')[0];
            var pool = new List<string>();
            if (n > 0) pool.Add(r[n - 1].Split('/')[0]);
            if (n < 9) pool.Add(r[n + 1].Split('/')[0]);
            var other = Data[U.Rng.Next(Data.Length)].r[n].Split('/')[0];
            pool.Add(other);
            pool.AddRange(Data.Select(d => d.r[n].Split('/')[0]));
            var opts = pool.Where(x => x != correct).Distinct().Shuffled().Take(3).Append(correct).Shuffled().ToArray();
            return new QuizQ { Prompt = Nums[n] + c, PromptSize = 72, Sub = $"Как прочитать? ({use})", Answer = correct, OptionsJp = true, Options = opts, Speak = correct, Explain = $"{Nums[n]}{c} — {correct} ({Kana.ToRomaji(correct)})" };
        });
        return new QuizSession("Счётные суффиксы", qs);
    }

    public static Control Table()
    {
        var sp = new StackPanel();
        foreach (var (c, use, r) in Data)
        {
            var row = U.Cols("60", "*");
            row.Children.Add(U.Stack(U.Jp(c, 26, U.B("Accent"), true), U.T(use, "Muted", 11).Also(t => t.MaxWidth = 60)).M(0, 0, 8, 0));
            var wrap = new WrapPanel();
            for (int i = 0; i < 10; i++)
            {
                var reading = r[i];
                var b = new Button { Content = U.Stack(U.T((i + 1) + c, "Muted", 11).Center(), U.Jp(reading, 14).Center()), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 6, 6) };
                b.Click += (_, _) => Speech.Say(reading.Split('/')[0]);
                wrap.Children.Add(b);
            }
            row.Children.Add(wrap.Col(1));
            sp.Children.Add(U.Card(row, new Thickness(14)).M(0, 0, 0, 8));
        }
        return sp;
    }
}

/// <summary>«Собери предложение»: русская фраза → расставь японские слова по порядку.</summary>
public sealed class BuilderSession : Grid, IPage, IKeyTarget
{
    public string Title => "Собери предложение";
    readonly List<(string[] tokens, string jp, string ru)> _items;
    int _i, _ok;
    readonly List<string> _answer = new();
    readonly WrapPanel _tiles = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly WrapPanel _slots = new() { HorizontalAlignment = HorizontalAlignment.Center, MinHeight = 60 };
    readonly TextBlock _ru = U.T("", "H2", 22);
    readonly TextBlock _fb = U.T("", "H3", 15);
    readonly ProgressBar _bar = new();
    bool _checked;

    public BuilderSession()
    {
        var src = Db.Dialogs.SelectMany(d => d.Lines).Select(l => (l.Kana, l.Jp, l.Ru))
            .Concat(Db.SpeechLessons.SelectMany(l => l.Items).Select(p => (p.Kana, p.Jp, p.Ru)))
            .Select(x => (tokens: x.Kana.Replace("、", " ").Replace("。", "").Replace("！", "").Replace("？", "").Split(' ', StringSplitOptions.RemoveEmptyEntries), x.Jp, x.Ru))
            .Where(x => x.tokens.Length is >= 3 and <= 8).ToList();
        _items = src.Shuffled().Take(10).ToList();
        Margin = new Thickness(40, 10, 40, 24);
        var sp = new StackPanel { MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(_bar.M(0, 0, 0, 24));
        sp.Children.Add(_ru.Center());
        sp.Children.Add(U.T("Нажимай на слова в правильном порядке", "Muted").Center().M(0, 6, 0, 20));
        sp.Children.Add(new Border { Classes = { "Card" }, Child = _slots, MinHeight = 80, Padding = new Thickness(14) });
        sp.Children.Add(_tiles.M(0, 20, 0, 0));
        sp.Children.Add(_fb.Center().M(0, 18, 0, 0));
        var btns = U.Row(10, U.Btn("Сбросить", () => { _answer.Clear(); RenderTiles(); }, "Ghost", ""), U.Btn("Проверить", Check, "Primary", ""), U.Btn("Дальше", Next, "", ""));
        btns.HorizontalAlignment = HorizontalAlignment.Center; btns.Margin = new Thickness(0, 18, 0, 0);
        sp.Children.Add(btns);
        Children.Add(new ScrollViewer { Content = sp });
        Load();
    }

    (string[] tokens, string jp, string ru) Cur => _items[_i];

    void Load()
    {
        _checked = false; _answer.Clear(); _fb.Text = "";
        _bar.Maximum = _items.Count; _bar.Value = _i;
        _ru.Text = Cur.ru;
        RenderTiles();
    }

    void RenderTiles()
    {
        _tiles.Children.Clear(); _slots.Children.Clear();
        var remaining = Cur.tokens.ToList();
        foreach (var a in _answer) remaining.Remove(a);
        foreach (var t in remaining.OrderBy(x => x.GetHashCode() ^ _i))
        {
            var tok = t;
            var b = new Button { Content = U.Jp(tok, 20), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(4) };
            b.Click += (_, _) => { if (_checked) return; _answer.Add(tok); RenderTiles(); Speech.Say(tok); };
            _tiles.Children.Add(b);
        }
        for (int i = 0; i < _answer.Count; i++)
        {
            int idx = i;
            var b = new Button { Content = U.Jp(_answer[i], 20), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(4), Background = U.B("AccentSoft") };
            b.Click += (_, _) => { if (_checked) return; _answer.RemoveAt(idx); RenderTiles(); };
            _slots.Children.Add(b);
        }
    }

    void Check()
    {
        if (_checked || _answer.Count == 0) return;
        _checked = true;
        bool ok = string.Join("", _answer) == string.Join("", Cur.tokens);
        if (ok) { _ok++; Sfx.Ok(); ProgressStore.AddXp(3); } else Sfx.Bad();
        _fb.Text = ok ? "Верно! " + Cur.jp : "Правильно: " + string.Join(" ", Cur.tokens) + "  ·  " + Cur.jp;
        _fb.Foreground = U.B(ok ? "Good" : "Bad");
        Speech.Say(Cur.jp);
    }

    void Next()
    {
        if (!_checked) { Check(); return; }
        if (++_i >= _items.Count)
        {
            Children.Clear();
            Children.Add(Results.Build(_ok * 100 / Math.Max(1, _items.Count), _ok * 3, new[] { ("Предложений", _items.Count.ToString()), ("Собрано верно", _ok.ToString()) }, null, Shell.Back, this));
            Sfx.Done();
            return;
        }
        Load();
    }

    public bool HandleKey(Key key)
    {
        if (key == Key.Enter) { if (_checked) Next(); else Check(); return true; }
        if (key == Key.Back && !_checked && _answer.Count > 0) { _answer.RemoveAt(_answer.Count - 1); RenderTiles(); return true; }
        return false;
    }
}

public sealed class TrainersPage : UserControl, IPage
{
    public string Title => "Тренажёры и игры";

    public void OnShown()
    {
        var root = new StackPanel();
        root.Children.Add(U.Header("練", "Тренажёры и игры", "Игры, план на день, работа над ошибками и упражнения на грамматику, счёт и слух.", art: "coach"));

        var grid = new UniformGrid { Columns = U.Columns(3), Margin = new Thickness(-6, 18, -6, 0) };
        grid.Children.Add(Tile("誤", "Тетрадь ошибок", Mistakes.Count > 0 ? $"{Mistakes.Count} ошибок ждут исправления" : "Ошибок нет — отлично!", () => { var q = MistakesQuiz(); if (q != null) Shell.Open(q); else Shell.Toast("Ошибок пока нет", "完璧"); }));
        grid.Children.Add(Tile("活", "Спряжение глаголов", "11 форм: ます, て, ない, потенциальная…", () => Shell.Open(Conj.Quiz())));
        grid.Children.Add(Tile("数", "Счётные суффиксы", "本・枚・匹・人… с особыми чтениями", () => Shell.Open(new CountersPage())));
        grid.Children.Add(Tile("対", "Минимальные пары", "おばさん или おばあさん? Слух и произношение", () => Shell.Open(new PairsPage())));
        grid.Children.Add(Tile("聞", "Диктант", "Слушай и записывай слово", () => Shell.Open(Dictation())));
        grid.Children.Add(Tile("耳", "Фразы на слух", "Целые фразы в обычном темпе, без текста — что сказали?", () => Shell.Open(PhraseListening())));
        grid.Children.Add(Tile("組", "Собери предложение", "Расставь слова по порядку", () => Shell.Open(new BuilderSession())));
        grid.Children.Add(Tile("書", "Прописи", "Пиши кандзи мышью или пером — проверка порядка черт", () => Shell.Open(new WritingPage())));
        grid.Children.Add(Tile("試", "Пробный тест JLPT", $"30 вопросов уровня {ProgressStore.P.Level}", () => Shell.Open(MockTest(ProgressStore.P.Level))));
        root.Children.Add(U.T("Игры", "H2").M(0, 0, 0, 4));
        root.Children.Add(U.T("Закрепляют уже изученные слова — учиться веселее, а ошибки попадают в тетрадь.", "Muted").M(0, 0, 0, 10));
        var games = new UniformGrid { Columns = U.Columns(3), Margin = new Thickness(-6, 0, -6, 0) };
        games.Children.Add(Game("games", "Пары на скорость", "Соедини слово и перевод — минута на всё", "match", () => Shell.Open(new MatchRushGame())));
        games.Children.Add(Game("words", "Собери слово", "Сложи чтение из слогов каны", "build", () => Shell.Open(new WordBuilderGame())));
        games.Children.Add(Game("voice", "Ловец звуков", "Услышь слово и поймай его первым", "listen", () => Shell.Open(new ListenCatchGame())));
        games.Children.Add(Game("kana", "Кана-дождь", "Печатай чтение падающих знаков", "rain-h", () => Shell.Open(new KanaRainGame(false))));
        games.Children.Add(Game("sparkle", "Найди пару", "Мемори: открой карточки и запомни", "memory", () => Shell.Open(new MemoryGame())));
        games.Children.Add(Game("flame", "Спринт 60 секунд", "Верно или нет — как можно быстрее", "sprint", () => Shell.Open(new SprintGame())));
        root.Children.Add(games);
        root.Children.Add(PlanCard().M(0, 24, 0, 0));
        root.Children.Add(grid.Also(g => g.Margin = new Thickness(-6, 0, -6, 0)));
        root.Children.Insert(root.Children.IndexOf(grid), U.T("Упражнения", "H2").M(0, 26, 0, 12));
        Anim.Stagger(games, 35, 80);
        Content = U.Page(root);
    }

    static readonly Dictionary<string, string> TileArt = new()
    {
        ["誤"] = "memo", ["活"] = "gear", ["数"] = "input_numbers", ["聞"] = "headphone", ["組"] = "puzzle_piece", ["試"] = "graduation_cap",
        ["書"] = "writing_hand", ["雨"] = "cloud_with_rain", ["対"] = "game_die", ["速"] = "stopwatch",
    };

    /// <summary>Плитка игры: своя иконка, описание и рекорд.</summary>
    static Border Game(string icon, string title, string sub, string bestKey, Action go)
    {
        int best = GameUi.Best(bestKey);
        var head = U.Cols("auto", "*");
        head.Children.Add(Art.Icon(icon, 46));
        if (best > 0) head.Children.Add(U.Pill($"рекорд {best}", U.B("AccentSoft"), U.B("Accent")).Also(p => { p.HorizontalAlignment = HorizontalAlignment.Right; p.VerticalAlignment = VerticalAlignment.Top; }).Col(1));
        var sp = U.Stack(head, U.T(title, "H3", 15).M(0, 12, 0, 3), U.T(sub, "Muted").Also(t => t.MinHeight = 34));
        var card = new Border { Classes = { "Card" }, Child = sp, Margin = new Thickness(6), Cursor = Cursors.Hand };
        HomePage.Hover(card);
        card.PointerReleased += (_, _) => { Shell.Main.PendingExpand = card; go(); };
        return card;
    }

    static Border Tile(string jp, string title, string sub, Action go)
    {
        Control icon = TileArt.TryGetValue(jp, out var art) ? Emoji.Img(art, 44) : U.Jp(jp, 24, U.B("Accent"), true);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        var sp = U.Stack(icon, U.T(title, "H3", 15).M(0, 10, 0, 3), U.T(sub, "Muted").Also(t => t.MinHeight = 34));
        var card = new Border { Classes = { "Card" }, Child = sp, Margin = new Thickness(6), Cursor = Cursors.Hand };
        HomePage.Hover(card);
        card.PointerReleased += (_, _) => { Shell.Main.PendingExpand = card; go(); };
        return card;
    }

    // ─────────── План на сегодня ───────────
    static string PlanKey(int i) => $"plan:{DateTime.Today:yyyy-MM-dd}:{i}";

    /// <summary>«Урок дня»: запускает первый невыполненный пункт плана. false — план уже выполнен.</summary>
    public static bool NextPlanStep()
    {
        var items = PlanItems();
        for (int i = 0; i < items.Count; i++)
            if (!ProgressStore.P.Flags.Contains(PlanKey(i)))
            {
                // нечего повторять — пункт засчитывается сам, идём дальше
                if (items[i].title == "Повторение" && !ProgressStore.P.Cards.Values.Any(c => c.Due <= DateTime.Now)) { ProgressStore.P.Flags.Add(PlanKey(i)); continue; }
                ProgressStore.P.Flags.Add(PlanKey(i)); ProgressStore.Notify();
                items[i].go();
                return true;
            }
        return false;
    }

    public static bool PlanDone => Enumerable.Range(0, PlanItems().Count).All(i => ProgressStore.P.Flags.Contains(PlanKey(i)));

    static List<(string title, string sub, Action go)> PlanItems()
    {
        var p = ProgressStore.P;
        int due = p.Cards.Count(kv => kv.Value.Due <= DateTime.Now);
        var unit = Course.Current;
        var items = new List<(string title, string sub, Action go)>
        {
            ("Повторение", due > 0 ? $"{Math.Min(due, 30)} карточек по расписанию" : "Всё повторено", () => Shell.Go("review")),
            ("Новые слова", "5 слов уровня " + p.Level, () =>
            {
                var fresh = Db.Words.Where(w => w.Level == p.Level && ProgressStore.Card(w.Id) == null).Take(5).ToList();
                if (fresh.Count > 0) Shell.Open(LearnSession.Words("Новые слова", fresh));
            }),
            ("Шаг пути", $"Юнит {unit.Id}: {unit.Title}", () => Shell.Open(new UnitPage(unit))),
            ("Говорение", "Одна фраза вслух с проверкой", () => Shell.Open(new SpeechLessonPage(Db.SpeechLessons[DateTime.Today.DayOfYear % Db.SpeechLessons.Length]))),
            (DateTime.Today.DayOfYear % 2 == 0 ? "Спряжение" : "Счётные слова", "Тренажёр дня", () => Shell.Open(DateTime.Today.DayOfYear % 2 == 0 ? Conj.Quiz() : Counters.Quiz())),
        };
        if (Mistakes.Count > 0) items.Insert(1, ("Ошибки", $"{Math.Min(Mistakes.Count, 15)} на исправление", () => { var q = MistakesQuiz(); if (q != null) Shell.Open(q); }));
        items.Add(("Игра", "Закрепи слова в игре", (Action)(() => Shell.Open((DateTime.Today.DayOfYear % 3) switch { 0 => new MatchRushGame(), 1 => (Control)new WordBuilderGame(), _ => new ListenCatchGame() }))));
        return items;
    }

    public static Border PlanCard()
    {
        var p = ProgressStore.P;
        var items = PlanItems();
        var sp = new StackPanel();
        int done = Enumerable.Range(0, items.Count).Count(i => p.Flags.Contains(PlanKey(i)));
        var title = U.Stack(U.T("ПЛАН НА СЕГОДНЯ", "Overline"), U.T(done == items.Count ? "План выполнен! お疲れ様！" : $"Выполнено {done} из {items.Count} · ~15 минут", "H3", 16).M(0, 4, 0, 0));
        var bar = new ProgressBar { Width = 120, Maximum = items.Count, Value = done, VerticalAlignment = VerticalAlignment.Center };
        if (Shell.Compact) { bar.Width = double.NaN; bar.MinWidth = 0; }
        var head = U.Cols("*", "auto");
        if (Shell.Compact) { sp.Children.Add(title); head.Margin = new Thickness(0, 12, 0, 4); head.Children.Add(bar); } // телефон: полоска под заголовком на всю ширину
        else head.Children.Add(title);
        head.Children.Add(U.Row(12, Shell.Compact ? new Border() : bar,
            done < items.Count ? U.Btn("Следующий шаг", () => NextPlanStep(), "Primary", "") : new Border()).Col(1));
        sp.Children.Add(head);
        for (int i = 0; i < items.Count; i++)
        {
            int idx = i;
            bool ok = p.Flags.Contains(PlanKey(i));
            var row = U.Cols("auto", "*", "auto");
            row.Margin = new Thickness(0, 10, 0, 0);
            row.Children.Add(new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(ok ? 0 : 1.5), BorderBrush = U.B("StrokeStrong"),
                Background = ok ? U.B("Good") : Brushes.Transparent,
                Child = ok ? new TextBlock { Text = "", FontFamily = U.R<FontFamily>("Icons"), FontSize = 11, Foreground = U.B("Bg"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null,
            }.CenterV());
            row.Children.Add(U.Stack(U.T(items[i].title, "H3", 14, U.B(ok ? "Muted" : "Text")), U.T(items[i].sub, "Muted", 12)).M(12, 0, 0, 0).Col(1));
            var go = items[i].go;
            row.Children.Add(U.Btn(ok ? "Ещё раз" : "Начать", () => { p.Flags.Add(PlanKey(idx)); ProgressStore.Notify(); go(); }, ok ? "Ghost" : "").CenterV().Col(2));
            sp.Children.Add(row);
        }
        return U.Card(sp, Shell.Compact ? new Thickness(16) : new Thickness(22));
    }

    // ─────────── Ошибки ───────────
    public static QuizSession? MistakesQuiz()
    {
        var keys = ProgressStore.P.Mistakes.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Take(15).ToList();
        var qs = new List<QuizQ>();
        var ruPool = Db.Words.Select(w => w.Ru).ToArray();
        foreach (var k in keys)
        {
            if (k.StartsWith("w:") && Db.Words.FirstOrDefault(w => w.Id == k) is { } w)
                qs.Add(new QuizQ { Prompt = w.Jp, PromptSize = w.Jp.Length > 5 ? 52 : 76, Sub = $"{w.Kana} · {w.Romaji}", Answer = w.Ru, Options = QuizSession.MakeOptions(w.Ru, ruPool), Speak = w.Kana, Key = k });
            else if (k.StartsWith("k:") && Db.Kanji.FirstOrDefault(x => x.Id == k) is { } kj)
                qs.Add(new QuizQ { Prompt = kj.Char, PromptSize = 120, Sub = "Что означает?", Answer = kj.Ru, Options = QuizSession.MakeOptions(kj.Ru, Db.Kanji.Select(x => x.Ru)), Key = k, Explain = $"он: {kj.On} · кун: {kj.Kun}" });
            else if ((k.StartsWith("h:") || k.StartsWith("k:")) && Kana.All.FirstOrDefault(c => c.Hira == k[2..] || c.Kata == k[2..]) is { } kc)
                qs.Add(new QuizQ { Prompt = k[2..], PromptSize = 110, Sub = "Как читается?", Answer = kc.Romaji, Options = QuizSession.MakeOptions(kc.Romaji, Kana.All.Select(c => c.Romaji)), Speak = k[2..], Key = k, OnResult = ok => ProgressStore.KanaAnswer(k, ok) });
            else if (k.StartsWith("g:"))
            {
                var parts = k.Split(':');
                if (parts.Length == 3 && int.TryParse(parts[1], out var lid) && int.TryParse(parts[2], out var qi) && lid >= 1 && lid <= Db.Grammar.Length && qi < Db.Grammar[lid - 1].Quiz.Length)
                {
                    var q = Db.Grammar[lid - 1].Quiz[qi];
                    qs.Add(new QuizQ { Prompt = q.Question, PromptSize = 32, Sub = q.Ru, Answer = q.Answer, Options = q.Options.Shuffled().ToArray(), OptionsJp = true, Key = k, Speak = q.Question.Replace("＿", q.Answer) });
                }
            }
        }
        return qs.Count == 0 ? null : new QuizSession("Работа над ошибками", qs);
    }

    // ─────────── Фразы на слух ───────────
    /// <summary>
    /// Аудирование без текста: фраза звучит в обычном темпе (из уроков говорения, диалогов и примеров к словам),
    /// нужно выбрать перевод. Текст фразы показывается только после ответа.
    /// </summary>
    public static QuizSession PhraseListening(int n = 10)
    {
        var lvl = ProgressStore.P.Level;
        var pool = Db.SpeechLessons.SelectMany(l => l.Items.Select(i => (i.Jp, i.Ru)))
            .Concat(Db.Dialogs.Where(d => string.CompareOrdinal(d.Level, lvl) >= 0).SelectMany(d => d.Lines.Select(l => (l.Jp, l.Ru))))
            .Concat(Db.Words.Where(w => w.Level == lvl && w.ExJp.Length > 0).Select(w => (Jp: w.ExJp, Ru: w.ExRu)))
            .Where(x => x.Jp.Length is >= 4 and <= 28 && x.Ru.Length is > 0 and <= 70)
            .DistinctBy(x => x.Ru).ToList();
        var ruPool = pool.Select(x => x.Ru).ToArray();
        var qs = pool.Shuffled().Take(n).Select(x => new QuizQ
        {
            Prompt = "聞いて", PromptSize = 52, Sub = "Что сказали? Текст покажу после ответа · 🔊 повторить", SpeakOnShow = true,
            Answer = x.Ru, Options = QuizSession.MakeOptions(x.Ru, ruPool), Speak = x.Jp,
            Explain = $"{x.Jp}\n{Reading.RomajiWords(x.Jp)}",
        });
        return new QuizSession("Фразы на слух", qs);
    }

    // ─────────── Диктант ───────────
    public static QuizSession Dictation()
    {
        var pool = Db.Words.Where(w => w.Level is "N5" or "N4" or "1000" && w.Kana.Length is >= 2 and <= 6).ToList();
        var qs = pool.Shuffled().Take(12).Select(w => new QuizQ
        {
            Prompt = "聞いて書いて", PromptSize = 44, Sub = "Послушай и напиши слово латиницей или каной · 🔊 повторить", Typing = true, SpeakOnShow = true,
            Answer = string.Join("|", new[] { w.Romaji.Replace("'", ""), w.Romaji, Kana.ToHiragana(w.Kana), w.Kana }.Distinct()), Speak = w.Kana, Key = w.Id,
            Explain = $"{w.Jp} — {w.Kana} ({w.Romaji}) — {w.Ru}",
        });
        return new QuizSession("Диктант", qs);
    }

    // ─────────── Пробный тест ───────────
    public static QuizSession MockTest(string level)
    {
        if (level is not ("N5" or "N4" or "N3" or "N2")) level = "N5";
        var words = Db.WordsOf(level).ToList();
        var ruPool = words.Select(w => w.Ru).ToArray();
        var qs = new List<QuizQ>();
        // разбор по разделам, как в настоящем JLPT
        var score = new Dictionary<string, (int ok, int all)>();
        Action<bool> Tally(string sec) => ok => { var (o, a) = score.GetValueOrDefault(sec); score[sec] = (o + (ok ? 1 : 0), a + 1); };
        qs.AddRange(words.Shuffled().Take(10).Select(w => new QuizQ { OnResult = Tally("Лексика"), Prompt = w.Jp, PromptSize = 56, Sub = "Лексика: выбери значение", Answer = w.Ru, Options = QuizSession.MakeOptions(w.Ru, ruPool), Speak = w.Kana, Key = w.Id }));
        var exs = Db.KanjiOf(level).SelectMany(k => k.Examples).ToList();
        var kanaPool = exs.Select(e => e.Kana).ToArray();
        qs.AddRange(exs.Shuffled().Take(6).Select(e => new QuizQ { OnResult = Tally("Кандзи"), Prompt = e.Jp, PromptSize = 60, Sub = "Кандзи: выбери чтение", Answer = e.Kana, OptionsJp = true, Options = QuizSession.MakeOptions(e.Kana, kanaPool), Speak = e.Kana }));
        var gq = Db.Grammar.Where(g => g.Level == level).SelectMany(g => g.Quiz.Select((q, i) => (g, q, i))).Shuffled().Take(8);
        qs.AddRange(gq.Select(x => new QuizQ { OnResult = Tally("Грамматика"), Prompt = x.q.Question, PromptSize = 30, Sub = "Грамматика: " + x.q.Ru, Answer = x.q.Answer, OptionsJp = true, Options = x.q.Options.Shuffled().ToArray(), Key = $"g:{x.g.Id}:{x.i}", Speak = x.q.Question.Replace("＿", x.q.Answer) }));
        if (!ProgressStore.P.QuietMode)
            qs.AddRange(words.Shuffled().Take(6).Select(w => new QuizQ { OnResult = Tally("Аудирование"), Prompt = "聞いて", PromptSize = 52, Sub = "Аудирование: что прозвучало? · 🔊 повторить", SpeakOnShow = true, Answer = w.Ru, Options = QuizSession.MakeOptions(w.Ru, ruPool), Speak = w.Kana, Key = w.Id }));
        // как на экзамене: ~40 секунд на вопрос, ошибки в конце не повторяются, итог — по разделам
        return new QuizSession($"Пробный тест {level}", qs, (ok, total) =>
            Shell.Toast(total > 0 && ok * 100 / total >= 60 ? $"Тест сдан: {ok * 100 / total}%! 合格" : "Пока не сдан — порог 60%. Повтори ошибки!", "試"))
        {
            ExamMode = true, TimeLimitSeconds = qs.Count * 40,
            OnRestart = score.Clear,
            ExtraStats = () => score.Select(kv => ($"{kv.Key}", $"{kv.Value.ok} / {kv.Value.all}  ·  {(kv.Value.all == 0 ? 0 : kv.Value.ok * 100 / kv.Value.all)}%")),
        };
    }
}

public sealed class CountersPage : UserControl, IPage
{
    public string Title => "Счётные суффиксы";
    public CountersPage()
    {
        var root = new StackPanel { MaxWidth = 980 };
        root.Children.Add(U.Header("数", "Счётные суффиксы", "В японском предметы считают особыми словами. Обрати внимание на звуковые изменения: いっぽん, さんぼん, ろっぽん…"));
        var start = U.Btn("Тренировка: 15 вопросов", () => Shell.Open(Counters.Quiz()), "Primary", "");
        start.HorizontalAlignment = HorizontalAlignment.Left; start.Margin = new Thickness(0, 0, 0, 16);
        root.Children.Add(start);
        root.Children.Add(Counters.Table());
        Content = U.Page(root);
    }
}
