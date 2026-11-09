using System.IO;

namespace Matsubakira.Services;

/// <summary>
/// Отчёт об ошибке: открывает в браузере форму нового обращения на GitHub с заполненными
/// версией, системой и последней записью журнала. Пользователь сам решает, отправлять ли.
/// </summary>
public static class CrashReport
{
    static string LogPath => Path.Combine(ProgressStore.Folder, "crash.log");

    public static string? LastEntry()
    {
        try
        {
            if (!File.Exists(LogPath)) return null;
            var t = File.ReadAllText(LogPath);
            int i = t.LastIndexOf("\n[", StringComparison.Ordinal);
            return (i >= 0 ? t[(i + 1)..] : t).Trim();
        }
        catch { return null; }
    }

    /// <summary>Были ли новые ошибки после прошлого запуска.</summary>
    public static bool HasNew()
    {
        try
        {
            if (!File.Exists(LogPath)) return false;
            var w = File.GetLastWriteTime(LogPath);
            bool fresh = w > ProgressStore.P.LastCrashSeen;
            return fresh;
        }
        catch { return false; }
    }

    public static void MarkSeen() { ProgressStore.P.LastCrashSeen = DateTime.Now; ProgressStore.Notify(); }

    public static void Open(string? userNote = null)
    {
        var entry = LastEntry() ?? "(журнал пуст)";
        if (entry.Length > 3500) entry = entry[..3500] + "\n…";
        var body = $"""
            **Что произошло:** {userNote ?? "опиши, что делал перед ошибкой"}

            **Версия:** {Updater.Current.ToString(3)} · **Windows:** {Environment.OSVersion.Version} · {(File.Exists(Path.Combine(AppContext.BaseDirectory, "wpfgfx_cor3.dll")) ? "переносная" : "обычная")} сборка

            <details><summary>Журнал ошибки</summary>

            ```
            {entry}
            ```
            </details>
            """;
        var url = $"https://github.com/{Updater.Repo}/issues/new?labels=bug&title={Uri.EscapeDataString("Ошибка: " + FirstLine(entry))}&body={Uri.EscapeDataString(body)}";
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        MarkSeen();
    }

    static string FirstLine(string e)
    {
        var l = e.Split('\n')[0];
        int i = l.IndexOf("] ", StringComparison.Ordinal);
        l = i >= 0 ? l[(i + 2)..] : l;
        return l.Length > 90 ? l[..90] : l;
    }
}
