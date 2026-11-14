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
