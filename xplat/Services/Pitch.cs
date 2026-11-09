using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Matsubakira.Services;

/// <summary>
/// Питч-акцент из словаря (Kanjium, CC BY-SA 4.0; токийская норма).
/// Номер ударения → рисунок тона по морам: 0 — ровный (низ-верх-верх…, частица высокая),
/// 1 — падение после первой моры, n — подъём и падение после n-й моры.
/// </summary>
public static class Pitch
{
    static readonly Lazy<(Dictionary<string, int> byPair, Dictionary<string, int> byKana)> _data = new(Load);

    static (Dictionary<string, int>, Dictionary<string, int>) Load()
    {
        var pair = new Dictionary<string, int>(80000); var kana = new Dictionary<string, int>(60000);
        using var s = typeof(Pitch).Assembly.GetManifestResourceStream("accents.tsv.gz")!;
        using var r = new StreamReader(new GZipStream(s, CompressionMode.Decompress), Encoding.UTF8);
        string? line;
        while ((line = r.ReadLine()) != null)
        {
            var p = line.Split('\t');
            if (p.Length < 3 || !int.TryParse(p[2], out var a)) continue;
            pair.TryAdd(p[0] + "|" + Kana.ToHiragana(p[1]), a);
            kana.TryAdd(Kana.ToHiragana(p[1]), a);
        }
        return (pair, kana);
    }

    /// <summary>Номер ударения слова или null, если его нет в словаре.</summary>
    public static int? Accent(string word, string reading)
    {
        var (pair, kana) = _data.Value;
        var hr = Kana.ToHiragana(reading);
        if (pair.TryGetValue(word + "|" + hr, out var a)) return a;
        if (word == reading && kana.TryGetValue(hr, out a)) return a;
        return null;
    }

    /// <summary>Деление на моры: маленькие ゃゅょぁぃぅぇぉ присоединяются к предыдущему слогу; っ, ん, ー — отдельные моры.</summary>
    public static List<string> Moras(string kana)
    {
        var res = new List<string>();
        foreach (var c in kana)
        {
            if ("ゃゅょぁぃぅぇぉャュョァィゥェォ".Contains(c) && res.Count > 0) res[^1] += c;
            else if (Kana.IsKana(c)) res.Add(c.ToString());
        }
        return res;
    }

    /// <summary>Моры с высотой (1 — высоко, 0 — низко) и отметкой места падения тона, плюс условная частица.</summary>
    public static List<(string kana, double pitch, bool accentEnd)>? Pattern(string word, string reading)
    {
        if (Accent(word, reading) is not int a) return null;
        var m = Moras(reading);
        if (m.Count == 0) return null;
        var res = new List<(string, double, bool)>();
        for (int i = 0; i < m.Count; i++)
        {
            int pos = i + 1;
            bool high = a == 0 ? pos > 1 : a == 1 ? pos == 1 : pos > 1 && pos <= a;
            if (m.Count == 1 && a == 0) high = false;     // однословный хэйбан: частица поднимается
            res.Add((m[i], high ? 1 : 0, pos == a));
        }
        // Частица после слова показывает, падает ли тон после последней моры (одака / хэйбан)
        bool particleHigh = a == 0;
        res.Add(("(が)", particleHigh ? 1 : 0, false));
        return res;
    }

    public static string Describe(int accent, int moras) => accent switch
    {
        0 => "ровный тон (хэйбан): ниже → выше и без падения",
        1 => "атамадака: высоко на первом слоге, затем вниз",
        _ when accent == moras => "одака: тон падает сразу после слова",
        _ => $"накадака: тон падает после {accent}-го слога",
    };
}

public sealed record Mnemonic(string[][] Parts, string Story);

/// <summary>Мнемоники кандзи: составные части (KRADFILE) с русскими названиями и истории-подсказки.</summary>
public static class Mnemonics
{
    static readonly Lazy<Dictionary<string, Mnemonic>> _d = new(() =>
    {
        using var s = typeof(Mnemonics).Assembly.GetManifestResourceStream("mnemonics.json")!;
        using var doc = JsonDocument.Parse(s);
        var d = new Dictionary<string, Mnemonic>();
        foreach (var p in doc.RootElement.EnumerateObject())
            d[p.Name] = new Mnemonic(
                p.Value.GetProperty("parts").EnumerateArray().Select(x => new[] { x[0].GetString() ?? "", x[1].GetString() ?? "" }).ToArray(),
                p.Value.GetProperty("story").GetString() ?? "");
        return d;
    });

    public static Mnemonic? For(string kanji) => _d.Value.TryGetValue(kanji, out var m) ? m : null;

    /// <summary>Подсказка: готовая история или сборка «часть + часть → значение».</summary>
    public static string Hint(string kanji, string meaning)
    {
        var m = For(kanji);
        if (m == null) return "";
        if (m.Story.Length > 0) return m.Story;
        var named = m.Parts.Where(p => p[1].Length > 0).Take(3).Select(p => $"{p[1]} {p[0]}").ToList();
        return named.Count >= 2 ? $"{string.Join(" + ", named)} → {meaning.Split(';')[0]}" : "";
    }
}
