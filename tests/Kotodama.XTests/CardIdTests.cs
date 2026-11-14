using System.IO;
using System.IO.Compression;
using Matsubakira.Services;

namespace Kotodama.Tests;

/// <summary>
/// А8: ID карточки строится из текста (w:слово|чтение, s:фраза, d:реплика, k:кандзи). Исправил опечатку — прогресс
/// ученика «осиротел». Тест сравнивает ID с прошлым релизом (card_ids.txt.gz) и требует алиас для каждого исчезнувшего.
/// После выпуска релиза снимок обновляется: KOTODAMA_UPDATE_IDS=1 dotnet test.
/// </summary>
public class CardIdTests
{
    static IEnumerable<string> CurrentIds() =>
        Db.Words.Where(w => w.Topic != "Мои слова").Select(w => w.Id)
            .Concat(Db.Kanji.Select(k => k.Id))
            .Concat(Db.SpeechLessons.SelectMany(l => l.Items).Select(i => i.Id))
            .Concat(Db.Dialogs.SelectMany(d => d.Lines).Select(l => l.Id))
            .Distinct().OrderBy(x => x, StringComparer.Ordinal);

    static string SnapshotPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "tests", "Kotodama.XTests"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? "", "tests", "Kotodama.XTests", "card_ids.txt.gz");
    }

    [Fact]
    public void EveryRemovedIdHasAlias()
    {
        var path = SnapshotPath();
        if (Environment.GetEnvironmentVariable("KOTODAMA_UPDATE_IDS") == "1")
        {
            using var w = new StreamWriter(new GZipStream(File.Create(path), CompressionLevel.SmallestSize));
            foreach (var id in CurrentIds()) w.WriteLine(id);
            return;
        }
        Assert.True(File.Exists(path), "нет снимка card_ids.txt.gz");
        using var r = new StreamReader(new GZipStream(File.OpenRead(path), CompressionMode.Decompress));
        var now = CurrentIds().ToHashSet();
        var lost = new List<string>();
        while (r.ReadLine() is { } line)
            if (line.Length > 0 && !now.Contains(line) && !(Db.IdAliases.TryGetValue(line, out var to) && now.Contains(to))) lost.Add(line);
        Assert.True(lost.Count == 0, "Исчезли ID без алиаса в Data/id_aliases.json:\n" + string.Join("\n", lost.Take(30)));
    }

    [Fact]
    public void AliasesMoveProgress()
    {
        var p = new Progress();
        p.Cards["w:旧|きゅう"] = new SrsCard { Reps = 5 };
        p.SpeechBest["w:旧|きゅう"] = 80;
        p.Notes["w:旧|きゅう"] = "заметка";
        ProgressStore.ApplyIdAliases(p, new Dictionary<string, string> { ["w:旧|きゅう"] = "w:新|しん" });
        Assert.False(p.Cards.ContainsKey("w:旧|きゅう"));
        Assert.Equal(5, p.Cards["w:新|しん"].Reps);
        Assert.Equal(80, p.SpeechBest["w:新|しん"]);
        Assert.Equal("заметка", p.Notes["w:新|しん"]);
    }

    [Fact]
    public void OrphanCardsAreNotCountedAsDue()
    {
        var saved = ProgressStore.P;
        try
        {
            var p = new Progress();
            p.Cards["w:存在しない|そんざいしない"] = new SrsCard { Due = DateTime.Now.AddDays(-1) };
            p.Cards[Db.Words[0].Id] = new SrsCard { Due = DateTime.Now.AddDays(-1) };
            typeof(ProgressStore).GetProperty("P")!.SetValue(null, p);
            Assert.Equal(1, ProgressStore.DueCount());
        }
        finally { typeof(ProgressStore).GetProperty("P")!.SetValue(null, saved); }
    }
}
