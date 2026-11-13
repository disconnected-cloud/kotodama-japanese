using System.Text;
using System.Text.RegularExpressions;
using Matsubakira.Services;

namespace Kotodama.Tests;

/// <summary>
/// Аудит материалов: что выглядит как ошибка в словах, переводах, чтениях и латинице.
/// Отчёт пишется в файл из переменной KOTODAMA_AUDIT (без неё тест пропускается).
/// </summary>
public class DictAudit
{
    static bool IsHira(char c) => c is >= 'ぁ' and <= 'ゖ';
    static bool IsKata(char c) => c is >= 'ァ' and <= 'ヶ' or 'ー';

    public static readonly Regex Junk = new(@"(^|[\s,;])[абвг]\)|◇|\(\(|\(связ|\(инфикс|\(преф|\(суф|^\s*\(|(^|;)\s*:|, :|\bA\b|\bK\b|～|\s{2,}|[,;]\s*$|\(\s*\)|\bсм\.|\bтж\.", RegexOptions.Compiled);

    [Fact]
    public void Report()
    {
        var path = Environment.GetEnvironmentVariable("KOTODAMA_AUDIT");
        if (string.IsNullOrEmpty(path)) return;
        var sb = new StringBuilder();
        void L(string cat, string s) => sb.Append(cat).Append('\t').AppendLine(s);

        foreach (var w in Db.Words)
        {
            // 1. окончание слова каной должно совпадать с концом чтения (食べる — たべる)
            int tail = 0;
            while (tail < w.Jp.Length && IsHira(w.Jp[^(tail + 1)])) tail++;
            if (tail > 0 && tail < w.Jp.Length && !w.Kana.EndsWith(w.Jp[^tail..])) L("окончание", $"{w.Jp} [{w.Kana}] {w.Level}");
            // 2. слово целиком каной — чтение то же самое
            if (w.Jp.All(c => IsHira(c) || IsKata(c)) && Kana.ToHiragana(w.Jp) != Kana.ToHiragana(w.Kana)) L("кана≠чтение", $"{w.Jp} [{w.Kana}]");
            // 3. начало каной (お茶 — おちゃ)
            int head = 0;
            while (head < w.Jp.Length && IsHira(w.Jp[head])) head++;
            if (head > 0 && head < w.Jp.Length && !w.Kana.StartsWith(w.Jp[..head])) L("начало", $"{w.Jp} [{w.Kana}]");
            // 4. мусор в переводе
            if (Junk.IsMatch(w.Ru)) L("перевод", $"{w.Jp} — {w.Ru} ({w.Level})");
            if (w.Ru.Length > 90) L("длинный перевод", $"{w.Jp} — {w.Ru}");
            if (Regex.IsMatch(Regex.Replace(w.Ru, @"\(от [а-я]+\. [^)]*\)", ""), "[A-Za-z]{3,}")) L("латиница в переводе", $"{w.Jp} — {w.Ru}");
            // 5. пример без самого слова (по основе)
            if (w.ExJp.Length > 0)
            {
                var stem = w.Jp.Length > 2 && IsHira(w.Jp[^1]) ? w.Jp[..^1] : w.Jp;
                var kstem = w.Kana.Length > 2 && w.Jp != w.Kana && IsHira(w.Jp[^1]) ? w.Kana[..^1] : w.Kana;
                if (!w.ExJp.Contains(stem) && !w.ExJp.Contains(kstem) && !w.ExJp.Contains(Kana.ToKatakana(kstem)))
                    L("пример без слова", $"{w.Jp} [{w.Kana}] — {w.ExJp}");
            }
            if (w.ExJp.Length > 0 && Junk.IsMatch(w.ExRu)) L("перевод примера", $"{w.ExJp} — {w.ExRu}");
        }

        // 6. латиница фраз: частица は отдельно должна читаться wa
        IEnumerable<(string where, string jp, string kana, string romaji)> Sentences()
        {
            foreach (var l in Db.SpeechLessons) foreach (var p in l.Items) yield return ("фраза", p.Jp, p.Kana, p.Romaji);
            foreach (var d in Db.Dialogs) foreach (var ln in d.Lines) yield return ("диалог " + d.Title, ln.Jp, ln.Kana, ln.Romaji);
            foreach (var g in Db.Grammar) foreach (var pt in g.Points) foreach (var e in pt.Examples) yield return ("грамматика " + g.Id, e.Jp, e.Kana, e.Romaji);
            foreach (var u in Db.Course) foreach (var e in u.Examples) yield return ("юнит " + u.Id, e.Jp, e.Kana, e.Romaji);
            foreach (var k in Db.Kanji) foreach (var e in k.Examples) yield return ("кандзи " + k.Char, e.Jp, e.Kana, e.Romaji);
        }
        foreach (var (where, jp, kana, romaji) in Sentences())
        {
            // латиница из romaji.json (MeCab) против ручной разметки каны пробелами
            if (kana.Contains(' '))
            {
                var hand = Kana.ToRomaji(kana);
                string Norm(string x) => Regex.Replace(x.ToLowerInvariant(), @"[^a-z]", "");
                if (Norm(hand) != Norm(romaji)) L("латиница≠разметка", $"{where}: {jp} | mecab: {romaji} | ручная: {hand}");
            }
            if (kana.Contains(' ') && Regex.IsMatch(" " + kana + " ", @"\sは[\s、。！？]") && !Regex.IsMatch(romaji, @"\bwa\b")) L("ha вместо wa", $"{where}: {jp} → {romaji}");
            if (Regex.IsMatch(romaji, @"[ぁ-ヶ一-鿿]")) L("кана в латинице", $"{where}: {jp} → {romaji}");
            if (kana.Contains('　')) L("широкий пробел", $"{where}: {kana}");
            // кана предложения без пробелов — значит частицы не размечены (は прочитается как ha)
            if (kana.Length > 6 && !kana.Contains(' ') && kana.Any(IsHira) && !kana.All(IsKata)) L("нет разбивки", $"{where}: {kana}");
            // чтение слишком отличается по длине от текста — признак съехавшей строки
            var plain = Kana.ToHiragana(kana.Replace(" ", ""));
            if (jp.All(c => IsHira(c) || "、。！？ 「」…ー".Contains(c)) && Regex.Replace(Kana.ToHiragana(jp), "[、。！？ 「」…]", "") != Regex.Replace(plain, "[、。！？ 「」…]", ""))
                L("текст≠чтение", $"{where}: {jp} / {kana}");
        }

        // 7. кандзи
        foreach (var k in Db.Kanji)
        {
            if (Junk.IsMatch(k.Ru)) L("кандзи: значение", $"{k.Char} — {k.Ru}");
            if (k.Ru.StartsWith("в слове")) L("кандзи: нет значения", $"{k.Char} — {k.Ru}");
            foreach (var e in k.Examples) if (!e.Jp.Contains(k.Char)) L("кандзи: пример без знака", $"{k.Char}: {e.Jp}");
        }
        File.WriteAllText(path, sb.ToString());
    }
}
