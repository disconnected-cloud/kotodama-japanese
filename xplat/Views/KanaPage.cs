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

public sealed class KanaPage : UserControl, IPage
{
    public string Title => "Кана";
    bool _kata;
    readonly HashSet<string> _groups = new() { Kana.Basic };

    public void OnShown() => Content = U.Page(Build());

    static string Key(KanaCell c, bool kata) => (kata ? "k:" : "h:") + c.Get(kata);

    Control Build()
    {
        var root = new StackPanel();

        // Переключатель азбуки
        var sw = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (text, kata) in new[] { ("ひらがな  Хирагана", false), ("カタカナ  Катакана", true) })
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = text, GroupName = "script", IsChecked = _kata == kata };
            rb.Checked += (_, _) => { _kata = kata; OnShown(); };
            sw.Children.Add(rb);
        }
        root.Children.Add(U.Header(_kata ? "ア" : "あ", "Кана",
            _kata ? "Катакана — для заимствованных слов, имён и акцентов" : "Хирагана — основа японского письма. Начни с неё!", sw));

        // Панель тренировки
        int total = Kana.All.Count, known = Kana.All.Count(c => ProgressStore.KanaScore(Key(c, _kata)) >= 3);
        var train = U.Cols("*", "auto");
        var leftT = new StackPanel();
        leftT.Children.Add(U.T("Тренировка", "H2"));
        leftT.Children.Add(U.T($"Освоено {known} из {total} знаков. Знак считается освоенным после 3 верных ответов подряд.", "Muted").M(0, 4, 0, 14));
        var chips = new WrapPanel();
        foreach (var (g, name) in new[] { (Kana.Basic, "Основные (46)"), (Kana.Dakuten, "Дакутэн ゛゜ (25)"), (Kana.Combo, "Сочетания きゃ (33)") })
        {
            var tb = new ToggleButton { Classes = { "Chip" }, Content = name, IsChecked = _groups.Contains(g) };
            tb.Checked += (_, _) => _groups.Add(g);
            tb.Unchecked += (_, _) => { _groups.Remove(g); if (_groups.Count == 0) tb.IsChecked = true; };
            chips.Children.Add(tb);
        }
        leftT.Children.Add(chips);
        train.Children.Add(leftT);
        var btns = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinWidth = 230 };
        btns.Children.Add(U.Btn("Квиз: выбор ответа", () => StartQuiz(false), "Primary", "").M(0, 0, 0, 8));
        btns.Children.Add(U.Btn("Квиз: ввод ромадзи", () => StartQuiz(true), "", "").M(0, 0, 0, 8));
        btns.Children.Add(U.Btn("Только слабые знаки", () => StartQuiz(false, weakOnly: true), "", ""));
        train.Children.Add(btns.Col(1));
        var trainCard = U.Card(train);
        root.Children.Add(trainCard);

        // Таблицы
        // Основная таблица + панель учителя справа
        var basic = U.Cols("auto", "24", "*");
        basic.Children.Add(Table("Основные знаки · 五十音", Kana.BasicGrid, 5));
        basic.Children.Add(TeacherPanel().Col(2));
        root.Children.Add(U.Stackify(basic));
        root.Children.Add(Table("Звонкие и полузвонкие · 濁音・半濁音", Kana.DakutenGrid, 5));
        root.Children.Add(Table("Сочетания · 拗音", Kana.ComboGrid, 3));
        root.Children.Add(U.T("Подсказка: нажми на знак, чтобы услышать произношение.", "Muted").Center().M(0, 18, 0, 0));
        Anim.Stagger(root, 60, 0, 6);
        return root;
    }

    /// <summary>Панель «учителя»: советы и знакомство со знаками по 10 (порядок черт, звук, примеры).</summary>
    Control TeacherPanel()
    {
        bool kata = _kata;
        var basic = Kana.All.Where(c => c.Group == Kana.Basic).ToList();
        var fresh = basic.Where(c => ProgressStore.KanaScore(Key(c, kata)) < 3).Take(10).ToList();
        var sp = new StackPanel();
        var head = U.Cols("*", "auto");
        head.Children.Add(U.Stack(U.T("Как учить кану", "H2"),
            U.T("Учи по 10 знаков: сначала посмотри порядок черт и послушай, потом квиз. Пиши каждый знак рукой 3–5 раз — так он запомнится в разы быстрее.", "Body", 13.5).Also(t => t.TextWrapping = TextWrapping.Wrap).M(0, 8, 0, 0),
            U.T(kata ? "Катакана — те же звуки, что и в хирагане, только угловатые знаки. Учи её после хираганы." : "Похожие пары: さ/ち, は/ほ, ぬ/め, る/ろ, わ/れ/ね — сравни их внимательно.", "Muted", 12.5).Also(t => t.TextWrapping = TextWrapping.Wrap).M(0, 10, 0, 0)));
        head.Children.Add(Art.Anime("teacher", 230).Col(1).M(12, 0, 0, 0));
        sp.Children.Add(head);
        var learn = U.Btn(fresh.Count > 0 ? $"Учить следующие {fresh.Count}: {string.Concat(fresh.Take(5).Select(c => c.Get(kata)))}…" : "Повторить все знаки", () =>
        {
            var list = fresh.Count > 0 ? fresh : basic.Shuffled().Take(10).ToList();
            Shell.Open(new LearnSession(kata ? "Катакана" : "Хирагана", list.Select(c => LearnItem.From(c, kata)), OnShown,
                () => QuizFor(list, kata, kata ? "Катакана" : "Хирагана", null)));
        }, "Primary", "");
        learn.HorizontalAlignment = HorizontalAlignment.Left; learn.Margin = new Thickness(0, 14, 0, 8);
        sp.Children.Add(learn);
        var write = U.Btn("Прописи: пиши знаки мышью", () => Shell.Open(new WritingPage()), "", "");
        write.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(write);
        return U.Card(sp).M(0, 62, 0, 0).Also(c => c.VerticalAlignment = VerticalAlignment.Top);
    }

    Control Table(string title, IReadOnlyList<KanaCell?[]> rows, int cols)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 28, 0, 0) };
        sp.Children.Add(U.T(title, "H2").M(0, 0, 0, 12));
        var grid = new UniformGrid { Columns = cols, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var row in rows)
            foreach (var c in row)
                grid.Children.Add(c == null ? new Border { Width = 92, Height = 96, Margin = new Thickness(5) } : Cell(c));
        sp.Children.Add(U.Card(grid, new Thickness(14)));
        sp.Children[1].SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);
        return sp;
    }

    Border Cell(KanaCell c)
    {
        var ch = c.Get(_kata);
        int score = ProgressStore.KanaScore(Key(c, _kata));
        var g = new Grid();
        g.Children.Add(U.Stack(
            U.Jp(ch, ch.Length > 1 ? 28 : 34).Center(),
            U.T(c.Romaji, "Muted", 12).Center().M(0, 2, 0, 0)).CenterV());
        // Полоска освоения
        var bar = new Border
        {
            Height = 4, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left,
            Width = Math.Max(0, 64 * score / 5.0), Margin = new Thickness(14, 0, 0, 8),
            Background = score >= 3 ? U.B("GreenGradient") : U.B("AccentGradient"),
        };
        g.Children.Add(bar);
        var b = new Border
        {
            Width = 92, Height = 96, Margin = new Thickness(5), CornerRadius = new CornerRadius(16),
            Background = score >= 3 ? U.B("GoodSoft") : U.B("Surface2"), BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(1),
            Child = g, Cursor = Cursors.Hand, 
        }.Tip($"{ch} — {c.Romaji} · освоение {score}/5");
        b.PointerEntered += (_, _) => b.BorderBrush = U.B("Accent");
        b.PointerExited += (_, _) => b.BorderBrush = U.B("Stroke");
        b.PointerReleased += (_, _) => { Speech.Say(ch); Anim.Pop(b, 0.85); };
        return b;
    }

    static readonly Dictionary<string, string> Alt = new()
    {
        ["shi"] = "si", ["chi"] = "ti", ["tsu"] = "tu", ["fu"] = "hu", ["ji"] = "zi|di", ["zu"] = "du",
        ["wo"] = "o", ["n"] = "nn", ["sha"] = "sya", ["shu"] = "syu", ["sho"] = "syo",
        ["cha"] = "tya|cya", ["chu"] = "tyu|cyu", ["cho"] = "tyo|cyo", ["ja"] = "zya|jya", ["ju"] = "zyu|jyu", ["jo"] = "zyo|jyo",
    };

    void StartQuiz(bool typing, bool weakOnly = false)
    {
        bool kata = _kata;
        var pool = Kana.All.Where(c => _groups.Contains(c.Group)).ToList();
        IEnumerable<KanaCell> pick = pool.OrderBy(c => ProgressStore.KanaScore(Key(c, kata)) + U.Rng.NextDouble() * 2.5);
        if (weakOnly) pick = pick.Where(c => ProgressStore.KanaScore(Key(c, kata)) < 3);
        var chosen = pick.Take(20).ToList();
        if (chosen.Count < 4) { Shell.Toast("Слабых знаков нет — все освоены!", "すごい"); return; }
        Shell.Open(Build(chosen, pool, kata, typing, kata ? "Катакана" : "Хирагана", null));
    }

    /// <summary>Квиз по заданному набору знаков (для «Пути обучения»): 15–20 вопросов с повторами.</summary>
    public static QuizSession QuizFor(IList<KanaCell> cells, bool kata, string title, Action<int, int>? finished)
    {
        int n = Math.Clamp(cells.Count * 2, 15, 20);
        var chosen = Enumerable.Range(0, n / Math.Max(1, cells.Count) + 1).SelectMany(_ => cells.Shuffled()).Take(n).ToList();
        var pool = cells.Count >= 6 ? cells.ToList() : cells.Concat(Kana.All.Where(c => c.Group == Kana.Basic)).ToList();
        return Build(chosen, pool, kata, false, title, finished);
    }

    static QuizSession Build(List<KanaCell> chosen, List<KanaCell> pool, bool kata, bool typing, string title, Action<int, int>? finished)
    {
        var romajiPool = pool.Select(c => c.Romaji).ToArray();
        var kanaPool = pool.Select(c => c.Get(kata)).ToArray();
        var qs = chosen.Select(c =>
        {
            string ch = c.Get(kata), key = Key(c, kata);
            if (typing)
                return new QuizQ
                {
                    Prompt = ch, Sub = "Введи чтение латиницей", Typing = true, PromptSize = 110,
                    Answer = c.Romaji + (Alt.TryGetValue(c.Romaji, out var a) ? "|" + a : ""),
                    Speak = ch, OnResult = ok => ProgressStore.KanaAnswer(key, ok), Key = key,
                };
            bool reverse = U.Rng.Next(3) == 0;
            return reverse
                ? new QuizQ { Prompt = c.Romaji, Sub = "Найди нужный знак", PromptJp = false, PromptSize = 56, Answer = ch, OptionsJp = true, Options = QuizSession.MakeOptions(ch, kanaPool), Speak = ch, OnResult = ok => ProgressStore.KanaAnswer(key, ok), Key = key }
                : new QuizQ { Prompt = ch, Sub = "Как читается этот знак?", PromptSize = 110, Answer = c.Romaji, Options = QuizSession.MakeOptions(c.Romaji, romajiPool), Speak = ch, OnResult = ok => ProgressStore.KanaAnswer(key, ok), Key = key };
        });
        return new QuizSession(title, qs, finished);
    }
}
