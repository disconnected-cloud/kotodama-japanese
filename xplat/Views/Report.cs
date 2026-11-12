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

namespace Matsubakira.Views;

/// <summary>Недельный отчёт: XP по дням, прогноз повторений FSRS на 7 дней и самые трудные карточки.</summary>
public static class Report
{
    public static Border Build()
    {
        var p = ProgressStore.P;
        var g = U.Cols("*", "20", "*", "20", "*");

        // XP за 7 дней
        var days = Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(i - 6)).ToArray();
        var xp = days.Select(d => p.History.TryGetValue(d.ToString("yyyy-MM-dd"), out var x) ? x : 0).ToArray();
        g.Children.Add(Bars("ОПЫТ ЗА НЕДЕЛЮ", $"{xp.Sum()} XP", xp, days.Select(d => d.ToString("ddd")).ToArray(), "Accent", highlightLast: true));

        // Прогноз повторений
        var fc = Fsrs.Forecast(p.Cards.Values);
        var labels = Enumerable.Range(0, 7).Select(i => i == 0 ? "сег" : DateTime.Today.AddDays(i).ToString("ddd")).ToArray();
        g.Children.Add(Bars("ПРОГНОЗ ПОВТОРЕНИЙ", $"{fc.Sum()} за 7 дней", fc, labels, "Info", highlightLast: false).Col(2));

        // Трудные карточки
        var hard = p.Cards.Where(kv => kv.Value.Lapses > 0 || kv.Value.Difficulty > 7).OrderByDescending(kv => kv.Value.Lapses * 2 + kv.Value.Difficulty).Take(5).ToList();
        var hs = new StackPanel();
        hs.Children.Add(U.T("САМЫЕ ТРУДНЫЕ", "Overline"));
        if (hard.Count == 0) hs.Children.Add(U.T("Пока нет — карточки идут легко!", "Muted").M(0, 10, 0, 0));
        foreach (var (id, c) in hard.Select(kv => (kv.Key, kv.Value)))
        {
            string jp = id.StartsWith("w:") ? id[2..].Split('|')[0] : id.StartsWith("k:") ? id[2..] : id;
            string ru = Db.Words.FirstOrDefault(w => w.Id == id)?.Ru ?? Db.Kanji.FirstOrDefault(k => k.Id == id)?.Ru ?? "";
            var row = U.Cols("auto", "*", "auto");
            row.Margin = new Thickness(0, 8, 0, 0);
            row.Children.Add(U.Jp(jp, 17).CenterV());
            row.Children.Add(U.T(ru, "Muted", 12).Also(t => t.TextTrimming = TextTrimming.CharacterEllipsis).Also(t => t.TextWrapping = TextWrapping.NoWrap).CenterV().M(10, 0, 8, 0).Col(1));
            row.Children.Add(U.T($"×{c.Lapses}", "Muted", 12).CenterV().Col(2));
            hs.Children.Add(row);
        }
        g.Children.Add(hs.Col(4));
        var all = new StackPanel();
        all.Children.Add(g);
        all.Children.Add(new Border { Height = 1, Background = U.B("Stroke"), Margin = new Thickness(0, 18, 0, 14) });
        all.Children.Add(Goal());
        return U.Card(all, new Thickness(22));
    }

    /// <summary>Цель уровня JLPT: сколько слов, кандзи и грамматики осталось и когда при текущем темпе уровень будет пройден.</summary>
    public static Control Goal()
    {
        var p = ProgressStore.P;
        string lvl = p.Level is "N4" or "N3" ? p.Level : "N5";
        var words = Db.WordsOf(lvl).ToList();
        var kanji = Db.KanjiOf(lvl).ToList();
        var gram = Db.Grammar.Where(x => x.Level == lvl).ToList();
        int wk = words.Count(w => ProgressStore.Card(w.Id) != null), kk = kanji.Count(k => ProgressStore.Card(k.Id) != null), gk = gram.Count(x => p.GrammarDone.Contains(x.Id));
        int left = words.Count - wk + kanji.Count - kk + (gram.Count - gk) * 5; // урок грамматики ≈ 5 карточек по времени
        // темп: новые карточки за 2 недели (карточки с 1–3 повторениями, открытые недавно)
        int recent = p.Cards.Values.Count(c => c.Reps <= 3 && c.LastReview >= DateTime.Now.AddDays(-14));
        double pace = recent / 14.0;
        string forecast = left <= 0 ? $"Уровень {lvl} пройден — пора к следующему!"
            : pace >= 1 ? $"В твоём темпе (~{pace:0} в день) {lvl} будет пройден примерно к {DateTime.Today.AddDays(Math.Ceiling(left / pace)):d MMMM yyyy}."
            : $"Если учить по 10 новых слов и кандзи в день, {lvl} будет пройден примерно к {DateTime.Today.AddDays(Math.Ceiling(left / 10.0)):d MMMM yyyy}.";
        var g = U.Cols("*", "20", "*", "20", "*");
        g.Children.Add(Meter("Слова " + lvl, wk, words.Count, "Info"));
        g.Children.Add(Meter("Кандзи " + lvl, kk, kanji.Count, "Good").Col(2));
        g.Children.Add(Meter("Грамматика " + lvl, gk, gram.Count, "Accent2").Col(4));
        return U.Stack(U.T("ЦЕЛЬ: JLPT " + lvl, "Overline"), U.T(forecast, "H3", 15).Also(t => t.TextWrapping = TextWrapping.Wrap).M(0, 4, 0, 12), U.Stackify(g));
    }

    static Control Meter(string label, int done, int total, string brush)
    {
        var head = U.Cols("*", "auto");
        head.Children.Add(U.T(label, "Muted", 12.5));
        head.Children.Add(U.T($"{done} / {total}", "Body", 12.5).Col(1));
        return U.Stack(head, new ProgressBar { Maximum = Math.Max(1, total), Value = done, Height = 6, Foreground = U.B(brush), Margin = new Thickness(0, 6, 0, 0) });
    }

    public static Control Bars(string title, string total, int[] values, string[] labels, string brush, bool highlightLast, double barW = 14, string[]? tips = null)
    {
        var sp = new StackPanel();
        sp.Children.Add(U.T(title, "Overline"));
        sp.Children.Add(U.T(total, "H3", 16).M(0, 4, 0, 10));
        int max = Math.Max(1, values.Max());
        var bars = new UniformGrid { Columns = values.Length, Height = 92 };
        for (int i = 0; i < values.Length; i++)
        {
            var col = new Grid();
            col.RowDefinitions.Add(new RowDefinition());
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            double h = values[i] == 0 ? 3 : 8 + 60.0 * values[i] / max;
            var bar = new Border
            {
                Height = h, Width = barW, CornerRadius = new CornerRadius(Math.Min(4, barW / 2)), VerticalAlignment = VerticalAlignment.Bottom,
                Background = values[i] == 0 ? U.B("Surface3") : U.B(brush), Opacity = highlightLast && i < values.Length - 1 ? 0.6 : 1,
                RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            };
            bar.Tip(tips?[i] ?? values[i].ToString());
            var st = new ScaleTransform(1, 0); bar.RenderTransform = st;
            Anim.Run((st, "sy"), 0, 1, 500, v => st.ScaleY = v, null, 420 * i / values.Length);
            col.Children.Add(bar);
            col.Children.Add(U.T(labels[i], "Muted", 10.5).Also(t => { t.TextWrapping = TextWrapping.NoWrap; t.Margin = new Thickness(-20, 4, -20, 0); t.TextAlignment = TextAlignment.Center; }).RowAt(1));
            bars.Children.Add(col);
        }
        sp.Children.Add(bars);
        return sp;
    }
}
