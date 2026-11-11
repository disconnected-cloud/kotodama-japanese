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

/// <summary>Логика пути обучения: состояние шагов, блокировки юнитов, запуск активностей.</summary>
public static class Course
{
    static HashSet<string> F => ProgressStore.P.Flags;
    public static string TheoryKey(CourseUnit u) => "theory:" + u.Id;
    static string KanaKey(CourseUnit u, int i) => $"kana:{u.Id}:{i}";

    public static bool StepDone(CourseUnit u, int i)
    {
        var s = u.Steps[i];
        return s.T switch
        {
            "kana" => F.Contains(KanaKey(u, i)),
            "speech" => F.Contains("speech:" + s.Id),
            "dialog" => F.Contains("dialog:" + s.Id),
            "grammar" => ProgressStore.P.GrammarDone.Contains(s.Id),
            "words" => WordsStarted(s.Topic!) >= Math.Min(10, Db.Words.Count(w => w.Topic == s.Topic)),
            "kanji" => Db.KanjiOf(s.Level ?? "N5").Count(k => ProgressStore.Card(k.Id) != null) >= s.Count,
            _ => false,
        };
    }

    static int WordsStarted(string topic) => Db.Words.Count(w => w.Topic == topic && ProgressStore.Card(w.Id) != null);

    public static int DoneCount(CourseUnit u) => (F.Contains(TheoryKey(u)) ? 1 : 0) + Enumerable.Range(0, u.Steps.Length).Count(i => StepDone(u, i));
    public static int Total(CourseUnit u) => u.Steps.Length + 1;
    public static bool Complete(CourseUnit u) => F.Contains("skip:" + u.Id) || DoneCount(u) == Total(u);
    public static bool Unlocked(CourseUnit u) => u.Id == 1 || Complete(Db.Course[u.Id - 2]);
    public static CourseUnit Current => Db.Course.FirstOrDefault(u => !Complete(u) && Unlocked(u)) ?? Db.Course[^1];

    public static string StepTitle(CourseStep s) => s.Title ?? s.T switch
    {
        "speech" => "Говорение: " + Db.SpeechLessons[s.Id - 1].Title,
        "dialog" => "Диалог: " + Db.Dialogs[s.Id - 1].Title,
        "grammar" => "Грамматика: " + Db.Grammar[s.Id - 1].Title,
        "words" => "Слова: " + s.Topic,
        "kanji" => $"Кандзи {s.Level}: выучить {s.Count}",
        _ => s.T,
    };

    public static string StepIcon(CourseStep s) => s.T switch
    {
        "kana" => "あ", "speech" => "話", "dialog" => "対", "grammar" => "文", "words" => "語", "kanji" => "漢", _ => "・",
    };

    /// <summary>После порции: если шаг ещё не закрыт — подсказываем, сколько осталось.</summary>
    static Action AfterStep(CourseUnit u, int i) => () =>
    {
        if (StepDone(u, i)) Shell.Toast("Шаг пройден!", "合格");
        else
        {
            var s = u.Steps[i];
            int left = s.T == "kanji"
                ? s.Count - Db.KanjiOf(s.Level ?? "N5").Count(k => ProgressStore.Card(k.Id) != null)
                : Math.Min(10, Db.Words.Count(w => w.Topic == s.Topic)) - WordsStarted(s.Topic!);
            Shell.Toast($"Порция выучена! До конца шага осталось {left} — нажми «Продолжить»", "良");
        }
    };

    public static void Launch(CourseUnit u, int i)
    {
        var s = u.Steps[i];
        switch (s.T)
        {
            case "kana":
            {
                bool kata = s.Script == "k";
                IEnumerable<KanaCell> cells = s.Rows != null
                    ? s.Rows.SelectMany(r => Kana.BasicGrid[r]).OfType<KanaCell>()
                    : Kana.All.Where(c => c.Group == (s.Group ?? Kana.Basic));
                var key = KanaKey(u, i);
                var list = cells.ToList();
                Control Quiz() => KanaPage.QuizFor(list, kata, StepTitle(s), (ok, total) =>
                {
                    if (total > 0 && ok * 100 / total >= 80) { F.Add(key); ProgressStore.Notify(); Shell.Toast("Шаг пройден!", "合格"); }
                    else Shell.Toast("Нужно 80% верных ответов — попробуй ещё раз", "頑張って");
                });
                // Впервые — сначала знакомство со всеми знаками (порядок черт, звук), потом квиз
                if (F.Contains(key)) Shell.Open(Quiz());
                else Shell.Open(new LearnSession(StepTitle(s), list.Select(c => LearnItem.From(c, kata)), quiz: Quiz));
                break;
            }
            case "speech": Shell.Open(new SpeechLessonPage(Db.SpeechLessons[s.Id - 1])); break;
            case "dialog": Shell.Open(new DialogPage(Db.Dialogs[s.Id - 1])); break;
            case "grammar": Shell.Open(new GrammarLessonPage(Db.Grammar[s.Id - 1])); break;
            case "words":
            {
                // Порциями по 6: сначала знакомство со всеми, потом проверка только по ним
                var fresh = Db.Words.Where(w => w.Topic == s.Topic && ProgressStore.Card(w.Id) == null).Take(6).ToList();
                if (fresh.Count > 0) Shell.Open(LearnSession.Words("Слова: " + s.Topic, fresh, AfterStep(u, i)));
                else Shell.Open(WordsPage.QuizFor(Db.Words.Where(w => w.Topic == s.Topic).ToList(), 0));
                break;
            }
            case "kanji":
            {
                var fresh = Db.KanjiOf(s.Level ?? "N5").Where(k => ProgressStore.Card(k.Id) == null).Take(5).ToList();
                if (fresh.Count > 0) Shell.Open(LearnSession.Kanji(fresh, AfterStep(u, i)));
                else Shell.Go("kanji");
                break;
            }
        }
    }
}

public sealed class PathPage : UserControl, IPage
{
    public string Title => "Путь обучения";

    public void OnShown()
    {
        var root = new StackPanel { MaxWidth = 860 };
        var units = Db.Course;
        int done = units.Count(Course.Complete);
        root.Children.Add(U.Header("道", "Путь обучения", $"Пройдено {done} из {units.Length} юнитов. В каждом — сначала теория, потом практика."));

        var current = Course.Current;
        string? lastLevel = null;
        for (int i = 0; i < units.Length; i++)
        {
            var u = units[i];
            if (u.Level != lastLevel)
            {
                root.Children.Add(U.T(u.Level == "N5" ? "N5 · ОСНОВЫ" : "N4 · ПРОДОЛЖЕНИЕ", "Overline").M(54, i == 0 ? 0 : 18, 0, 10));
                lastLevel = u.Level;
            }
            root.Children.Add(UnitRow(u, u == current, i == units.Length - 1));
        }
        Anim.Stagger(root, 30, 0, 30);
        Content = U.Page(new Grid { Children = { root } }.Also(g => root.HorizontalAlignment = HorizontalAlignment.Center));
    }

    static Control UnitRow(CourseUnit u, bool current, bool last)
    {
        bool complete = Course.Complete(u), unlocked = Course.Unlocked(u);
        int d = Course.DoneCount(u), t = Course.Total(u);
        var g = U.Cols("40", "*");

        // Узел ленты + соединительная линия
        var node = new Grid { Width = 40 };
        if (!last) node.Children.Add(new Rectangle { Width = 2, Fill = U.B(complete ? "Good" : "Stroke"), Margin = new Thickness(0, 34, 0, -12), HorizontalAlignment = HorizontalAlignment.Center });
        node.Children.Add(new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(16), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 18, 0, 0),
            Background = U.B(complete ? "Good" : current ? "Accent" : "Surface2"),
            BorderBrush = U.B(unlocked ? "StrokeStrong" : "Stroke"), BorderThickness = new Thickness(complete || current ? 0 : 1),
            Child = new TextBlock
            {
                Text = complete ? "桜" : unlocked ? u.Id.ToString() : "",
                FontFamily = U.R<FontFamily>(complete ? "JpSerif" : !unlocked ? "Icons" : "UiFont"),
                FontSize = complete ? 15 : !unlocked ? 12 : 13, FontWeight = FontWeights.SemiBold,
                Foreground = complete || current ? new SolidColorBrush(Color.FromRgb(0x14, 0x10, 0x12)) : U.B("Muted"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        });
        g.Children.Add(node);
        if (complete && node.Children[^1] is Border bloom) bloom.Loaded += (_, _) => Anim.Pop(bloom, 0.4);

        var info = U.Cols("*", "auto");
        info.Children.Add(U.Stack(
            U.T(u.Title, "H3", 15.5, U.B(unlocked ? "Text" : "Faint")),
            U.T(complete ? "Пройдено" : unlocked ? $"{d} из {t} шагов" : "Откроется после предыдущего юнита", "Muted", 12.5).M(0, 3, 0, 0)).CenterV());
        if (current && unlocked) info.Children.Add(U.Btn(d == 0 ? "Начать" : "Продолжить", () => Shell.Open(new UnitPage(u)), "Primary").CenterV().Col(1));
        else if (unlocked) info.Children.Add(new ProgressBar { Width = 90, Maximum = t, Value = d, Height = 4, VerticalAlignment = VerticalAlignment.Center, Foreground = U.B(complete ? "Good" : "Accent") }.Col(1));

        var card = new Border
        {
            Classes = { "Card" }, Padding = new Thickness(18, 14, 14, 14), Margin = new Thickness(12, 8, 0, 8), Child = info,
            Cursor = unlocked ? Cursors.Hand : Cursors.Arrow, Opacity = unlocked ? 1 : 0.6,
        };
        if (current) card.BorderBrush = U.B("AccentSoft");
        if (unlocked)
        {
            HomePage.Hover(card);
            card.PointerReleased += (_, _) => Shell.OpenFrom(card, new UnitPage(u));
        }
        g.Children.Add(card.Col(1));
        return g;
    }
}

/// <summary>Юнит: теория → шаги практики (каждый следующий открывается после предыдущего).</summary>
public sealed class UnitPage : UserControl, IPage
{
    readonly CourseUnit _u;
    public string Title => $"Юнит {_u.Id}";
    public UnitPage(CourseUnit u) => _u = u;

    public void OnShown()
    {
        var u = _u;
        var root = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Center };
        root.Children.Add(U.T($"ЮНИТ {u.Id} · {u.Level}", "Overline"));
        root.Children.Add(U.T(u.Title, "H1").M(0, 4, 0, 18));

        // ─────────── Теория ───────────
        var th = new StackPanel();
        th.Children.Add(U.Row(10, U.Jp("理", 18, U.B("Accent"), true).CenterV(), U.T("Теория", "H2").CenterV()).M(0, 0, 0, 10));
        foreach (var para in u.Theory) th.Children.Add(U.T(para, "Body", 15).Also(t => t.LineHeight = 25).M(0, 0, 0, 12));
        if (u.Examples.Length > 0)
        {
            th.Children.Add(U.T("ПРИМЕРЫ", "Overline").M(0, 6, 0, 8));
            foreach (var ex in u.Examples) th.Children.Add(ExampleRow(ex));
        }
        bool theoryDone = ProgressStore.P.Flags.Contains(Course.TheoryKey(u));
        if (!theoryDone)
        {
            var ok = U.Btn("Понятно — к практике", () =>
            {
                ProgressStore.P.Flags.Add(Course.TheoryKey(u));
                ProgressStore.AddXp(5);
                OnShown();
            }, "Primary", "");
            ok.HorizontalAlignment = HorizontalAlignment.Left; ok.Margin = new Thickness(0, 10, 0, 0);
            th.Children.Add(ok);
        }
        root.Children.Add(U.Card(th, new Thickness(24, 20, 24, 20)));

        // ─────────── Практика ───────────
        root.Children.Add(U.Row(10, U.Jp("練", 18, U.B("Accent"), true).CenterV(), U.T("Практика", "H2").CenterV()).M(0, 24, 0, 10));
        bool prevDone = theoryDone;
        for (int i = 0; i < u.Steps.Length; i++)
        {
            bool done = Course.StepDone(u, i);
            bool open = prevDone || done;
            root.Children.Add(StepRow(u, i, done, open, isNext: open && !done && prevDone));
            prevDone = done && prevDone;
        }

        if (Course.Complete(u) && u.Id < Db.Course.Length)
        {
            var next = Db.Course[u.Id];
            var nb = U.Btn($"Следующий юнит: {next.Title}", () => { Shell.Back(); Shell.Open(new UnitPage(next)); }, "Primary", "");
            nb.HorizontalAlignment = HorizontalAlignment.Left; nb.Margin = new Thickness(0, 16, 0, 0);
            root.Children.Add(nb);
        }
        Anim.Stagger(root, 40, 0, 14);
        Content = U.Page(new Grid { Children = { root } });
    }

    static Control ExampleRow(Example ex)
    {
        var g = U.Cols("*", "auto");
        g.Children.Add(U.Stack(U.Jp(ex.Jp, 19), U.T(ex.Romaji, "Muted", 13).M(0, 1, 0, 0), U.T(ex.Ru, "Body", 13.5).M(0, 2, 0, 0)));
        g.Children.Add(U.Row(2, U.SpeakBtn(() => ex.Jp, true), U.SpeakBtn(() => ex.Jp)).CenterV().Col(1));
        return new Border { Background = U.B("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 8, 10), Margin = new Thickness(0, 0, 0, 8), Child = g };
    }

    static Control StepRow(CourseUnit u, int i, bool done, bool open, bool isNext)
    {
        var s = u.Steps[i];
        var g = U.Cols("auto", "*", "auto");
        g.Children.Add(new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Background = U.B(done ? "GoodSoft" : isNext ? "AccentSoft" : "Surface2"),
            Child = done
                ? new TextBlock { Text = "", FontFamily = U.R<FontFamily>("Icons"), Foreground = U.B("Good"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                : U.Jp(Course.StepIcon(s), 16, U.B(isNext ? "Accent" : "Muted"), true).Center().CenterV(),
        });
        g.Children.Add(U.Stack(U.T(Course.StepTitle(s), "H3", 14.5, U.B(open ? "Text" : "Faint")),
            U.T(done ? "Готово" : !open ? "Сначала пройди предыдущий шаг" : Hint(s), "Muted", 12.5)).CenterV().M(14, 0, 10, 0).Col(1));
        if (open) g.Children.Add(U.Btn(done ? "Повторить" : "Начать", () => Course.Launch(u, i), isNext ? "Primary" : "").CenterV().Col(2));
        var card = new Border { Classes = { "Card" }, Padding = new Thickness(14, 12, 12, 12), Margin = new Thickness(0, 0, 0, 8), Child = g, Opacity = open ? 1 : 0.55 };
        if (isNext) card.BorderBrush = U.B("AccentSoft");
        return card;
    }

    static string Hint(CourseStep s) => s.T switch
    {
        "kana" => "Знакомство со знаками → квиз · нужно 80%",
        "speech" => "Повтори все фразы урока вслух",
        "dialog" => "Сыграй свою роль в сценке",
        "grammar" => "Объяснение + упражнения · нужно 70%",
        "words" => "Знакомство со словами → проверка по ним",
        "kanji" => "Сначала все иероглифы с чертами и примерами, потом проверка",
        _ => "",
    };
}
