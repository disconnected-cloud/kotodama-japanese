using System.IO;
using System.Text.Json;

namespace Matsubakira.Services;

public sealed record Example(string Jp, string Kana, string Ru)
{
    public string Romaji => Db.RomajiFor(Jp, Kana);
}

public sealed record Word(string Id, string Jp, string Kana, string Ru, string Topic, string Level = "N5", string ExJp = "", string ExRu = "")
{
    public string ExRomaji => ExJp.Length > 0 ? Db.RomajiFor(ExJp, "") : "";
    public string Romaji { get; } = Services.Kana.ToRomaji(Kana);
    public bool HasKanji => Jp != Kana;
}

public sealed record KanjiInfo(string Id, string Char, string On, string Kun, string Ru, int Strokes, Example[] Examples, string Level);

public sealed record GrammarPoint(string Pattern, string Explain, Example[] Examples);
public sealed record GrammarQuiz(string Question, string Answer, string[] Options, string Ru);
public sealed record GrammarLesson(int Id, string Title, string Level, string Intro, GrammarPoint[] Points, GrammarQuiz[] Quiz);

public sealed record Phrase(string Id, string Jp, string Kana, string Ru, string? Tip)
{
    public string Romaji { get; } = Services.Kana.ToRomaji(Kana);
    public string KanaPlain { get; } = Kana.Replace(" ", "");
}
public sealed record SpeechLesson(int Id, string Title, string Icon, string Desc, Phrase[] Items);

public sealed record DialogLine(int Speaker, string Jp, string Kana, string Ru)
{
    public string Romaji { get; } = Services.Kana.ToRomaji(Kana);
    public string KanaPlain { get; } = Kana.Replace(" ", "");
    public string Id => "d:" + Jp;
}
public sealed record Dialog(int Id, string Title, string Icon, string Level, string Desc, string[] Roles, bool[] Male, DialogLine[] Lines);

public sealed record CourseStep(string T, string? Title, string? Script, int[]? Rows, string? Group, int Id, string? Topic, string? Level, int Count);
public sealed record CourseUnit(int Id, string Title, string Level, string[] Theory, Example[] Examples, CourseStep[] Steps);

/// <summary>Учебные материалы из встроенных JSON. Грузятся один раз, лениво и потокобезопасно.</summary>
public static class Db
{
    static readonly Lazy<Word[]> _words = new(LoadWords);
    static readonly Lazy<KanjiInfo[]> _kanji = new(LoadKanji);
    static readonly Lazy<GrammarLesson[]> _grammar = new(LoadGrammar);
    static readonly Lazy<SpeechLesson[]> _speech = new(LoadSpeech);
    static readonly Lazy<Dialog[]> _dialogs = new(LoadDialogs);
    static readonly Lazy<CourseUnit[]> _course = new(LoadCourse);
    static readonly Lazy<Dictionary<string, string[]>> _strokes = new(LoadStrokes);
    static readonly Lazy<Dictionary<string, string>> _romaji = new(LoadRomaji);

    static Word[]? _merged; static int _mergedCustom = -1;
    /// <summary>Базовые слова курса + слова, добавленные из большого словаря (пересобирается при изменении).</summary>
    public static Word[] Words
    {
        get
        {
            var custom = ProgressStore.P.CustomWords;
            if (_merged != null && _mergedCustom == custom.Count) return _merged;
            var baseW = _words.Value;
            var set = new HashSet<string>(baseW.Select(w => w.Id));
            var extra = custom.Where(c => c.Length >= 3).Select(c => new Word("w:" + c[0] + "|" + c[1], c[0], c[1], c[2], "Мои слова", "Мои")).Where(w => set.Add(w.Id));
            _merged = baseW.Concat(extra).ToArray();
            _mergedCustom = custom.Count;
            return _merged;
        }
    }
    public static KanjiInfo[] Kanji => _kanji.Value;
    public static GrammarLesson[] Grammar => _grammar.Value;
    public static SpeechLesson[] SpeechLessons => _speech.Value;
    public static Dialog[] Dialogs => _dialogs.Value;
    public static CourseUnit[] Course => _course.Value;

    public static IEnumerable<Word> WordsOf(string level) => Words.Where(w => w.Level == level);
    public static IEnumerable<KanjiInfo> KanjiOf(string level) => Kanji.Where(k => k.Level == level);
    public static IEnumerable<string> TopicsOf(string level) => WordsOf(level).Select(w => w.Topic).Distinct();

    public static string[]? Strokes(string ch) => _strokes.Value.TryGetValue(ch, out var s) ? s : null;

    /// <summary>Латиница для предложения: заранее размеченные границы слов и частицы (MeCab) → Хэпбёрн.</summary>
    /// <summary>Правильное чтение фразы или слова из материалов приложения (кана с пробелами между словами) — для проверки озвучки.</summary>
    public static string? KanaFor(string jp) => _kanaMap.Value.TryGetValue(jp.Trim(), out var k) ? k : null;
    static readonly Lazy<Dictionary<string, string>> _kanaMap = new(() =>
    {
        var m = new Dictionary<string, string>();
        void Add(string jp, string kana) { if (jp.Length > 0 && kana.Length > 0) m.TryAdd(jp.Trim(), kana); }
        foreach (var l in SpeechLessons) foreach (var it in l.Items) Add(it.Jp, it.Kana);
        foreach (var d in Dialogs) foreach (var ln in d.Lines) Add(ln.Jp, ln.Kana);
        foreach (var w in Words) Add(w.Jp, w.Kana);
        foreach (var k in Kanji) foreach (var e in k.Examples) Add(e.Jp, e.Kana);
        foreach (var (jp, seg) in _romaji.Value) Add(jp, seg);
        return m;
    });

    public static string RomajiFor(string jp, string kana) =>
        _romaji.Value.TryGetValue(jp, out var seg) ? Services.Kana.ToRomaji(seg) : Services.Kana.ToRomaji(kana.Length > 0 ? kana : jp);

    public static void Preload()
    {
        try { _ = Words; _ = Kanji; _ = Grammar; _ = SpeechLessons; _ = Dialogs; _ = Course; _ = _romaji.Value; } catch { }
    }

    static JsonDocument Open(string name)
    {
        using var s = typeof(Db).Assembly.GetManifestResourceStream(name)
                      ?? throw new FileNotFoundException("Нет ресурса " + name);
        return JsonDocument.Parse(s, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
    }

    static string S(JsonElement e, int i) => i < e.GetArrayLength() ? e[i].GetString() ?? "" : "";
    static string Str(JsonElement o, string p) => o.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static int Int(JsonElement o, string p) => o.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    static Example[] Examples(JsonElement arr) =>
        arr.EnumerateArray().Select(x => new Example(S(x, 0), S(x, 1), S(x, 2))).ToArray();

    static Word[] LoadWords()
    {
        var list = new List<Word>(1024);
        var seen = new HashSet<string>();
        foreach (var (file, level) in new[] { ("vocab.json", "N5"), ("vocab_n4.json", "N4"), ("vocab_plus.json", "1000"), ("vocab_n3.json", "N3") })
        {
            using var doc = Open(file);
            foreach (var topic in doc.RootElement.EnumerateObject())
                foreach (var w in topic.Value.EnumerateArray())
                {
                    string jp = S(w, 0), kana = S(w, 1);
                    if (kana.Length == 0) kana = jp;
                    var id = "w:" + jp + "|" + kana;
                    if (!seen.Add(id)) continue; // омографы (つく, ただ…) — одна карточка
                    list.Add(new Word(id, jp, kana, S(w, 2), topic.Name, level, S(w, 3), S(w, 4)));
                }
        }
        return list.ToArray();
    }

    static KanjiInfo[] LoadKanji()
    {
        var list = new List<KanjiInfo>();
        foreach (var (file, level) in new[] { ("kanji.json", "N5"), ("kanji_n4.json", "N4"), ("kanji_n3.json", "N3") })
        {
            using var doc = Open(file);
            list.AddRange(doc.RootElement.EnumerateArray().Select(k =>
                new KanjiInfo("k:" + S(k, 0), S(k, 0), S(k, 1), S(k, 2), S(k, 3), k[4].GetInt32(), Examples(k[5]), level)));
        }
        return list.ToArray();
    }

    static GrammarLesson[] LoadGrammar()
    {
        int id = 0;
        var list = new List<GrammarLesson>();
        foreach (var file in new[] { "grammar.json", "grammar_n4.json", "grammar_n3.json", "grammar_n3b.json", "grammar_more.json" })
        {
            using var doc = Open(file);
            list.AddRange(doc.RootElement.EnumerateArray().Select(l => new GrammarLesson(
                ++id, Str(l, "title"), Str(l, "level"), Str(l, "intro"),
                l.GetProperty("points").EnumerateArray().Select(p => new GrammarPoint(Str(p, "p"), Str(p, "e"), Examples(p.GetProperty("ex")))).ToArray(),
                l.GetProperty("quiz").EnumerateArray().Select(q => new GrammarQuiz(S(q, 0), S(q, 1), S(q, 2).Split('|'), S(q, 3))).ToArray()
            )).ToList());
        }
        return list.ToArray();
    }

    static SpeechLesson[] LoadSpeech()
    {
        using var doc = Open("speech.json");
        int id = 0;
        return doc.RootElement.EnumerateArray().Select(l =>
        {
            int lid = ++id;
            var items = l.GetProperty("items").EnumerateArray().Select(p =>
            {
                string jp = S(p, 0), kana = S(p, 1);
                if (kana.Length == 0) kana = jp;
                var tip = S(p, 3);
                return new Phrase("s:" + jp, jp, kana, S(p, 2), tip.Length == 0 ? null : tip);
            }).ToArray();
            return new SpeechLesson(lid, Str(l, "title"), Str(l, "icon"), Str(l, "desc"), items);
        }).ToArray();
    }

    static Dialog[] LoadDialogs()
    {
        using var doc = Open("dialogs.json");
        int id = 0;
        return doc.RootElement.EnumerateArray().Select(d => new Dialog(
            ++id, Str(d, "title"), Str(d, "icon"), Str(d, "level"), Str(d, "desc"),
            d.GetProperty("roles").EnumerateArray().Select(r => r.GetString() ?? "").ToArray(),
            d.GetProperty("voices").EnumerateArray().Select(v => v.GetString() == "m").ToArray(),
            d.GetProperty("lines").EnumerateArray().Select(l => new DialogLine(l[0].GetInt32(), S(l, 1), S(l, 2), S(l, 3))).ToArray()
        )).ToArray();
    }

    static CourseUnit[] LoadCourse()
    {
        using var doc = Open("course.json");
        int id = 0;
        return doc.RootElement.EnumerateArray().Select(u => new CourseUnit(
            ++id, Str(u, "title"), Str(u, "level"),
            u.GetProperty("theory").EnumerateArray().Select(t => t.GetString() ?? "").ToArray(),
            Examples(u.GetProperty("examples")),
            u.GetProperty("steps").EnumerateArray().Select(s => new CourseStep(
                Str(s, "t"), Str(s, "title") is { Length: > 0 } t ? t : null, Str(s, "script"),
                s.TryGetProperty("rows", out var r) ? r.EnumerateArray().Select(x => x.GetInt32()).ToArray() : null,
                Str(s, "group") is { Length: > 0 } g ? g : null, Int(s, "id"), Str(s, "topic"), Str(s, "level"), Int(s, "count"))).ToArray()
        )).ToArray();
    }

    static Dictionary<string, string[]> LoadStrokes()
    {
        using var doc = Open("strokes.json");
        var d = new Dictionary<string, string[]>();
        foreach (var p in doc.RootElement.EnumerateObject())
            d[p.Name] = p.Value.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        return d;
    }

    static Dictionary<string, string> LoadRomaji()
    {
        using var doc = Open("romaji.json");
        var d = new Dictionary<string, string>();
        foreach (var p in doc.RootElement.EnumerateObject()) d[p.Name] = p.Value.GetString() ?? "";
        return d;
    }
}
