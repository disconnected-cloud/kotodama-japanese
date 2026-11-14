using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Matsubakira.Services;

/// <summary>
/// Чтение распознанного текста каной — чтобы сравнивать произношение, а не написание.
/// Whisper может записать правильно сказанное «おねがいします» как «お願いします», «さんじ» как «3時»,
/// а «こんにちは» как «こんにちわ». Здесь всё сводится к одному звучанию:
/// • кандзи из целевой фразы читаются так же, как в ней (выравнивание фраза ↔ кана);
/// • остальные кандзи — по словарю JMdict, с учётом окуриганы (食べ → た + べ);
/// • цифры превращаются в японские числа; は/わ, を/お, づ/ず и долгие おう/おお считаются одинаковыми.
/// </summary>
public static class Reading
{
    public static bool IsKanji(char c) => c is >= '一' and <= '鿿' or >= '㐀' and <= '䶿' or '々' or '〆' or 'ヶ';

    /// <summary>Выравнивание «кандзи-фраза ↔ кана»: каждой группе кандзи ставится в соответствие её чтение.</summary>
    public static Dictionary<string, string> Align(string jp, string kana)
    {
        var map = new Dictionary<string, string>();
        var j = Kana.ToHiragana(Speech.Normalize(jp));
        var k = Kana.ToHiragana(Speech.Normalize(kana));
        if (j.Length == 0 || k.Length == 0 || j == k) return map;
        var runs = new List<string>();
        var pat = new StringBuilder("^");
        for (int i = 0; i < j.Length;)
        {
            if (IsKanji(j[i]))
            {
                int e = i; while (e < j.Length && IsKanji(j[e])) e++;
                runs.Add(j[i..e]); pat.Append("(.+?)"); i = e;
            }
            else { pat.Append(Regex.Escape(j[i].ToString())); i++; }
        }
        if (runs.Count == 0) return map;
        pat.Append('$');
        try
        {
            var m = Regex.Match(k, pat.ToString(), RegexOptions.None, TimeSpan.FromMilliseconds(80));
            if (m.Success)
                for (int r = 0; r < runs.Count; r++) map.TryAdd(runs[r], m.Groups[r + 1].Value);
        }
        catch (RegexMatchTimeoutException) { }
        return map;
    }

    static readonly string[] Ends = { "", "る", "う", "く", "す", "つ", "む", "ぶ", "ぬ", "ぐ", "い" };

    /// <summary>Чтение произвольного японского текста хираганой (map — чтения кандзи из целевой фразы).</summary>
    public static string Of(string text, Dictionary<string, string>? map = null, string? targetHira = null)
    {
        var t = Kana.ToHiragana(Speech.Normalize(DigitsToKanji(text)));
        var sb = new StringBuilder(t.Length * 2);
        for (int i = 0; i < t.Length;)
        {
            if (!IsKanji(t[i])) { sb.Append(t[i]); i++; continue; }
            int e = i; while (e < t.Length && IsKanji(t[e])) e++;
            // Слова, где кана стоит между кандзи (真っ直ぐ, 取り扱い): ищем самую длинную словарную форму целиком
            if (map == null || !map.ContainsKey(t[i..e]))
            {
                bool hit = false;
                for (int len = Math.Min(8, t.Length - i); len > e - i && !hit; len--)
                {
                    var span = t.Substring(i, len);
                    if (!IsKanji(span[^1]) && !span.Skip(e - i).Any(IsKanji)) continue; // только если дальше снова кандзи
                    if (Read(span) is { } r) { sb.Append(r); i += len; hit = true; }
                }
                if (hit) continue;
            }
            sb.Append(RunReading(t[i..e], t, e, map, targetHira));
            i = e;
        }
        return sb.ToString();
    }

    static string RunReading(string run, string text, int after, Dictionary<string, string>? map, string? targetHira)
    {
        var sb = new StringBuilder();
        int p = 0;
        while (p < run.Length)
        {
            int rest = run.Length - p;
            bool found = false;
            if (map != null)
                for (int len = rest; len >= 1 && !found; len--)
                    if (map.TryGetValue(run.Substring(p, len), out var r)) { sb.Append(r); p += len; found = true; }
            if (found) continue;

            for (int len = rest; len >= 1 && !found; len--)
            {
                var sub = run.Substring(p, len);
                if (p + len == run.Length)
                {
                    // Окуригана: 食べ(ます) → словарная форма 食べる → чтение основы «た»
                    int okEnd = after; while (okEnd < text.Length && okEnd - after < 3 && text[okEnd] is >= 'ぁ' and <= 'ゖ') okEnd++;
                    var oku = text[after..okEnd];
                    // jj = 0 — окуригана изменилась при спряжении: 切って → 切る, 話して → 話す, 書いた → 書く
                    for (int jj = oku.Length; jj >= 0 && !found && oku.Length > 0; jj--)
                        foreach (var end in Ends)
                        {
                            if (jj == 0 && end.Length == 0) continue;
                            var suffix = oku[..jj] + end;
                            if (Read(sub + suffix) is { } k && k.EndsWith(suffix, StringComparison.Ordinal) && k.Length > suffix.Length)
                            { sb.Append(k[..^suffix.Length]); p += len; found = true; break; }
                        }
                }
                if (!found && len == 1 && Guess(sub[0], targetHira) is { } g) { sb.Append(g); p += 1; found = true; }
                if (!found && Read(sub) is { } whole) { sb.Append(whole); p += len; found = true; }
            }
            if (!found) { sb.Append(run[p]); p++; }
        }
        return sb.ToString();
    }

    static Dictionary<char, KanjiInfo>? _kanjiByChar;

    /// <summary>
    /// Одиночный кандзи без словарного слова вокруг (Whisper записал «真っ直ですね» вместо «まっすぐですね»):
    /// из его чтений выбираем то, что действительно звучит в целевой фразе.
    /// </summary>
    static string? Guess(char c, string? targetHira)
    {
        if (string.IsNullOrEmpty(targetHira)) return null;
        var cands = new List<string>();
        if (Read(c.ToString()) is { } r) cands.Add(r);
        try
        {
            _kanjiByChar ??= Db.Kanji.GroupBy(k => k.Char[0]).ToDictionary(g => g.Key, g => g.First());
            if (_kanjiByChar.TryGetValue(c, out var k))
                foreach (var x in (k.On + "、" + k.Kun).Split('、', ' ', ',', ';'))
                {
                    var y = Kana.ToHiragana(x.Split('.')[0].Trim('-', ' '));
                    if (y.Length > 0) cands.Add(y);
                }
        }
        catch { }
        return cands.Where(x => targetHira.Contains(x, StringComparison.Ordinal)).OrderByDescending(x => x.Length).FirstOrDefault();
    }

    static string? Read(string form)
    {
        var e = JDict.Lookup(form);
        if (e == null || !e.Kanji.Split('、').Contains(form)) return null;
        return Kana.ToHiragana(e.Kana);
    }

    /// <summary>
    /// Латиница по словам: «こんにちは、今日は何をしますか» → «konnichiwa, kyou wa nani o shimasu ka».
    /// Текст делится на слова по словарю, частицы は/を/へ читаются как wa/o/e.
    /// </summary>
    public static string RomajiWords(string text)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        var t = DigitsToKanji(text);
        const string Lead = "はをがにでともへの", Tail = "かねよな";
        bool Punct(char c) => c is '。' or '、' or '！' or '？' or '!' or '?' or ',' or '.' or '…' or ' ' or '　';
        void Flush() { if (words.Count > 0) { sb.Append(string.Join(" ", words)); words.Clear(); } }
        string Rom(string token) => token switch
        {
            "は" => "wa", "を" => "o", "へ" => "e", "こんにちは" => "konnichiwa", "こんばんは" => "konbanwa",
            _ => Kana.ToRomaji(Kana.ToHiragana(Of(token))),
        };
        for (int i = 0; i < t.Length;)
        {
            char c = t[i];
            if (Punct(c))
            {
                Flush();
                sb.Append(c switch { '。' => ". ", '、' => ", ", '！' or '!' => "! ", '？' or '?' => "? ", _ => " " });
                i++; continue;
            }
            if (Kana.IsKana(c) && c >= 'ァ')
            {
                // катакана — заимствованное слово целиком (コーヒー → koohii)
                int e = i;
                while (e < t.Length && Kana.IsKana(t[e]) && t[e] >= 'ァ') e++;
                words.Add(Kana.ToRomaji(Kana.ToHiragana(t[i..e])));
                i = e; continue;
            }
            if (IsKanji(c))
            {
                // слово с кандзи или катаканой: самое длинное по словарю, но без «приклеенной» частицы (今日は → 今日 + は)
                var (len, _) = JDict.Longest(t, i);
                if (len > 1 && Lead.Contains(t[i + len - 1]) && i + len < t.Length && !Punct(t[i + len]) && JDict.Lookup(t.Substring(i, len - 1)) != null) len--;
                // окончание глагола/прилагательного пишется слитно: 食べます → tabemasu, 高かった → takakatta
                int k = i + len;
                if (IsKanji(t[k - 1]) && k < t.Length && Kana.IsKana(t[k]) && t[k] < 'ァ' && !Lead.Contains(t[k]) && !IsCopula(t, k))
                {
                    int e = k;
                    while (e < t.Length && Kana.IsKana(t[e]) && t[e] < 'ァ') e++;
                    if (e - k > 1 && Tail.Contains(t[e - 1]) && (e >= t.Length || Punct(t[e]))) e--;
                    len = e - i;
                }
                words.Add(Rom(t.Substring(i, len)));
                i += len; continue;
            }
            if (Kana.IsKana(c))
            {
                int e = i;
                while (e < t.Length && Kana.IsKana(t[e]) && t[e] < 'ァ' && !Punct(t[e])) e++;
                var run = t[i..e];
                if (run is "こんにちは" or "こんばんは" or "ありがとう" or "おはよう") { words.Add(Rom(run)); i = e; continue; }
                // частица в начале (после слова) и в конце (か, ね, よ) — отдельными словами
                if (IsCopula(t, i)) { int ce = CopulaEnd(t, i); words.Add(Kana.ToRomaji(t[i..ce])); i = ce; continue; }
                if (run.Length > 1 && Lead.Contains(run[0]) && i > 0 && !Punct(t[i - 1]) && !Kana.IsKana(t[i - 1]) is var afterWord && (afterWord || t[i - 1] >= 'ァ'))
                { words.Add(Rom(run[..1])); run = run[1..]; }
                string? tail = null;
                if (run.Length > 2 && Tail.Contains(run[^1]) && (e >= t.Length || Punct(t[e]))) { tail = run[^1..]; run = run[..^1]; }
                if (run.Length > 0) words.Add(run.Length == 1 && Lead.Contains(run[0]) ? Rom(run) : Kana.ToRomaji(run));
                if (tail != null) words.Add(Kana.ToRomaji(tail));
                i = e; continue;
            }
            Flush();
            sb.Append(c); i++;
        }
        Flush();
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), " +", " ").Trim();
    }

    static readonly string[] Copulas = { "でしょう", "でした", "ですか", "です", "だった", "だろう", "では", "じゃない", "じゃ" };
    static bool IsCopula(string t, int i) => Copulas.Any(c => string.CompareOrdinal(t, i, c, 0, c.Length) == 0);
    static int CopulaEnd(string t, int i) { var c = Copulas.First(x => string.CompareOrdinal(t, i, x, 0, x.Length) == 0); return i + (c == "ですか" ? 2 : c.Length); }

    /// <summary>Звучание для сравнения: латиница без различий, которых не слышно (は/わ, おう/おお, づ/ず…).</summary>
    public static string Phon(string hira)
    {
        var sb = new StringBuilder(hira.Length);
        foreach (var c in Kana.ToHiragana(hira))
            sb.Append(c switch { 'は' => 'わ', 'を' => 'お', 'へ' => 'え', 'づ' => 'ず', 'ぢ' => 'じ', _ => c });
        var r = Kana.ToRomaji(sb.ToString()).ToLowerInvariant();
        var clean = new StringBuilder(r.Length);
        foreach (var c in r) if (c is >= 'a' and <= 'z') clean.Append(c);
        return clean.ToString().Replace("ou", "oo").Replace("ei", "ee");
    }

    /// <summary>
    /// Схожесть звучания (латиница после Phon). Как Левенштейн, но лишняя/пропущенная долгота гласной
    /// и удвоение согласного (っ) стоят половину: Whisper сам плохо их различает, а ритм проверяется отдельно.
    /// lenient — ошибки, которые не мешают пониманию (звонкий/глухой: か/が, た/だ; гласные え/い, お/う; ん перед м; лишняя «й» в きょ/こ),
    /// тоже стоят половину: так фраза засчитывается, даже если сказана с типичным акцентом.
    /// </summary>
    public static double SoundSimilarity(string a, string b, bool lenient = false)
    {
        if (a.Length == 0 && b.Length == 0) return 1;
        if (a.Length == 0 || b.Length == 0) return 0;
        // повтор предыдущей буквы = долгота или っ (но не «nn»: пропущенное ん — полноценная ошибка)
        double Indel(string s, int i) => i > 0 && ((s[i] == s[i - 1] && s[i] != 'n') || (lenient && s[i] is 'y' or 'h' && s[i - 1] is not ('a' or 'i' or 'u' or 'e' or 'o'))) ? 0.5 : 1;
        double Sub(char x, char y) => x == y ? 0 : lenient && Close(x, y) ? 0.5 : 1;
        var prev = new double[b.Length + 1];
        var cur = new double[b.Length + 1];
        prev[0] = 0;
        for (int j = 1; j <= b.Length; j++) prev[j] = prev[j - 1] + Indel(b, j - 1);
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = prev[0] + Indel(a, i - 1);
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + Indel(b, j - 1), prev[j] + Indel(a, i - 1)), prev[j - 1] + Sub(a[i - 1], b[j - 1]));
            (prev, cur) = (cur, prev);
        }
        return Math.Max(0, 1.0 - prev[b.Length] / Math.Max(a.Length, b.Length));
    }

    static readonly string[] ClosePairs = { "kg", "sz", "td", "hb", "hp", "bp", "fh", "fb", "jz", "cj", "rd", "nm", "ei", "ou" };
    static bool Close(char x, char y) => ClosePairs.Any(p => p[0] == x && p[1] == y || p[0] == y && p[1] == x);

    /// <summary>Подсказки по типичным ошибкам: っ, долгие гласные, ん, звонкие/глухие.</summary>
    public static List<string> Hints(string targetHira, string heardHira)
    {
        var tips = new List<string>();
        string t = Kana.ToHiragana(targetHira), h = Kana.ToHiragana(heardHira);
        if (t.Length == 0 || h.Length == 0 || t == h) return tips;
        string pt = Phon(t), ph = Phon(h);
        if (pt == ph || Speech.Similarity(pt, ph) < 0.45) return tips;

        int tq = t.Count(c => c == 'っ'), hq = h.Count(c => c == 'っ');
        if (tq > hq) tips.Add("Не хватает паузы っ: перед удвоенным согласным задержись на долю секунды (きって — «ки-т-тэ»).");
        else if (hq > tq) tips.Add("Лишняя пауза っ: в этой фразе согласный не удваивается.");

        int tl = LongVowels(pt), hl = LongVowels(ph);
        if (tl > hl) tips.Add("Тяни долгие гласные вдвое дольше: おばあさん (бабушка) ≠ おばさん (тётя).");
        else if (hl > tl) tips.Add("Есть лишняя долгота: некоторые гласные здесь короткие.");

        if (t.Count(c => c == 'ん') > h.Count(c => c == 'ん')) tips.Add("Не проглатывай ん: это отдельный слог, его нужно дотянуть.");

        string st = StripVoicing(t), sh = StripVoicing(h);
        if (st != t || sh != h)
            if (Speech.Similarity(st, sh) - Speech.Similarity(t, h) >= 0.08)
                tips.Add("Путаются звонкие и глухие: か/が, た/だ, さ/ざ, は/ば/ぱ. Следи за голосом на согласном.");
        return tips;
    }

    static int LongVowels(string romaji) => Regex.Matches(romaji, "aa|ii|uu|ee|oo").Count;

    static string StripVoicing(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d) if (c is not ('゙' or '゚')) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>«3時» → «三時», «25» → «二十五»: дальше читается как обычные кандзи.</summary>
    public static string DigitsToKanji(string s)
    {
        if (!s.Any(c => c is >= '0' and <= '9' or >= '０' and <= '９')) return s;
        var sb = new StringBuilder(s.Length + 4);
        for (int i = 0; i < s.Length;)
        {
            if (Digit(s[i]) < 0) { sb.Append(s[i]); i++; continue; }
            long n = 0; int e = i;
            while (e < s.Length && (Digit(s[e]) >= 0 || (s[e] is ',' or '，' && e + 1 < s.Length && Digit(s[e + 1]) >= 0)))
            {
                if (Digit(s[e]) >= 0 && n < 1_000_000_000_000L) n = n * 10 + Digit(s[e]);
                e++;
            }
            sb.Append(NumberKanji(n));
            i = e;
        }
        return sb.ToString();
    }

    static int Digit(char c) => c is >= '0' and <= '9' ? c - '0' : c is >= '０' and <= '９' ? c - '０' : -1;

    public static string NumberKanji(long n)
    {
        if (n == 0) return "零";
        const string D = "〇一二三四五六七八九";
        string Small(int x)
        {
            var sb = new StringBuilder();
            foreach (var (v, u) in new[] { (1000, "千"), (100, "百"), (10, "十") })
            {
                int q = x / v; x %= v;
                if (q == 0) continue;
                if (q > 1) sb.Append(D[q]);
                sb.Append(u);
            }
            if (x > 0) sb.Append(D[x]);
            return sb.ToString();
        }
        var res = new StringBuilder();
        foreach (var (v, u) in new[] { (1_0000_0000_0000L, "兆"), (1_0000_0000L, "億"), (1_0000L, "万"), (1L, "") })
        {
            int q = (int)(n / v); n %= v;
            if (q > 0) res.Append(Small(q)).Append(u);
        }
        return res.ToString();
    }
}
