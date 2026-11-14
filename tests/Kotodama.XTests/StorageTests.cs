using System.IO;
using System.Runtime.CompilerServices;
using Matsubakira.Services;

[assembly: CollectionBehavior(DisableTestParallelization = true)] // тесты меняют общий прогресс ProgressStore.P

namespace Kotodama.Tests;

static class TestData
{
    /// <summary>Тесты никогда не трогают настоящую папку данных пользователя (пункт Е4).</summary>
    [ModuleInitializer]
    internal static void Init()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kotodama-tests-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("KOTODAMA_DATA_DIR", dir);
    }
}

/// <summary>Загрузка, импорт и восстановление прогресса: битый файл, занятый файл, null в коллекциях.</summary>
[Collection("progress-file")]
public class StorageTests : IDisposable
{
    static string Main => Path.Combine(ProgressStore.Folder, "progress.json");
    readonly Progress _saved = ProgressStore.P;

    public StorageTests()
    {
        Assert.Contains("kotodama-tests-", ProgressStore.Folder);
        ProgressStore.ReadOnly = false;
        foreach (var f in Directory.GetFiles(ProgressStore.Folder, "progress*")) File.Delete(f);
        if (Directory.Exists(ProgressStore.BackupDir)) Directory.Delete(ProgressStore.BackupDir, true);
    }

    public void Dispose()
    {
        ProgressStore.ReadOnly = false;
        typeof(ProgressStore).GetProperty("P")!.SetValue(null, _saved);
    }

    [Fact]
    public void CorruptFileIsKeptAndBackupUsed()
    {
        Directory.CreateDirectory(ProgressStore.BackupDir);
        File.WriteAllText(Path.Combine(ProgressStore.BackupDir, "progress-2026-01-01.json"), "{\"Xp\":321}");
        File.WriteAllText(Main, "{\"Xp\":12, обрыв");
        ProgressStore.Load();
        Assert.Equal(321, ProgressStore.P.Xp);
        Assert.Single(Directory.GetFiles(ProgressStore.Folder, "progress.corrupt-*.json"));
        Assert.False(ProgressStore.ReadOnly);
    }

    [Fact]
    public void LockedFileSwitchesToReadOnlyInsteadOfOverwriting()
    {
        if (!OperatingSystem.IsWindows()) return; // эксклюзивная блокировка файла есть только в Windows
        File.WriteAllText(Main, "{\"Xp\":77}");
        using (new FileStream(Main, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            ProgressStore.Load();
        Assert.True(ProgressStore.ReadOnly);
        Assert.NotNull(ProgressStore.LoadProblem);
        ProgressStore.Save(); // в режиме только чтения ничего не пишется
        Assert.Equal("{\"Xp\":77}", File.ReadAllText(Main));
    }

    [Fact]
    public void ImportWithNullCollectionsDoesNotCrash()
    {
        var names = typeof(Progress).GetProperties().Where(p => !p.PropertyType.IsValueType && p.PropertyType != typeof(string)).Select(p => $"\"{p.Name}\":null");
        var file = Path.Combine(ProgressStore.Folder, "import.json");
        File.WriteAllText(file, "{" + string.Join(",", names) + ",\"CustomWords\":[null,[\"a\"],[\"猫\",\"ねこ\",\"кошка\"]],\"Xp\":-5}");
        Assert.True(ProgressStore.Import(file));
        var p = ProgressStore.P;
        foreach (var prop in typeof(Progress).GetProperties().Where(x => !x.PropertyType.IsValueType))
            Assert.NotNull(prop.GetValue(p));
        Assert.Single(p.CustomWords);
        Assert.Equal(0, p.Xp);
        Assert.Contains("mig-v3", p.Flags); // миграции применяются и к импорту
        Assert.Contains(Db.Words, w => w.Id == "w:猫|ねこ" || w.Jp == "猫");
    }

    [Fact]
    public void ImportReplacesWordsCacheEvenWithSameCount()
    {
        var a = Path.Combine(ProgressStore.Folder, "a.json");
        var b = Path.Combine(ProgressStore.Folder, "b.json");
        File.WriteAllText(a, "{\"CustomWords\":[[\"一石二鳥テスト\",\"いっせきにちょうてすと\",\"тест А\"]]}");
        File.WriteAllText(b, "{\"CustomWords\":[[\"別テスト\",\"べつてすと\",\"тест Б\"]]}");
        Assert.True(ProgressStore.Import(a));
        Assert.Contains(Db.Words, w => w.Jp == "一石二鳥テスト");
        Assert.True(ProgressStore.Import(b));
        Assert.DoesNotContain(Db.Words, w => w.Jp == "一石二鳥テスト");
        Assert.Contains(Db.Words, w => w.Jp == "別テスト");
    }

    [Fact]
    public void OldDayFlagCleanupToleratesShortFlags()
    {
        var p = new Progress();
        p.Flags.UnionWith(new[] { "ch:", "ch:x", "plan:2020-01-01", "ch:2099-01-01", "study:2020-01-01", "mig-v3" });
        ProgressStore.DropOldDayFlags(p, DateTime.Today, "ch:", "plan:");
        Assert.Equal(new[] { "ch:", "ch:2099-01-01", "ch:x", "mig-v3", "study:2020-01-01" }, p.Flags.OrderBy(f => f, StringComparer.Ordinal));
    }
}

[Collection("progress-file")]
public class FullResetTests
{
    [Fact]
    public void FullResetRemovesBackupsAndCopies()
    {
        Directory.CreateDirectory(ProgressStore.BackupDir);
        File.WriteAllText(Path.Combine(ProgressStore.BackupDir, "progress-2026-01-01.json"), "{}");
        foreach (var n in new[] { "progress.json.before-restore", "progress.json.before-import", "progress.json.bak", "progress.corrupt-1.json" })
            File.WriteAllText(Path.Combine(ProgressStore.Folder, n), "{}");
        ProgressStore.DeleteAllCopies();
        Assert.False(Directory.Exists(ProgressStore.BackupDir));
        Assert.Empty(Directory.GetFiles(ProgressStore.Folder, "progress*").Where(f => !f.EndsWith("progress.json")));
    }
}

/// <summary>Стабильность ID уроков (А7): случайная перестановка уроков ломает этот тест, а не прогресс пользователей.</summary>
public class LessonIdTests
{
    [Fact]
    public void LessonIdsMatchSnapshot()
    {
        var actual = Db.Grammar.Select(g => $"g{g.Id}|{g.Title}")
            .Concat(Db.SpeechLessons.Select(l => $"s{l.Id}|{l.Title}"))
            .Concat(Db.Dialogs.Select(d => $"d{d.Id}|{d.Title}"))
            .Concat(Db.Course.OrderBy(u => u.Id).Select(u => $"c{u.Id}|{u.Title}")).ToHashSet();
        var snap = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "lesson_ids.txt")).Where(l => l.Length > 0);
        foreach (var line in snap) Assert.Contains(line, actual); // новые уроки можно добавлять, старые ID не меняются
    }

    [Fact]
    public void EveryLessonHasExplicitUniqueId()
    {
        Assert.Equal(Db.Grammar.Length, Db.Grammar.Select(g => g.Id).Distinct().Count());
        Assert.Equal(Db.SpeechLessons.Length, Db.SpeechLessons.Select(g => g.Id).Distinct().Count());
        Assert.Equal(Db.Dialogs.Length, Db.Dialogs.Select(g => g.Id).Distinct().Count());
        Assert.Equal(Db.Course.Length, Db.Course.Select(g => g.Id).Distinct().Count());
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "Data"))) dir = Path.GetDirectoryName(dir);
        foreach (var f in new[] { "grammar.json", "grammar_n4.json", "grammar_n3.json", "grammar_n3b.json", "grammar_more.json", "grammar_n2.json", "grammar_n1.json", "grammar_extra.json", "speech.json", "dialogs.json", "course.json" })
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!, "Data", f)));
            Assert.All(doc.RootElement.EnumerateArray(), e => Assert.True(e.TryGetProperty("id", out _), f));
        }
    }
}

public class DataRobustnessTests
{
    [Fact]
    public void MigrationsRunOnceAndRecordSchema()
    {
        var p = new Progress { TtsEngine = "auto", Petals = true };
        ProgressStore.Prepare(p);
        Assert.Equal(ProgressStore.SchemaVersion, p.Schema);
        Assert.False(p.Petals);
        p.Petals = true;               // пользователь включил лепестки снова
        ProgressStore.Prepare(p);      // повторная загрузка не сбрасывает
        Assert.True(p.Petals);
    }

    [Fact]
    public void ReadingChallengeCountersDoesNotChangeState()
    {
        var saved = ProgressStore.P;
        try
        {
            var y = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");
            var p = new Progress { DayDate = y };
            p.DayCounts["review"] = 5;
            typeof(ProgressStore).GetProperty("P")!.SetValue(null, p);
            _ = Challenges.Today().Select(c => c.Done).ToList();
            Assert.Equal(y, p.DayDate);  // чтение не архивирует вчерашний день
            Assert.True(Challenges.RollDay(p));
            Assert.Equal(5, p.DayLog[y]["review"]);
            Assert.Empty(p.DayCounts);
        }
        finally { typeof(ProgressStore).GetProperty("P")!.SetValue(null, saved); }
    }

    [Fact]
    public void ExportSerializesCurrentProgress() => Assert.Contains("\"Xp\"", System.Text.Encoding.UTF8.GetString(ProgressStore.Serialize()));
}
