using System.IO;
using System.IO.Compression;
using System.Text;

namespace Matsubakira.Services;

public sealed record DictEntry(int Index, bool Common, string Kanji, string Kana, string Ru)
{
    public string Jp => Kanji.Length > 0 ? Kanji.Split('、')[0] : Kana;
    public string Romaji => Services.Kana.ToRomaji(Kana);
    public string Id => "w:" + Jp + "|" + Kana;
}

/// <summary>
/// Большой японско-русский словарь (JMdict/EDRDG, CC BY-SA 4.0): ~69 тыс. статей.
/// Хранится сжатым (gzip) внутри exe, распаковывается в фоне при первом обращении.
/// Поиск — линейный проход по массивам строк: ~5–15 мс на запрос, без индексов в памяти.
/// </summary>
public static class JDict
{
    static string[] _kanji = Array.Empty<string>(), _kana = Array.Empty<string>(), _ru = Array.Empty<string>(), _ruLower = Array.Empty<string>(), _hira = Array.Empty<string>();
    static bool[] _common = Array.Empty<bool>();
    // Lazy: при одновременном первом обращении из разных потоков загрузка запускается ровно один раз
    static readonly Lazy<Task> _load = new(() => Task.Run(Load));
    public static int Count => _kana.Length;
    /// <summary>Размер словаря для подписей: точное число статей, если словарь уже загружен.</summary>
    public static string CountLabel => Loaded && Count > 0 ? $"{Count / 1000 * 1000:N0}+".Replace(' ', ' ') : "69 000+";
    public static bool Loaded => _load.IsValueCreated && _load.Value.IsCompletedSuccessfully;

    public static Task LoadAsync() => _load.Value;

    static void Load()
    {
        using var s = typeof(JDict).Assembly.GetManifestResourceStream("dict.tsv.gz")!;
        using var gz = new GZipStream(s, CompressionMode.Decompress);
        using var r = new StreamReader(gz, Encoding.UTF8);
        var k = new List<string>(230000); var kn = new List<string>(230000); var ru = new List<string>(230000); var c = new List<bool>(230000);
        string? line;
        while ((line = r.ReadLine()) != null)
        {
            var p = line.Split('\t');
            if (p.Length < 4) continue;
            c.Add(p[0] == "1"); k.Add(string.Intern(p[1])); kn.Add(p[2]); ru.Add(p[3]);
        }
        _kanji = k.ToArray(); _kana = kn.ToArray(); _ru = ru.ToArray(); _common = c.ToArray();
        // Копии храним, только если строка действительно меняется: большинство переводов уже в нижнем регистре,
        // большинство чтений — уже хирагана (так словарь на 219 тыс. статей занимает заметно меньше памяти)
        _ruLower = _ru.Select(x => { var l = x.ToLowerInvariant(); return l == x ? x : l; }).ToArray();
        _hira = _kana.Select(x => { var h = Kana.ToHiragana(x); return h == x ? x : h; }).ToArray();
    }

    static DictEntry Entry(int i) => new(i, _common[i], _kanji[i], _kana[i], _ru[i]);

    /// <summary>Поиск. Язык запроса определяется автоматически: кириллица, латиница (ромадзи) или японский.</summary>
    public static List<DictEntry> Search(string query, int limit = 120)
    {
        var res = new List<DictEntry>();
        if (!Loaded) return res;
        query = query.Trim();
        if (query.Length == 0)
        {
            for (int i = 0; i < Math.Min(limit, _kana.Length); i++) res.Add(Entry(i));
            return res;
        }
        // ранги: 0 — точное совпадение, 1 — начинается с, 2 — содержит; внутри — сначала частотные
        var buckets = new List<int>[6];
        for (int b = 0; b < 6; b++) buckets[b] = new List<int>();
        bool cyr = query.Any(ch => ch is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё');
        bool latin = !cyr && query.All(ch => ch < 128);

        if (cyr)
        {
            var q = query.ToLowerInvariant();
            for (int i = 0; i < _ruLower.Length; i++)
            {
                var ru = _ruLower[i];
                int pos = ru.IndexOf(q, StringComparison.Ordinal);
                if (pos < 0) continue;
                int rank = IsWholeGloss(ru, pos, q.Length) ? 0 : (pos == 0 || !char.IsLetter(ru[pos - 1])) ? 1 : 2;
                buckets[rank * 2 + (_common[i] ? 0 : 1)].Add(i);
            }
        }
        else
        {
            string q = latin ? Kana.FromRomaji(query.ToLowerInvariant()) : Kana.ToHiragana(query);
            string qRaw = query;
            for (int i = 0; i < _kana.Length; i++)
            {
                string kana = _hira[i];
                int rank;
                if (kana == q || _kanji[i] == qRaw || _kanji[i].Split('、').Contains(qRaw)) rank = 0;
                else if (kana.StartsWith(q, StringComparison.Ordinal) || (!latin && _kanji[i].StartsWith(qRaw, StringComparison.Ordinal))) rank = 1;
                else if (!latin && q.Length >= 1 && (_kanji[i].Contains(qRaw, StringComparison.Ordinal) || (q.Length >= 2 && kana.Contains(q, StringComparison.Ordinal)))) rank = 2;
                else continue;
                buckets[rank * 2 + (_common[i] ? 0 : 1)].Add(i);
            }
        }
        foreach (var b in buckets)
            foreach (var i in b.OrderBy(i => _kana[i].Length))
            {
                res.Add(Entry(i));
                if (res.Count >= limit) return res;
            }
        return res;
    }

    /// <summary>Частые слова, в записи которых есть этот иероглиф.</summary>
    public static List<DictEntry> WithKanji(char c, int limit = 10)
    {
        var res = new List<DictEntry>();
        if (!Loaded) return res;
        for (int i = 0; i < _kanji.Length && res.Count < limit; i++)
            if (_common[i] && _kanji[i].IndexOf(c) >= 0 && _kanji[i].Length <= 4) res.Add(Entry(i));
        return res;
    }

    // Совпадение целой глоссы: между разделителями «, ; ( » и концом строки
    static bool IsWholeGloss(string ru, int pos, int len)
    {
        bool startOk = pos == 0 || (ru[pos - 1] == ' ' && pos >= 2 && ru[pos - 2] is ',' or ';' or ')') || ru[pos - 1] is ',' or ';' or '(';
        int end = pos + len;
        bool endOk = end == ru.Length || ru[end] is ',' or ';' or ')';
        return startOk && endOk;
    }

    static Dictionary<string, int>? _forms;

    /// <summary>Точный поиск по форме слова (кандзи или кана); частотные статьи в приоритете.</summary>
    public static DictEntry? Lookup(string form)
    {
        if (!Loaded || string.IsNullOrEmpty(form)) return null;
        var forms = _forms;
        if (forms == null)
        {
            var d = new Dictionary<string, int>(140000);
            for (int i = 0; i < _kana.Length; i++)   // массивы уже отсортированы: частотные первыми
            {
                foreach (var k in _kanji[i].Split('、', StringSplitOptions.RemoveEmptyEntries)) d.TryAdd(k, i);
                d.TryAdd(_kana[i], i);
            }
            _forms = forms = d;
        }
        return forms.TryGetValue(form, out var idx) ? Entry(idx) : null;
    }

    /// <summary>Самое длинное слово словаря, начинающееся с позиции pos (для разбора произвольного текста).</summary>
    public static (int len, DictEntry? entry) Longest(string text, int pos, int max = 8)
    {
        for (int len = Math.Min(max, text.Length - pos); len >= 1; len--)
            if (Lookup(text.Substring(pos, len)) is { } e) return (len, e);
        return (1, null);
    }

    /// <summary>Добавить слово из словаря в личные карточки (появится в повторениях).</summary>
    public static bool AddToStudy(DictEntry e)
    {
        var list = ProgressStore.P.CustomWords;
        if (list.Any(c => c[0] == e.Jp && c[1] == e.Kana)) return false;
        var firstSense = e.Ru.Split(';')[0].Trim();
        list.Add(new[] { e.Jp, e.Kana, firstSense });
        ProgressStore.Notify();
        return true;
    }

    public static bool InStudy(DictEntry e) => ProgressStore.P.CustomWords.Any(c => c[0] == e.Jp && c[1] == e.Kana);
}
