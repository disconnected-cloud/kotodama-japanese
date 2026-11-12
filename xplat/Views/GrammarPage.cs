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

public sealed class GrammarPage : UserControl, IPage
{
    public string Title => "Грамматика";

    public void OnShown()
    {
        var root = new StackPanel();
        var done = ProgressStore.P.GrammarDone;
        root.Children.Add(U.Header("文", "Грамматика", $"Пройдено {done.Count} из {Db.Grammar.Length} уроков. Каждый урок — объяснение, примеры с озвучкой и упражнения."));

        bool unlockedNext = true;
        foreach (var level in new[] { "N5", "N4", "N3" })
        {
            root.Children.Add(U.T(level switch { "N5" => "N5 · основы", "N4" => "N4 · продолжение", _ => "N3 · средний уровень" }, "H2").M(0, 6, 0, 10));
            var grid = new UniformGrid { Columns = U.Columns(2), Margin = new Thickness(-6, 0, -6, 16) };
            foreach (var l in Db.Grammar.Where(g => g.Level == level).OrderBy(Num))
            {
                bool isDone = done.Contains(l.Id);
                bool recommended = !isDone && unlockedNext;
                if (recommended) unlockedNext = false;
                grid.Children.Add(LessonCard(l, isDone, recommended));
            }
            root.Children.Add(grid);
            Anim.Stagger(grid, 30, 60);
        }
        Content = U.Page(root);
    }

    /// <summary>Номер урока — в порядке пути обучения (уроки, добавленные в 2.0, встают на свои места).</summary>
    public static int Num(GrammarLesson l) => _num.Value.TryGetValue(l.Id, out var n) ? n : l.Id;
    static readonly Lazy<Dictionary<int, int>> _num = new(() =>
    {
        var order = Db.Course.SelectMany(u => u.Steps).Where(s => s.T == "grammar").Select(s => s.Id).Distinct()
            .Concat(Db.Grammar.Select(g => g.Id)).Distinct().ToList();
        return order.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i + 1);
    });

    static Border LessonCard(GrammarLesson l, bool done, bool next)
    {
        var g = U.Cols("auto", "*", "auto");
        g.Children.Add(new Border
        {
            Width = 54, Height = 54, CornerRadius = new CornerRadius(16),
            Background = done ? U.B("GreenGradient") : next ? U.B("AccentGradient") : U.B("Surface2"),
            Child = new TextBlock
            {
                Text = done ? "" : Num(l).ToString(), FontFamily = U.R<FontFamily>(done ? "Icons" : "UiFont"),
                FontSize = done ? 20 : 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        });
        var info = U.Stack(
            // с переносом и на компьютере: в две колонки длинный заголовок уходил под значок уровня
            new WrapPanel { Children = { U.T(l.Title, "H3", 16).Also(t => t.TextWrapping = TextWrapping.Wrap).M(0, 0, 8, 0), next ? U.Pill("СЛЕДУЮЩИЙ", U.B("AccentSoft"), U.B("Accent")) : new Border() } },
            U.T(l.Points.Length > 0 ? string.Join("  ·  ", l.Points.Select(p => p.Pattern)) : "", "Muted", 13).M(0, 4, 0, 0));
        ((TextBlock)info.Children[1]).FontFamily = U.R<FontFamily>("JpFont");
        ((TextBlock)info.Children[1]).TextTrimming = TextTrimming.CharacterEllipsis;
        // на компьютере — одна строка, на телефоне — до трёх (иначе от схемы урока видно два слова)
        ((TextBlock)info.Children[1]).TextWrapping = Shell.Compact ? TextWrapping.Wrap : TextWrapping.NoWrap;
        if (Shell.Compact) ((TextBlock)info.Children[1]).MaxLines = 3;
        g.Children.Add(info.CenterV().M(16, 0, 10, 0).Col(1));
        g.Children.Add(U.Pill(l.Level).CenterV().Col(2));
        var card = new Border { Classes = { "Card" }, Child = g, Margin = new Thickness(8), Padding = new Thickness(18), Cursor = Cursors.Hand };
        HomePage.Hover(card);
        card.PointerReleased += (_, _) => Shell.OpenFrom(card, new GrammarLessonPage(l));
        return card;
    }
}

public sealed class GrammarLessonPage : UserControl, IPage, IResumable
{
    readonly GrammarLesson _l;
    public string ResumeKey => $"grammar:{_l.Id}";
    public string ResumeTitle => $"Грамматика · {_l.Title}";
    public string Title => $"Урок {GrammarPage.Num(_l)}";
    public GrammarLessonPage(GrammarLesson l) { _l = l; Build(); }

    void Build()
    {
        var root = new StackPanel { MaxWidth = 900 };
        root.Children.Add(U.Header("文", _l.Title, "Урок " + GrammarPage.Num(_l) + " · " + _l.Level));

        root.Children.Add(U.Card(U.T(_l.Intro, "Body", 15)));

        int n = 0;
        foreach (var p in _l.Points)
        {
            n++;
            var sp = new StackPanel();
            var patt = PatternView(p.Pattern);
            sp.Children.Add(U.Row(12, U.Pill($"{n}", U.B("Surface3"), U.B("Text")).CenterV(), patt));
            // Буквы-заглушки в формуле (X, Y, A, B) — поясняем, что это места для своих слов
            var slots = p.Pattern.Where(c => c is >= 'A' and <= 'Z').Distinct().ToArray();
            if (slots.Length > 0)
                sp.Children.Add(U.T($"{string.Join(" и ", slots)} в рамках — места, куда подставляются твои слова (смотри примеры ниже)", "Muted", 13).M(0, 8, 0, 0));
            sp.Children.Add(U.T(p.Explain, "Body", 14.5).M(0, 12, 0, 6));
            foreach (var ex in p.Examples)
            {
                var row = U.Cols("4", "*", "auto");
                row.Children.Add(new Border { Background = U.B("AccentGradient"), CornerRadius = new CornerRadius(2) });
                row.Children.Add(U.Stack(
                    U.Jp(ex.Jp, 19),
                    U.Jp(ex.Kana, 13, U.B("Faint")),
                    U.T(ex.Romaji, "Body", 13, U.B("Muted")),
                    U.T(ex.Ru, "Body", 13.5).M(0, 2, 0, 0)).M(14, 0, 0, 0).Col(1));
                row.Children.Add(U.Row(4, U.SpeakBtn(() => ex.Jp, true), U.SpeakBtn(() => ex.Jp)).CenterV().Col(2));
                sp.Children.Add(new Border { Background = U.B("Surface2"), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 12, 10, 12), Margin = new Thickness(0, 10, 0, 0), Child = row });
            }
            root.Children.Add(U.Card(sp).M(0, 16, 0, 0));
        }

        var cta = U.Cols("*", "auto");
        cta.Children.Add(U.Stack(U.T("Закрепим?", "H2"), U.T($"{_l.Quiz.Length} упражнений. Набери 70%, чтобы пройти урок.", "Muted")).CenterV());
        cta.Children.Add(U.Btn("Начать упражнения", StartQuiz, "Primary", "").Col(1));
        var ctaCard = U.Card(U.Stackify(cta)).M(0, 20, 0, 0);
        ctaCard.BorderBrush = U.B("Accent");
        root.Children.Add(ctaCard);

        var wrap = new Grid();
        wrap.Children.Add(root);
        root.HorizontalAlignment = HorizontalAlignment.Center;
        Content = U.Page(wrap);
        Anim.Stagger(root, 70, 0, 12);
    }

    /// <summary>Формула урока: японская часть акцентным цветом, заглушки X/Y/A/B — в рамочках другого цвета.</summary>
    static Control PatternView(string pattern)
    {
        var wrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var buf = new System.Text.StringBuilder();
        void Flush()
        {
            if (buf.Length == 0) return;
            wrap.Children.Add(new TextBlock { Text = buf.ToString(), FontFamily = U.R<FontFamily>("JpFont"), FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = U.B("Accent"), VerticalAlignment = VerticalAlignment.Center });
            buf.Clear();
        }
        foreach (var c in pattern)
        {
            if (c is >= 'A' and <= 'Z')
            {
                Flush();
                wrap.Children.Add(new Border
                {
                    BorderBrush = U.B("Accent2"), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 0, 8, 0),
                    Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center, 
                    Child = new TextBlock { Text = c.ToString(), FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = U.B("Accent2") },
                }.Tip("Сюда подставляется слово"));
            }
            else buf.Append(c);
        }
        Flush();
        return wrap;
    }

    void StartQuiz()
    {
        int qi = 0;
        var qs = _l.Quiz.Select(q => new QuizQ
        {
            Key = $"g:{_l.Id}:{qi++}",
            Prompt = q.Question, PromptSize = 34, Sub = q.Ru, Answer = q.Answer, Options = q.Options.Shuffled().ToArray(),
            OptionsJp = true, Speak = q.Question.Replace("＿＿", q.Answer).Replace("＿", q.Answer),
            Explain = q.Question.Replace("＿", q.Answer) + "  ·  " + Db.RomajiFor(q.Question.Replace("＿", q.Answer), ""),
        });
        var id = _l.Id;
        Shell.Open(new QuizSession($"Урок {id}: упражнения", qs, (ok, total) =>
        {
            if (total > 0 && ok * 100 / total >= 70 && !ProgressStore.P.GrammarDone.Contains(id))
            {
                ProgressStore.GrammarComplete(id);
                Shell.Toast($"Урок {id} пройден! +20 XP", "合格");
            }
        }));
    }
}
