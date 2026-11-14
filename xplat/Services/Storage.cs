using System.IO;

namespace Matsubakira.Services;

/// <summary>
/// Управление накопленными данными: кэш озвучки, модели Whisper, VOICEVOX, журналы.
/// Файлы, занятые процессом (загруженная DLL VOICEVOX), удаляются при следующем запуске.
/// </summary>
public static class Storage
{
    public sealed record Item(string Key, string Title, string Desc, string Path, bool IsDir);

    public static Item[] Items => new[]
    {
        new Item("audio", "Кэш озвучки", "Сохранённые фразы — скачаются заново при прослушивании", Tts.CacheDir, true),
        new Item("models", "Модели распознавания (Whisper)", "Нужны для проверки произношения", System.IO.Path.Combine(ProgressStore.Folder, "models"), true),
        new Item("voicevox", "Офлайн-голоса VOICEVOX", "Ядро, словарь и пакеты голосов", Voicevox.Dir, true),
        new Item("llm", "Модель ИИ-собеседника", "Раздел убран в версии 1.3.1 — файлы больше не нужны", System.IO.Path.Combine(ProgressStore.Folder, "llm"), true),
        new Item("logs", "Журналы и временные файлы", "crash.log, warn.log, профиль запуска, старые записи", ProgressStore.Folder, false),
    };

    public static long Size(Item it)
    {
        try
        {
            if (it.Key == "logs")
                return new[] { "crash.log", "warn.log", "startup.jitprofile", "progress.json.bak", "progress.json.before-import" }
                    .Select(f => System.IO.Path.Combine(ProgressStore.Folder, f)).Where(File.Exists).Sum(f => new FileInfo(f).Length)
                    + DirSize(System.IO.Path.Combine(ProgressStore.Folder, "rec"));
            return DirSize(it.Path);
        }
        catch { return 0; }
    }

    static long DirSize(string dir) => Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;

    public static long Total => Items.Sum(Size) + (File.Exists(System.IO.Path.Combine(ProgressStore.Folder, "progress.json")) ? new FileInfo(System.IO.Path.Combine(ProgressStore.Folder, "progress.json")).Length : 0);

    /// <summary>Удаляет данные пункта. Возвращает false, если часть файлов занята и будет удалена при следующем запуске.</summary>
    public static bool Clear(Item it)
    {
        if (it.Key == "models") _ = Asr.UnloadAsync(); // whisper.cpp держит модель в памяти, а не файл — удалять можно сразу (Д3)
        if (it.Key == "voicevox") { Voicevox.Unload(); ProgressStore.P.Flags.Remove("vv-ok"); ProgressStore.Notify(); }
        if (it.Key == "logs")
        {
            foreach (var f in new[] { "crash.log", "warn.log", "startup.jitprofile", "progress.json.bak", "progress.json.before-import" })
                TryDelete(System.IO.Path.Combine(ProgressStore.Folder, f));
            TryDeleteDir(System.IO.Path.Combine(ProgressStore.Folder, "rec"));
            return true;
        }
        bool ok = TryDeleteDir(it.Path);
        if (!ok) ProgressStore.P.Flags.Add("pending-delete:" + it.Key);
        ProgressStore.Notify();
        return ok;
    }

    /// <summary>Вызывается при запуске до загрузки нативных библиотек: доудаляет то, что было занято.</summary>
    public static void RunPendingDeletes()
    {
        foreach (var f in ProgressStore.P.Flags.Where(x => x.StartsWith("pending-delete:")).ToList())
        {
            var it = Items.FirstOrDefault(i => i.Key == f["pending-delete:".Length..]);
            if (it == null || TryDeleteDir(it.Path)) ProgressStore.P.Flags.Remove(f);
        }
    }

    static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

    static bool TryDeleteDir(string d)
    {
        if (!Directory.Exists(d)) return true;
        bool ok = true;
        foreach (var f in Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).ToList())
            try { File.Delete(f); } catch { ok = false; }
        if (ok) try { Directory.Delete(d, true); } catch { ok = false; }
        return ok;
    }

    public static string Format(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{bytes / 1024.0:0} КБ",
        < 1024L * 1024 * 1024 => $"{bytes / 1048576.0:0.#} МБ",
        _ => $"{bytes / 1073741824.0:0.##} ГБ",
    };
}
