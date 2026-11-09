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

public sealed class HomePage : UserControl, IPage
{
    public string Title => "Главная";

    public void OnShown() => Content = U.Page(Build());

    Control Build()
    {
        var p = ProgressStore.P;
        var root = new StackPanel();

        // ─────────── Приветствие ───────────
        var (jpGreet, ruGreet) = DateTime.Now.Hour switch
        {
            < 5 => ("こんばんは", "Доброй ночи"),
            < 11 => ("おはようございます", "Доброе утро"),
            < 18 => ("こんにちは", "Добрый день"),
            _ => ("こんばんは", "Добрый вечер"),
        };
        root.Children.Add(U.Jp(jpGreet, 15, U.B("Accent")));
        root.Children.Add(U.T(ruGreet, "H1", 30).M(0, 2, 0, 22));

        // ─────────── Продолжить путь + повторение ───────────
        var row = U.Cols("*", "16", "300");
        var u = Course.Current;
        int d = Course.DoneCount(u), t = Course.Total(u);
        var cont = new StackPanel();
        cont.Children.Add(U.T("ПРОДОЛЖИТЬ ПУТЬ", "Overline"));
        cont.Children.Add(U.T($"Юнит {u.Id}. {u.Title}", "H2", 19).M(0, 6, 0, 4));
        cont.Children.Add(U.T(d == 0 ? "Начни с теории — потом практика." : $"Пройдено {d} из {t} шагов", "Muted"));
        var pr = U.Cols("*", "auto");
        pr.Margin = new Thickness(0, 16, 0, 0);
        pr.Children.Add(new ProgressBar { Maximum = t, Value = d, Height = 4, VerticalAlignment = VerticalAlignment.Center });
        pr.Children.Add(U.Btn(d == 0 ? "Начать" : "Продолжить", () => { Shell.Go("path"); Shell.Open(new UnitPage(u)); }, "Primary", "").M(18, 0, 0, 0).Col(1));
        cont.Children.Add(pr);
        row.Children.Add(U.Card(cont, new Thickness(22)));

        int due = p.Cards.Count(kv => kv.Value.Due <= DateTime.Now);
        var rev = new StackPanel();
        rev.Children.Add(U.T("ПОВТОРЕНИЕ", "Overline"));
        var dueNum = U.T("0", "H1", 34);
        Anim.CountUp(dueNum, due);
        rev.Children.Add(U.Row(8, dueNum, U.T(MainWindow.Plural(due, "карточка", "карточки", "карточек"), "Muted").CenterV().M(0, 10, 0, 0)).M(0, 4, 0, 0));
        var rb = U.Btn(due > 0 ? "Повторить" : "Всё повторено", () => Shell.Go("review"), "", "");
        rb.IsEnabled = due > 0; rb.HorizontalAlignment = HorizontalAlignment.Left; rb.Margin = new Thickness(0, 10, 0, 0);
        rev.Children.Add(rb);
        row.Children.Add(U.Card(rev, new Thickness(22)).Col(2));
        root.Children.Add(row);

        // ─────────── Статистика ───────────
        int wordsKnown = Db.Words.Count(w => ProgressStore.Card(w.Id)?.Reps > 0);
        int kanjiKnown = Db.Kanji.Count(k => ProgressStore.Card(k.Id)?.Reps > 0);
        var stats = new UniformGrid { Columns = 4, Margin = new Thickness(-6, 16, -6, 0) };
        stats.Children.Add(Stat("Серия", p.Streak, MainWindow.Plural(p.Streak, "день", "дня", "дней"), "Gold"));
        stats.Children.Add(Stat("Сегодня", ProgressStore.TodayXp, $"из {p.DailyGoal} XP", "Accent"));
        stats.Children.Add(Stat("Слова", wordsKnown, "в изучении", "Info"));
        stats.Children.Add(Stat("Кандзи", kanjiKnown, $"из {Db.Kanji.Length}", "Good"));
        root.Children.Add(stats);
        root.Children.Add(TrainersPage.PlanCard().M(0, 16, 0, 0));
        root.Children.Add(Report.Build().M(0, 16, 0, 0));
        var hint = U.T("Совет: Ctrl+K — быстрый поиск по всему приложению", "Muted", 12);
        hint.Margin = new Thickness(2, 10, 0, 0);
        root.Children.Add(hint);

        // ─────────── Разделы ───────────
        root.Children.Add(U.T("Разделы", "H2").M(0, 28, 0, 12));
        int kanaKnown = Kana.All.Count(k => ProgressStore.KanaScore("h:" + k.Hira) >= 3) + Kana.All.Count(k => ProgressStore.KanaScore("k:" + k.Kata) >= 3);
        var sections = new UniformGrid { Columns = 4, Margin = new Thickness(-6, 0, -6, 0) };
        sections.Children.Add(Section("あ", "Кана", $"{kanaKnown * 100 / (Kana.All.Count * 2)}% освоено", "kana"));
        sections.Children.Add(Section("語", "Слова", $"{Db.Words.Length} слов", "words"));
        sections.Children.Add(Section("漢", "Кандзи", $"{Db.Kanji.Length} знаков", "kanji"));
        sections.Children.Add(Section("文", "Грамматика", $"{p.GrammarDone.Count}/{Db.Grammar.Length} уроков", "grammar"));
        sections.Children.Add(Section("話", "Говорение", $"{Db.SpeechLessons.Length} уроков", "speech"));
        sections.Children.Add(Section("対", "Диалоги", $"{Db.Dialogs.Length} сценок", "dialogs"));
        sections.Children.Add(Section("辞", "Словарь", "69 000+ слов", "dict"));
        sections.Children.Add(Section("読", "Чтение", "тексты с фуриганой", "reader"));
        root.Children.Add(sections);
        Anim.Stagger(sections, 30, 120);

        // ─────────── Активность + слово дня ───────────
        var bottom = U.Cols("*", "16", "340");
        bottom.Margin = new Thickness(0, 28, 0, 0);
        bottom.Children.Add(Activity());
        bottom.Children.Add(WordOfDay().Col(2));
        root.Children.Add(bottom);

        Anim.FadeUp(row, 40, 12, 380);
        Anim.FadeUp(stats, 90, 12, 380);
        return root;
    }

    static Border Stat(string label, int value, string unit, string color)
    {
        var num = U.T("0", "H1", 24, U.B(color));
        Anim.CountUp(num, value);
        var sp = U.Stack(U.T(label, "Muted", 12), U.Row(6, num, U.T(unit, "Muted", 12).CenterV().M(0, 6, 0, 0)).M(0, 2, 0, 0));
        return new Border { Classes = { "Card" }, Child = sp, Margin = new Thickness(6), Padding = new Thickness(16, 12, 16, 12) };
    }

    static Border Section(string jp, string title, string sub, string key)
    {
        var g = U.Cols("auto", "*");
        g.Children.Add(U.Jp(jp, 20, U.B("Accent"), true).CenterV());
        g.Children.Add(U.Stack(U.T(title, "H3", 14), U.T(sub, "Muted", 12)).CenterV().M(12, 0, 0, 0).Col(1));
        var card = new Border { Classes = { "Card" }, Child = g, Margin = new Thickness(6), Padding = new Thickness(16, 12, 16, 12), Cursor = Cursors.Hand };
        Hover(card);
        card.PointerReleased += (_, _) => Shell.Go(key);
        return card;
    }

    /// <summary>Лёгкий подъём карточки и подсветка рамки при наведении (только трансформация, без перерасчёта layout).</summary>
    public static void Hover(Border card)
    {
        var t = new TranslateTransform();
        card.RenderTransform = t;
        var normal = card.BorderBrush;
        card.PointerEntered += (_, _) => { Anim.Run((t, "y"), t.Y, -3, 180, v => t.Y = v); card.BorderBrush = U.B("StrokeStrong"); };
        card.PointerExited += (_, _) => { Anim.Run((t, "y"), t.Y, 0, 260, v => t.Y = v); card.BorderBrush = normal; };
    }

    static Border Activity()
    {
        var sp = new StackPanel();
        sp.Children.Add(U.T("Активность", "H3"));
        sp.Children.Add(U.T("Последние 18 недель", "Muted", 12).M(0, 2, 0, 14));
        const int weeks = 18;
        var grid = new UniformGrid { Rows = 7, Columns = weeks, HorizontalAlignment = HorizontalAlignment.Left };
        var start = DateTime.Today.AddDays(-(weeks * 7 - 1) - ((int)DateTime.Today.DayOfWeek + 6) % 7 + 6);
        var cells = new Border[7, weeks];
        for (int w = 0; w < weeks; w++)
            for (int d = 0; d < 7; d++)
            {
                var day = start.AddDays(w * 7 + d);
                int xp = ProgressStore.P.History.TryGetValue(day.ToString("yyyy-MM-dd"), out var x) ? x : 0;
                byte a = xp == 0 ? (byte)0 : xp < 20 ? (byte)70 : xp < 50 ? (byte)140 : (byte)230;
                cells[d, w] = new Border
                {
                    Width = 15, Height = 15, Margin = new Thickness(2), CornerRadius = new CornerRadius(4),
                    Background = day > DateTime.Today ? Brushes.Transparent : xp == 0 ? U.B("Surface2") : new SolidColorBrush(Color.FromArgb(a, 0xF0, 0x7A, 0xA0)),
                    
                }.Tip(day > DateTime.Today ? null : $"{day:d MMM}: {xp} XP");
            }
        for (int d = 0; d < 7; d++) for (int w = 0; w < weeks; w++) grid.Children.Add(cells[d, w]);
        sp.Children.Add(grid);
        return new Border { Classes = { "Card" }, Child = sp };
    }

    static Border WordOfDay()
    {
        // то же слово, что приходит в уведомлении «Слово дня»
        var w = Services.WordOfDay.Today ?? Db.Words[DateTime.Today.DayOfYear % Db.Words.Length];
        var sp = new StackPanel();
        var head = U.Cols("*", "auto");
        head.Children.Add(U.T("Слово дня", "H3"));
        head.Children.Add(U.SpeakBtn(() => w.Kana).Col(1));
        sp.Children.Add(head);
        sp.Children.Add(U.Jp(w.Jp, 40, null, true).M(0, 8, 0, 0));
        sp.Children.Add(U.Jp(w.Kana, 14, U.B("Accent")).M(0, 4, 0, 0));
        sp.Children.Add(U.T(w.Romaji, "Muted", 13));
        sp.Children.Add(U.T(w.Ru, "H3", 16).M(0, 10, 0, 0));
        sp.Children.Add(U.T("Нажми — пример фразы", "Muted", 11.5).M(0, 8, 0, 0));
        var card = new Border { Classes = { "Card" }, Child = sp, Cursor = Cursors.Hand };
        card.PointerReleased += (_, e) => { if (e.Source is not Button && e.Source is not TextBlock { Parent: Button }) Services.WordOfDay.Show(force: true); };
        return card;
    }
}
