using System.Text;

namespace Matsubakira.Services;

/// <summary>
/// Экспорт карточек для Anki: текстовый файл с табуляциями и заголовками Anki 2.1.55+
/// (#separator, #html, #tags column) — импортируется в любую колоду одной кнопкой.
/// </summary>
public static class AnkiExport
{
    public enum Set { Learning, Level, Mistakes }

    /// <summary>Режим #html:true: «<», «>» и «&» в переводах экранируются, иначе карточка ломается (Г10).</summary>
    static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s.Replace("\t", " ").Replace("\r", "")).Replace("\n", "<br>");
    static string Tag(string s) => new(s.Replace(' ', '_').Replace("·", "").Where(c => !char.IsWhiteSpace(c)).ToArray());

    public static (string text, int count) Build(Set set)
    {
        var p = ProgressStore.P;
        IEnumerable<Word> words = set switch
        {
            Set.Level => Db.WordsOf(p.Level),
            Set.Mistakes => Db.Words.Where(w => p.Mistakes.ContainsKey(w.Id)),
            _ => Db.Words.Where(w => ProgressStore.Card(w.Id) is { Reps: > 0 }),
        };
        IEnumerable<KanjiInfo> kanji = set switch
        {
            Set.Level => Db.KanjiOf(p.Level),
            Set.Mistakes => Db.Kanji.Where(k => p.Mistakes.ContainsKey(k.Id)),
            _ => Db.Kanji.Where(k => ProgressStore.Card(k.Id) is { Reps: > 0 }),
        };
        var sb = new StringBuilder();
        sb.AppendLine("#separator:tab");
        sb.AppendLine("#html:true");
        sb.AppendLine("#notetype:Basic");
        sb.AppendLine("#tags column:3");
        int n = 0;
        foreach (var w in words)
        {
            var back = new StringBuilder();
            if (w.HasKanji) back.Append(w.Kana).Append("<br>");
            back.Append("<i>").Append(Esc(w.Romaji)).Append("</i><br><b>").Append(Esc(w.Ru)).Append("</b>");
            if (w.ExJp.Length > 0) back.Append("<br><br>").Append(Esc(w.ExJp)).Append("<br><small>").Append(Esc(w.ExRu)).Append("</small>");
            if (p.Notes.TryGetValue(w.Id, out var note)) back.Append("<br><br>✎ ").Append(Esc(note));
            sb.Append(Esc(w.Jp)).Append('\t').Append(back).Append('\t').Append("kotodama ").Append(Tag(w.Level)).Append(' ').Append(Tag(w.Topic)).AppendLine();
            n++;
        }
        foreach (var k in kanji)
        {
            var back = $"<b>{Esc(k.Ru)}</b><br>он: {Esc(k.On)}<br>кун: {Esc(k.Kun.Length > 0 ? k.Kun : "—")}";
            if (k.Examples.Length > 0) back += "<br><br>" + string.Join("<br>", k.Examples.Take(3).Select(e => $"{Esc(e.Jp)} ({Esc(e.Kana)}) — {Esc(e.Ru)}"));
            sb.Append(k.Char).Append('\t').Append(back).Append('\t').Append("kotodama kanji ").Append(Tag(k.Level)).AppendLine();
            n++;
        }
        return (sb.ToString(), n);
    }
}
