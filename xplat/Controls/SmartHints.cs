using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Matsubakira.Services;
using Matsubakira.Views;

namespace Matsubakira.Controls;

/// <summary>
/// Умная подсказка после ошибки: с чем не путать (похожие по звучанию или значению слова),
/// из каких кандзи состоит слово (с переходом к разбору) и ссылка на урок грамматики.
/// </summary>
public static class SmartHints
{
    public static Control? For(QuizQ q)
    {
        if (q.Key is not { Length: > 2 } key) return null;
        var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        if (key.StartsWith("g:") && key.Split(':') is [_, var ls, _] && int.TryParse(ls, out var lid) && lid >= 1 && lid <= Db.Grammar.Length)
        {
            var l = Db.Grammar[lid - 1];
            sp.Children.Add(Link("文", $"Повтори правило: {l.Title}", () => Shell.Open(new GrammarLessonPage(l))));
            return sp;
        }

        var w = key.StartsWith("w:") ? Db.Words.FirstOrDefault(x => x.Id == key) : null;
        var kj = w == null && key.StartsWith("k:") ? Db.Kanji.FirstOrDefault(x => x.Id == key) : null;
        if (w != null)
        {
            // Не путай: омофоны и слова с тем же переводом
            var similar = Db.Words.Where(x => x.Id != w.Id && (x.Kana == w.Kana || (x.Ru == w.Ru && x.Jp != w.Jp) || SharesKanji(x.Jp, w.Jp) && x.Jp.Length <= 3))
                .OrderBy(x => x.Kana == w.Kana ? 0 : x.Ru == w.Ru ? 1 : 2).Take(3).ToList();
            if (similar.Count > 0)
            {
                var row = new WrapPanel();
                row.Children.Add(U.T("Не путай:", "Muted", 12.5).CenterV().M(0, 0, 8, 4));
                foreach (var s in similar)
                    row.Children.Add(Chip($"{s.Jp} — {s.Ru.Split(';')[0].Trim()}", s.Kana == w.Kana ? "Звучит так же" : s.Ru == w.Ru ? "Тот же перевод — смотри разницу в словаре" : "Похожая запись", () => OpenWord(s.Jp, s.Kana)));
                sp.Children.Add(row);
            }
            // Разбор кандзи слова
            var kanji = w.Jp.Where(Reading.IsKanji).Distinct().Select(c => Db.Kanji.FirstOrDefault(k => k.Char == c.ToString())).OfType<KanjiInfo>().ToList();
            if (kanji.Count > 0)
            {
                var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                row.Children.Add(U.T("Кандзи:", "Muted", 12.5).CenterV().M(0, 0, 8, 4));
                foreach (var k in kanji)
                {
                    var mn = Mnemonics.For(k.Char);
                    row.Children.Add(Chip($"{k.Char} {k.Ru}", $"он: {k.On} · кун: {k.Kun}" + (mn?.Story is { Length: > 0 } h ? "\n" + h : ""), () => OpenWord(k.Char, null)));
                }
                sp.Children.Add(row);
            }
        }
        else if (kj != null)
        {
            var mn = Mnemonics.For(kj.Char);
            if (mn?.Story is { Length: > 0 } hint) sp.Children.Add(U.T("Запомни: " + hint, "Body", 13).Also(t => t.TextWrapping = TextWrapping.Wrap));
            var lookalike = Db.Kanji.Where(x => x.Id != kj.Id && mn != null && Mnemonics.For(x.Char) is { } m2 && m2.Parts.Any(p => mn.Parts.Any(q2 => q2.Length > 0 && p.Length > 0 && q2[0] == p[0]))).Take(3).ToList();
            if (lookalike.Count > 0)
            {
                var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                row.Children.Add(U.T("Похожие:", "Muted", 12.5).CenterV().M(0, 0, 8, 4));
                foreach (var x in lookalike) row.Children.Add(Chip($"{x.Char} {x.Ru}", $"он: {x.On} · кун: {x.Kun}", () => OpenWord(x.Char, null)));
                sp.Children.Add(row);
            }
            sp.Children.Add(Link("漢", "Разбор знака: части, порядок черт, слова", () => OpenWord(kj.Char, null)));
        }
        return sp.Children.Count > 0 ? sp : null;
    }

    static bool SharesKanji(string a, string b) => a.Any(c => Reading.IsKanji(c) && b.Contains(c));

    static void OpenWord(string jp, string? _)
    {
        if (JDict.Lookup(jp) is { } e) Shell.Open(new WordPage(e));
        else { Shell.Go("dict"); DictPage.PendingQuery = jp; }
    }

    static Border Chip(string text, string tip, Action go)
    {
        var b = new Border
        {
            Child = U.Jp(text, 13).Also(t => t.TextWrapping = TextWrapping.NoWrap), Padding = new Thickness(9, 3), Margin = new Thickness(0, 0, 6, 4),
            CornerRadius = new CornerRadius(8), Background = U.B("Surface2"), BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
        }.Tip(tip);
        b.OnClick(() => go());
        return b;
    }

    static Control Link(string jp, string text, Action go)
    {
        var btn = U.Btn(text, go, "Ghost", "");
        btn.HorizontalAlignment = HorizontalAlignment.Left;
        btn.Padding = new Thickness(0, 4);
        return U.Row(8, U.Jp(jp, 15, U.B("Accent")).CenterV(), btn).M(0, 4, 0, 0);
    }
}
