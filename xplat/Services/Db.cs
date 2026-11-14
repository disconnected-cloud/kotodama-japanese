using System.IO;
using System.Text.Json;

namespace Matsubakira.Services;

public sealed record Example(string Jp, string Kana, string Ru)
{
    public string Romaji => Db.RomajiFor(Jp, Kana);
}

public sealed record Word(string Id, string Jp, string Kana, string Ru, string Topic, string Level = "N5", string ExJp = "", string ExRu = "")
{
    public string Romaji { get; } = Services.Kana.ToRomaji(Kana);
    public bool HasKanji => Jp != Kana;
}

public sealed record KanjiInfo(string Id, string Char, string On, string Kun, string Ru, int Strokes, Example[] Examples, string Level);

public sealed record GrammarPoint(string Pattern, string Explain, Example[] Examples);
public sealed record GrammarQuiz(string Question, string Answer, string[] Options, string Ru)
{
    /// <summary>Предложение с ответом на месте пропуска (пропуск бывает «＿», «＿＿» или «＿＿＿» — раньше ответ задваивался).</summary>
    public string Filled => System.Text.RegularExpressions.Regex.Replace(Question, "＿+", Answer.Replace("$", "$$"));
}
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
public sealed record QuestChoice(string Jp, string Ru, bool Ok, string Why)
{
    public string Romaji => Db.RomajiFor(Jp, Db.KanaFor(Jp) ?? Jp);
}
public sealed record QuestScene(string? Place, string? Narr, string Who, bool Male, string Jp, string Kana, string Ru, QuestChoice[] Choices)
{
    public string Romaji => Db.RomajiFor(Jp, Kana);
}
/// <summary>«Приключение»: история из сцен, в каждой — реплика собеседника и варианты ответа (один верный).</summary>
public sealed record Quest(string Id, string Title, string Level, string Emoji, string Desc, QuestScene[] Scenes);

/// <summary>Id — постоянный ключ прогресса (позиция в course.json), Num — номер на «Пути» (порядок показа).</summary>
public sealed record CourseUnit(int Id, string Title, string Level, string[] Theory, Example[] Examples, CourseStep[] Steps)
{
    public int Num { get; set; }
}

/// <summary>Учебные материалы из встроенных JSON. Грузятся один раз, лениво и потокобезопасно.</summary>
public static class Db
{
    // PublicationOnly: сбой загрузки не запоминается навсегда — следующее обращение загрузит заново (А9)
    static readonly Lazy<Word[]> _words = new(LoadWords, LazyThreadSafetyMode.PublicationOnly);
    static readonly Lazy<KanjiInfo[]> _kanji = new(LoadKanji, LazyThreadSafetyMode.PublicationOnly);
    static readonly Lazy<GrammarLesson[]> _grammar = new(LoadGrammar, LazyThreadSafetyMode.PublicationOnly);
    static readonly Lazy<SpeechLesson[]> _speech = new(LoadSpeech, LazyThreadSafetyMode.PublicationOnly);
    static readonly Lazy<Dialog[]> _dialogs = new(LoadDialogs, LazyThreadSafetyMode.PublicationOnly);
    static readonly Lazy<CourseUnit[]> _course = new(LoadCourse, LazyThreadSafetyMode.PublicationOnly);
    static readonly Lazy<Dictionary<string, string[]>> _strokes = new(LoadStrokes, LazyThreadSafetyMode.PublicationOnly);
    static readonly Lazy<Dictionary<string, string>> _romaji = new(LoadRomaji, LazyThreadSafetyMode.PublicationOnly);

    sealed record Merged(List<string[]> Custom, int Count, Word[] Words);
    static volatile Merged? _merged;
    /// <summary>Базовые слова курса + слова, добавленные из большого словаря (пересобирается при изменении).</summary>
    public static Word[] Words
    {
        get
        {
            // свои слова только добавляются; импорт прогресса меняет сам список — тогда собираем заново
            var custom = ProgressStore.P.CustomWords;
            var m = _merged;
            if (m != null && ReferenceEquals(m.Custom, custom) && m.Count == custom.Count) return m.Words;
            var baseW = _words.Value;
            var set = new HashSet<string>(baseW.Select(w => w.Id));
            var extra = custom.ToArray().Where(c => c is { Length: >= 3 }).Select(c => new Word("w:" + c[0] + "|" + c[1], c[0], c[1], c[2], "Мои слова", "Мои")).Where(w => set.Add(w.Id));
            var words = baseW.Concat(extra).ToArray();
            _merged = new Merged(custom, custom.Count, words);
            return words;
        }
    }
    public static KanjiInfo[] Kanji => _kanji.Value;

    static (Word[] words, HashSet<string> ids)? _reviewable;
    /// <summary>ID карточек, которые можно повторить: слова (с добавленными из словаря) и кандзи.</summary>
    public static HashSet<string> ReviewableIds
    {
        get
        {
            var words = Words;
            if (_reviewable is { } r && ReferenceEquals(r.words, words)) return r.ids;
            var ids = words.Select(w => w.Id).Concat(Kanji.Select(k => k.Id)).ToHashSet();
            _reviewable = (words, ids);
            return ids;
        }
    }

    static readonly Lazy<Dictionary<string, string>> _aliases = new(() =>
    {
        try
        {
            using var doc = Open("id_aliases.json");
            return doc.RootElement.EnumerateObject().Where(p => !p.Name.StartsWith("//") && p.Value.ValueKind == JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString()!);
        }
        catch (Exception e) { CrashReport.Warn("Алиасы ID", e); return new(); }
    });
    /// <summary>Старый ID карточки → новый (после исправления написания слова или фразы).</summary>
    public static IReadOnlyDictionary<string, string> IdAliases => _aliases.Value;
    public static GrammarLesson[] Grammar => _grammar.Value;
    public static SpeechLesson[] SpeechLessons => _speech.Value;
    public static Dialog[] Dialogs => _dialogs.Value;
    public static CourseUnit[] Course => _course.Value;
    static readonly Lazy<Quest[]> _quests = new(LoadQuests, LazyThreadSafetyMode.PublicationOnly);
    public static Quest[] Quests => _quests.Value;

    public static IEnumerable<Word> WordsOf(string level) => Words.Where(w => w.Level == level);
    public static IEnumerable<KanjiInfo> KanjiOf(string level) => Kanji.Where(k => k.Level == level);

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
        try { _ = Words; _ = Kanji; _ = Grammar; _ = SpeechLessons; _ = Dialogs; _ = Course; _ = _romaji.Value; } catch (Exception e) { CrashReport.Warn("Db.Preload", e); }
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
        foreach (var (file, level) in new[] { ("vocab.json", "N5"), ("vocab_n4.json", "N4"), ("vocab_plus.json", "1000"), ("vocab_n3.json", "N3"), ("vocab_n2.json", "N2"), ("vocab_n1.json", "N1"), ("vocab_live.json", "Живой") })
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
        foreach (var (file, level) in new[] { ("kanji.json", "N5"), ("kanji_n4.json", "N4"), ("kanji_n3.json", "N3"), ("kanji_n2.json", "N2"), ("kanji_n1.json", "N1") })
        {
            using var doc = Open(file);
            list.AddRange(Each(doc.RootElement, file, k =>
                new KanjiInfo("k:" + S(k, 0), S(k, 0), S(k, 1), S(k, 2), S(k, 3), k[4].GetInt32(), Examples(k[5]), level)));
        }
        return list.ToArray();
    }

    /// <summary>
    /// ID урока — явное поле "id" в данных: на нём держится прогресс (GrammarDone, «speech:N», «dialog:N», юниты пути),
    /// поэтому вставка урока в середину файла или перестановка файлов больше не переносит прогресс на чужие уроки (А7).
    /// Без поля — следующий номер по порядку (старое поведение), тест следит, чтобы поле было везде.
    /// </summary>
    static int Id(JsonElement e, ref int counter)
    {
        int id = Int(e, "id");
        counter = id > 0 ? Math.Max(counter, id) : counter + 1;
        return id > 0 ? id : counter;
    }

    /// <summary>Разбор записей по одной: испорченная запись пропускается и пишется в журнал, а не выключает весь раздел (А9).</summary>
    static IEnumerable<T> Each<T>(JsonElement arr, string file, Func<JsonElement, T> parse)
    {
        int i = 0;
        foreach (var e in arr.EnumerateArray())
        {
            T? v = default; bool ok = true;
            try { v = parse(e); }
            catch (Exception ex) { ok = false; CrashReport.Warn($"Данные {file}: запись {i} пропущена", ex); }
            i++;
            if (ok) yield return v!;
        }
    }

    static GrammarLesson[] LoadGrammar()
    {
        int id = 0;
        var list = new List<GrammarLesson>();
        foreach (var file in new[] { "grammar.json", "grammar_n4.json", "grammar_n3.json", "grammar_n3b.json", "grammar_more.json", "grammar_n2.json", "grammar_n1.json", "grammar_extra.json" })
        {
            using var doc = Open(file);
            list.AddRange(Each(doc.RootElement, file, l => new GrammarLesson(
                Id(l, ref id), Str(l, "title"), Str(l, "level"), Str(l, "intro"),
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
        return Each(doc.RootElement, "speech.json", l =>
        {
            int lid = Id(l, ref id);
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
        return Each(doc.RootElement, "dialogs.json", d => new Dialog(
            Id(d, ref id), Str(d, "title"), Str(d, "icon"), Str(d, "level"), Str(d, "desc"),
            d.GetProperty("roles").EnumerateArray().Select(r => r.GetString() ?? "").ToArray(),
            d.GetProperty("voices").EnumerateArray().Select(v => v.GetString() == "m").ToArray(),
            d.GetProperty("lines").EnumerateArray().Select(l => new DialogLine(l[0].GetInt32(), S(l, 1), S(l, 2), S(l, 3))).ToArray()
        )).ToArray();
    }

    /// <summary>
    /// Юниты в порядке «Пути». Новые юниты дописываются в конец course.json (Id = позиция в файле, на нём держится прогресс),
    /// а поле "after": N ставит юнит на «Пути» сразу после юнита N — так уроки встают на свой уровень, не сбивая прогресс.
    /// </summary>
    static CourseUnit[] LoadCourse()
    {
        using var doc = Open("course.json");
        int id = 0;
        var units = new List<(CourseUnit u, int after)>();
        foreach (var u in doc.RootElement.EnumerateArray())
            units.Add((new CourseUnit(
                Id(u, ref id), Str(u, "title"), Str(u, "level"),
                u.GetProperty("theory").EnumerateArray().Select(t => t.GetString() ?? "").ToArray(),
                Examples(u.GetProperty("examples")),
                u.GetProperty("steps").EnumerateArray().Select(s => new CourseStep(
                    Str(s, "t"), Str(s, "title") is { Length: > 0 } t ? t : null, Str(s, "script"),
                    s.TryGetProperty("rows", out var r) ? r.EnumerateArray().Select(x => x.GetInt32()).ToArray() : null,
                    Str(s, "group") is { Length: > 0 } g ? g : null, Int(s, "id"), Str(s, "topic"), Str(s, "level"), Int(s, "count"))).ToArray()),
                Int(u, "after")));
        var order = units.Where(x => x.after == 0).Select(x => x.u).ToList();
        // вставляем по цепочке: юнит после 14, затем следующий «after: 14» встаёт уже за ним
        var last = new Dictionary<int, int>();
        foreach (var (u, after) in units.Where(x => x.after > 0))
        {
            int anchor = last.TryGetValue(after, out var a) ? a : after;
            int at = order.FindIndex(x => x.Id == anchor);
            order.Insert(at < 0 ? order.Count : at + 1, u);
            last[after] = u.Id;
        }
        for (int i = 0; i < order.Count; i++) order[i].Num = i + 1;
        return order.ToArray();
    }

    static Quest[] LoadQuests()
    {
        using var doc = Open("quests.json");
        string? Opt(JsonElement o, string p) => Str(o, p) is { Length: > 0 } v ? v : null;
        return doc.RootElement.EnumerateArray().Select(q => new Quest(
            Str(q, "id"), Str(q, "title"), Str(q, "level"), Str(q, "emoji"), Str(q, "desc"),
            q.GetProperty("scenes").EnumerateArray().Select(sc => new QuestScene(
                Opt(sc, "place"), Opt(sc, "narr"), Str(sc, "who"), sc.TryGetProperty("male", out var m) && m.GetBoolean(),
                Str(sc, "jp"), Str(sc, "kana"), Str(sc, "ru"),
                sc.GetProperty("choices").EnumerateArray().Select(c => new QuestChoice(S(c, 0), S(c, 1), c[2].GetBoolean(), S(c, 3))).ToArray())).ToArray()
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
