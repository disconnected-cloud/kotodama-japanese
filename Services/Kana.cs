using System.Text;

namespace Matsubakira.Services;

public sealed record KanaCell(string Hira, string Kata, string Romaji, string Group)
{
    public string Get(bool katakana) => katakana ? Kata : Hira;
}

/// <summary>Таблицы каны (годзюон, дакутэн, ёон) и конвертер кана → ромадзи (Хэпбёрн).</summary>
public static class Kana
{
    public const string Basic = "basic", Dakuten = "dakuten", Combo = "combo";

    // Строки по 5 колонок, "_" — пустая ячейка
    static readonly (string kana, string romaji)[] BasicRows =
    {
        ("あいうえお", "a i u e o"),
        ("かきくけこ", "ka ki ku ke ko"),
        ("さしすせそ", "sa shi su se so"),
        ("たちつてと", "ta chi tsu te to"),
        ("なにぬねの", "na ni nu ne no"),
        ("はひふへほ", "ha hi fu he ho"),
        ("まみむめも", "ma mi mu me mo"),
        ("や_ゆ_よ", "ya _ yu _ yo"),
        ("らりるれろ", "ra ri ru re ro"),
        ("わ___を", "wa _ _ _ wo"),
        ("ん____", "n _ _ _ _"),
    };
    static readonly (string kana, string romaji)[] DakutenRows =
    {
        ("がぎぐげご", "ga gi gu ge go"),
        ("ざじずぜぞ", "za ji zu ze zo"),
        ("だぢづでど", "da ji zu de do"),
        ("ばびぶべぼ", "ba bi bu be bo"),
        ("ぱぴぷぺぽ", "pa pi pu pe po"),
    };
    static readonly string[] ComboHeads = { "き", "し", "ち", "に", "ひ", "み", "り", "ぎ", "じ", "び", "ぴ" };
    static readonly string[] ComboHeadRomaji = { "ky", "sh", "ch", "ny", "hy", "my", "ry", "gy", "j", "by", "py" };

    public static readonly IReadOnlyList<KanaCell?[]> BasicGrid = BuildGrid(BasicRows, Basic);
    public static readonly IReadOnlyList<KanaCell?[]> DakutenGrid = BuildGrid(DakutenRows, Dakuten);
    public static readonly IReadOnlyList<KanaCell?[]> ComboGrid = BuildCombo();

    public static readonly IReadOnlyList<KanaCell> All =
        BasicGrid.Concat(DakutenGrid).Concat(ComboGrid).SelectMany(r => r).OfType<KanaCell>().ToArray();

    static readonly Dictionary<string, string> Map = BuildMap();

    static List<KanaCell?[]> BuildGrid((string kana, string romaji)[] rows, string group)
    {
        var list = new List<KanaCell?[]>();
        foreach (var (kana, romaji) in rows)
        {
            var r = romaji.Split(' ');
            var row = new KanaCell?[kana.Length];
            for (int i = 0; i < kana.Length; i++)
                row[i] = kana[i] == '_' ? null : new KanaCell(kana[i].ToString(), ToKatakana(kana[i].ToString()), r[i], group);
            list.Add(row);
        }
        return list;
    }

    static List<KanaCell?[]> BuildCombo()
    {
        var list = new List<KanaCell?[]>();
        string[] smalls = { "ゃ", "ゅ", "ょ" }, vow = { "a", "u", "o" };
        for (int h = 0; h < ComboHeads.Length; h++)
        {
            var row = new KanaCell?[3];
            for (int s = 0; s < 3; s++)
            {
                var hira = ComboHeads[h] + smalls[s];
                row[s] = new KanaCell(hira, ToKatakana(hira), ComboHeadRomaji[h] + vow[s], Combo);
            }
            list.Add(row);
        }
        return list;
    }

    static Dictionary<string, string> BuildMap()
    {
        var m = new Dictionary<string, string>(256);
        foreach (var c in All) m[c.Hira] = c.Romaji;
        m["ゃ"] = "ya"; m["ゅ"] = "yu"; m["ょ"] = "yo";
        m["ぁ"] = "a"; m["ぃ"] = "i"; m["ぅ"] = "u"; m["ぇ"] = "e"; m["ぉ"] = "o";
        m["ゔ"] = "vu";
        // Сочетания для заимствований (катакана)
        m["ふぁ"] = "fa"; m["ふぃ"] = "fi"; m["ふぇ"] = "fe"; m["ふぉ"] = "fo";
        m["てぃ"] = "ti"; m["でぃ"] = "di"; m["しぇ"] = "she"; m["ちぇ"] = "che"; m["じぇ"] = "je";
        m["うぃ"] = "wi"; m["うぇ"] = "we"; m["うぉ"] = "wo"; m["とぅ"] = "tu";
        return m;
    }

    static readonly Lazy<(string r, string k)[]> Rev = new(() =>
    {
        var d = new Dictionary<string, string>();
        foreach (var kv in Map) if (!kv.Key.StartsWith("ゃ") && kv.Key.Length > 0 && !"ゃゅょぁぃぅぇぉ".Contains(kv.Key[0])) d.TryAdd(kv.Value, kv.Key);
        foreach (var (r, k) in new[] { ("si", "し"), ("ti", "ち"), ("tu", "つ"), ("hu", "ふ"), ("zi", "じ"), ("di", "ぢ"), ("du", "づ"),
                     ("sya", "しゃ"), ("syu", "しゅ"), ("syo", "しょ"), ("tya", "ちゃ"), ("tyu", "ちゅ"), ("tyo", "ちょ"),
                     ("jya", "じゃ"), ("jyu", "じゅ"), ("jyo", "じょ"), ("zya", "じゃ"), ("zyu", "じゅ"), ("zyo", "じょ"), ("je", "じぇ") })
            d.TryAdd(r, k);
        return d.Select(kv => (kv.Key, kv.Value)).OrderByDescending(p => p.Key.Length).ToArray();
    });

    /// <summary>Ромадзи → хирагана (для поиска латиницей). Поддерживает Хэпбёрн и кунрэй, удвоения и «n».</summary>
    public static string FromRomaji(string s)
    {
        var sb = new StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (!char.IsLetter(c)) { if (c == '-') sb.Append('ー'); i++; continue; }
            if (i + 1 < s.Length && s[i + 1] == c && "bcdfghjkmpqrstvwxz".Contains(c)) { sb.Append('っ'); i++; continue; }
            if (c == 't' && i + 2 < s.Length && s[i + 1] == 'c' && s[i + 2] == 'h') { sb.Append('っ'); i++; continue; }
            if (c == 'n' && (i + 1 == s.Length || !"aiueoy".Contains(s[i + 1])))
            {
                sb.Append('ん');
                bool nn = i + 1 < s.Length && s[i + 1] == 'n';
                // «nn» перед гласной (konnichiwa) — это ん + «n» следующего слога
                i += nn && !(i + 2 < s.Length && "aiueoy".Contains(s[i + 2])) ? 2 : 1;
                continue;
            }
            bool hit = false;
            foreach (var (r, k) in Rev.Value)
                if (r.Length <= s.Length - i && string.CompareOrdinal(s, i, r, 0, r.Length) == 0)
                { sb.Append(k); i += r.Length; hit = true; break; }
            if (!hit) { sb.Append(c); i++; }
        }
        return sb.ToString();
    }

    public static string ToKatakana(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(c >= 'ぁ' && c <= 'ゖ' ? (char)(c + 0x60) : c);
        return sb.ToString();
    }

    public static string ToHiragana(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(c >= 'ァ' && c <= 'ヶ' ? (char)(c - 0x60) : c);
        return sb.ToString();
    }

    public static bool IsKana(char c) => c is >= '぀' and <= 'ヿ';

    /// <summary>
    /// Кана → ромадзи. Пробелы разделяют слова: отдельно стоящие частицы は/を/へ читаются как wa/o/e.
    /// </summary>
    public static string ToRomaji(string kana)
    {
        if (kana.IndexOf(' ') < 0 && kana.IndexOf('　') < 0) return WordToRomaji(kana);
        var parts = kana.Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder(kana.Length * 2);
        foreach (var p in parts)
        {
            if (sb.Length > 0) sb.Append(' ');
            // Частица может стоять вплотную к знаку препинания: «は？»
            var core = p.TrimEnd('。', '、', '！', '？', '!', '?', '…');
            sb.Append(core switch { "は" => "wa", "を" => "o", "へ" => "e", _ => WordToRomaji(core) });
            if (core.Length < p.Length) sb.Append(p[core.Length..].Replace('。', '.').Replace('、', ',').Replace('！', '!').Replace('？', '?'));
        }
        return sb.ToString();
    }

    static string WordToRomaji(string kana)
    {
        // Приветствия, где は — исторически частица
        if (kana is "こんにちは") return "konnichiwa";
        if (kana is "こんばんは") return "konbanwa";
        return Convert(kana);
    }

    static string Convert(string kana)
    {
        var s = ToHiragana(kana);
        var sb = new StringBuilder(s.Length * 2);
        bool doubleNext = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == 'っ') { doubleNext = true; continue; }
            if (c == 'ー') { if (sb.Length > 0 && "aiueo".Contains(sb[^1])) sb.Append(sb[^1]); continue; }
            if (c is '、' or '。' or ' ' or '　') { sb.Append(c == '。' ? ". " : c == '、' ? ", " : " "); continue; }

            string r;
            if (i + 1 < s.Length && Map.TryGetValue(s.Substring(i, 2), out var two)) { r = two; i++; }
            else if (Map.TryGetValue(c.ToString(), out var one)) r = one;
            else r = c.ToString();

            if (doubleNext && r.Length > 0 && char.IsLetter(r[0]))
                sb.Append(r.StartsWith("ch") ? 't' : r[0]);
            doubleNext = false;

            // ん перед гласной/y: kan'i
            if (r == "n" && i + 1 < s.Length && Map.TryGetValue(s[i + 1].ToString(), out var nx) && "aiueoy".Contains(nx[0]))
                r = "n'";
            sb.Append(r);
        }
        return sb.ToString().Trim();
    }
}
