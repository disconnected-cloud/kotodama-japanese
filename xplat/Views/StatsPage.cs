using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>«Прогресс»: активность за год, опыт и прогноз повторений, состояние памяти (FSRS), навыки, цель JLPT.</summary>
public sealed class StatsPage : UserControl, IPage
{
    public string Title => "Прогресс";

    public void OnShown() => Content = U.Page(Build());

    static Control Build()
    {
        var p = ProgressStore.P;
        var root = new StackPanel();
        root.Children.Add(U.Header("進歩", "Прогресс", "Как растут знания: активность, память, навыки и прогноз до цели"));

        // ─────────── Главные числа ───────────
        var cards = p.Cards.Values.ToList();
        int learned = cards.Count(c => c.Reps > 0 || c.Lapses > 0);
        int mastered = cards.Count(c => c.Mastered);
        int days = p.History.Count(kv => kv.Value > 0);
        double recall = cards.Count == 0 ? 0 : cards.Where(c => c.Stability > 0)
            .Select(c => Fsrs.Retrievability(Math.Max(0, (DateTime.Now - c.LastReview).TotalDays), c.Stability)).DefaultIfEmpty(0).Average();
        var kpi = new UniformGrid { Columns = U.Columns(4, 2), Margin = new Thickness(-6, 0, -6, 0) };
        kpi.Children.Add(Kpi("flame", "Серия", p.Streak, MainView.Plural(p.Streak, "день", "дня", "дней"), $"Дней с занятиями всего: {days}"));
        kpi.Children.Add(Kpi("trophy", "Уровень", ProgressStore.Level, $"{p.Xp} XP", "Опыт за всё время"));
        kpi.Children.Add(Kpi("words", "Карточек", learned, $"{mastered} прочно", "Прочно — интервал повторения от 3 недель"));
        kpi.Children.Add(Kpi("target", "Память", (int)Math.Round(recall * 100), "%", "Средняя вероятность вспомнить карточку прямо сейчас (модель FSRS)"));
        root.Children.Add(kpi);
        Anim.Stagger(kpi, 50, 60);

        // ─────────── Активность за год ───────────
        root.Children.Add(U.Card(Heatmap(), new Thickness(22, 18, 22, 18)).M(0, 16, 0, 0));

        // ─────────── Опыт за 30 дней + прогноз повторений на 14 ───────────
        var d30 = Enumerable.Range(0, 30).Select(i => DateTime.Today.AddDays(i - 29)).ToArray();
        var xp = d30.Select(d => p.History.GetValueOrDefault(d.ToString("yyyy-MM-dd"))).ToArray();
        var xpChart = Report.Bars("ОПЫТ ЗА 30 ДНЕЙ", $"{xp.Sum()} XP · в среднем {xp.Sum() / 30} в день", xp,
            d30.Select((d, i) => i % 7 == 1 ? d.ToString("dd.MM") : "").ToArray(), "Accent", true, Shell.Compact ? 5 : 9,
            d30.Select((d, i) => $"{d:d MMM}: {xp[i]} XP").ToArray());
        var fc = Fsrs.Forecast(cards, 14);
        var fcChart = Report.Bars("ПРОГНОЗ ПОВТОРЕНИЙ", $"{fc.Sum()} карточек за 2 недели", fc,
            fc.Select((_, i) => i == 0 ? "сег" : i % 4 == 0 ? DateTime.Today.AddDays(i).ToString("dd.MM") : "").ToArray(), "Info", false, Shell.Compact ? 8 : 12,
            fc.Select((v, i) => $"{(i == 0 ? "Сегодня" : DateTime.Today.AddDays(i).ToString("ddd, d MMM"))}: {v}").ToArray());
        var two = U.Cols("3*", "16", "2*");
        two.Children.Add(U.Card(xpChart, new Thickness(22)));
        two.Children.Add(U.Card(fcChart, new Thickness(22)).Col(2));
        root.Children.Add(U.Stackify(two).M(0, 16, 0, 0));

        // ─────────── Память и навыки ───────────
        var mem = U.Cols("*", "16", "*");
        mem.Children.Add(U.Card(Memory(cards), new Thickness(22)));
        mem.Children.Add(U.Card(Skills(), new Thickness(22)).Col(2));
        root.Children.Add(U.Stackify(mem).M(0, 16, 0, 0));

        root.Children.Add(U.Card(Report.Goal(), new Thickness(22)).M(0, 16, 0, 0));
        return root;
    }

    static Border Kpi(string icon, string label, int value, string unit, string tip)
    {
        var num = U.T("0", "H1", 26);
        Anim.CountUp(num, value);
        var g = U.Cols("auto", "*");
        g.Children.Add(Art.Icon(icon, 38).CenterV());
        g.Children.Add(U.Stack(U.T(label, "Muted", 12), U.Row(6, num, U.T(unit, "Muted", 12).CenterV().M(0, 6, 0, 0))).CenterV().M(12, 0, 0, 0).Col(1));
        return new Border { Classes = { "Card" }, Child = g, Margin = new Thickness(6), Padding = new Thickness(16, 12) }.Tip(tip);
    }

    /// <summary>Тепловая карта как на GitHub: 53 недели, оттенок акцента по количеству опыта.</summary>
    static Control Heatmap()
    {
        var hist = ProgressStore.P.History;
        int weeks = Shell.Compact ? 17 : 53;
        double cell = Shell.Compact ? 14 : 12.5;
        var accent = ((ISolidColorBrush)U.B("Accent")).Color;
        var today = DateTime.Today;
        var start = today.AddDays(-((int)today.DayOfWeek + 6) % 7).AddDays(-7 * (weeks - 1)); // понедельник первой недели
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        for (int w = 0; w < weeks; w++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int d = 0; d < 7; d++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        int total = 0, active = 0, best = 0, run = 0;
        for (int w = 0; w < weeks; w++)
        {
            var monday = start.AddDays(w * 7);
            if (monday.Day <= 7) grid.Children.Add(U.T(monday.ToString("MMM"), "Muted", 10).M(1, 0, 0, 3).Col(w, 4));
            for (int d = 0; d < 7; d++)
            {
                var day = monday.AddDays(d);
                if (day > today) continue;
                int x = hist.GetValueOrDefault(day.ToString("yyyy-MM-dd"));
                total += x; if (x > 0) { active++; run++; best = Math.Max(best, run); } else run = 0;
                byte a = x == 0 ? (byte)0 : x < 20 ? (byte)70 : x < 50 ? (byte)130 : x < 100 ? (byte)190 : (byte)255;
                var b = new Border
                {
                    Width = cell, Height = cell, Margin = new Thickness(1.5), CornerRadius = new CornerRadius(3),
                    Background = x == 0 ? U.B("Surface2") : new SolidColorBrush(Color.FromArgb(a, accent.R, accent.G, accent.B)),
                    BorderBrush = day == today ? U.B("Text") : null, BorderThickness = new Thickness(day == today ? 1 : 0),
                }.Tip($"{day:ddd, d MMMM}: {x} XP");
                Grid.SetColumn(b, w); Grid.SetRow(b, d + 1);
                grid.Children.Add(b);
            }
        }
        var head = U.Cols("*", "auto");
        head.Children.Add(U.Stack(U.T("АКТИВНОСТЬ", "Overline"), U.T($"{active} {MainView.Plural(active, "день", "дня", "дней")} занятий · {total} XP · лучшая серия {best}", "H3", 15).M(0, 4, 0, 12)));
        var legend = U.Row(3, U.T("меньше", "Muted", 10.5).CenterV().M(0, 0, 4, 0));
        foreach (byte a in new byte[] { 0, 70, 130, 190, 255 })
            legend.Children.Add(new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(3), Background = a == 0 ? U.B("Surface2") : new SolidColorBrush(Color.FromArgb(a, accent.R, accent.G, accent.B)) }.CenterV());
        legend.Children.Add(U.T("больше", "Muted", 10.5).CenterV().M(4, 0, 0, 0));
        head.Children.Add(legend.DesktopOnly().Col(1));
        var sv = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = grid };
        sv.Loaded += (_, _) => sv.Offset = new Vector(sv.Extent.Width, 0); // на узком экране видна последняя неделя
        return U.Stack(head, sv);
    }

    /// <summary>Зрелость карточек: одна полоса из сегментов одного оттенка (новые → прочные) + легенда.</summary>
    static Control Memory(List<SrsCard> cards)
    {
        int learning = cards.Count(c => c.Reps == 0 || Math.Max(c.Interval, c.Stability) < 1);
        int young = cards.Count(c => c.Reps > 0 && Math.Max(c.Interval, c.Stability) is >= 1 and < 21);
        int mature = cards.Count(c => c.Mastered);
        int all = Math.Max(1, learning + young + mature);
        var info = ((ISolidColorBrush)U.B("Info")).Color;
        var segs = new (string name, int n, byte a, string tip)[]
        {
            ("Учу", learning, 90, "Интервал меньше дня — карточка только запоминается"),
            ("Молодые", young, 170, "Интервал от 1 дня до 3 недель"),
            ("Прочные", mature, 255, "Интервал от 3 недель — в долгой памяти"),
        };
        var sp = new StackPanel();
        sp.Children.Add(U.T("ПАМЯТЬ", "Overline"));
        sp.Children.Add(U.T(cards.Count == 0 ? "Карточек пока нет — начни с «Учиться»" : $"{mature * 100 / all}% карточек уже в долгой памяти", "H3", 15).M(0, 4, 0, 14));
        var bar = new Grid { Height = 14 };
        int col = 0;
        foreach (var s in segs)
        {
            if (s.n == 0) continue;
            bar.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(s.n, GridUnitType.Star)));
            var b = new Border { CornerRadius = new CornerRadius(4), Margin = new Thickness(col == 0 ? 0 : 2, 0, 0, 0), Background = new SolidColorBrush(Color.FromArgb(s.a, info.R, info.G, info.B)) }.Tip($"{s.name}: {s.n}");
            Grid.SetColumn(b, col++);
            bar.Children.Add(b);
        }
        if (col == 0) bar.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = U.B("Surface2") });
        sp.Children.Add(bar);
        foreach (var s in segs)
        {
            var row = U.Cols("auto", "*", "auto");
            row.Margin = new Thickness(0, 10, 0, 0);
            row.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(s.a, info.R, info.G, info.B)) }.CenterV());
            row.Children.Add(U.Stack(U.T(s.name, "Body", 13.5), U.T(s.tip, "Muted", 11.5).Also(t => t.TextWrapping = TextWrapping.Wrap)).M(10, 0, 8, 0).Col(1));
            row.Children.Add(U.T(s.n.ToString(), "H3", 14).CenterV().Col(2));
            sp.Children.Add(row);
        }
        return sp;
    }

    static Control Skills()
    {
        var p = ProgressStore.P;
        int kana = Kana.All.Count(k => ProgressStore.KanaScore("h:" + k.Hira) >= 3) + Kana.All.Count(k => ProgressStore.KanaScore("k:" + k.Kata) >= 3);
        var phrases = Db.SpeechLessons.SelectMany(l => l.Items).ToList();
        var spoken = phrases.Where(ph => p.SpeechBest.ContainsKey(ph.Id)).Select(ph => p.SpeechBest[ph.Id]).ToList();
        int texts = p.Flags.Count(f => f.StartsWith("read:"));
        var rows = new (string icon, string name, int done, int total, string note)[]
        {
            ("kana", "Кана", kana, Kana.All.Count * 2, "хирагана и катакана"),
            ("words", "Слова", Db.Words.Count(w => ProgressStore.Card(w.Id) != null), Db.Words.Length, "в изучении"),
            ("kanji", "Кандзи", Db.Kanji.Count(k => ProgressStore.Card(k.Id) != null), Db.Kanji.Length, "в изучении"),
            ("grammar", "Грамматика", p.GrammarDone.Count, Db.Grammar.Length, "уроков пройдено"),
            ("speech", "Говорение", spoken.Count, phrases.Count, spoken.Count > 0 ? $"фраз сказано · средний балл {spoken.Average():0}" : "фраз сказано"),
            ("books", "Чтение", texts, Math.Max(texts, ReaderPage.Texts.Length), "текстов прочитано"),
        };
        var sp = new StackPanel();
        sp.Children.Add(U.T("НАВЫКИ", "Overline"));
        sp.Children.Add(U.T("Что уже освоено", "H3", 15).M(0, 4, 0, 6));
        foreach (var r in rows)
        {
            var g = U.Cols("auto", "*");
            g.Margin = new Thickness(0, 8, 0, 0);
            g.Children.Add(Art.Icon(r.icon, 30).CenterV());
            var head = U.Cols("*", "auto");
            head.Children.Add(U.Row(6, U.T(r.name, "Body", 13.5), U.T(r.note, "Muted", 11.5).CenterV()));
            head.Children.Add(U.T($"{r.done} / {r.total}", "Muted", 12).Col(1));
            g.Children.Add(U.Stack(head, new ProgressBar { Maximum = Math.Max(1, r.total), Value = r.done, Height = 6, Margin = new Thickness(0, 5, 0, 0) }).CenterV().M(12, 0, 0, 0).Col(1));
            sp.Children.Add(g);
        }
        return sp;
    }
}
