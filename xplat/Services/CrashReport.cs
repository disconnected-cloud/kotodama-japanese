using System.IO;

namespace Matsubakira.Services;

/// <summary>
/// Отчёт об ошибке: открывает в браузере форму нового обращения на GitHub с заполненными
/// версией, системой и последней записью журнала. Пользователь сам решает, отправлять ли.
/// </summary>
public static class CrashReport
{
    static string LogPath => Path.Combine(ProgressStore.Folder, "crash.log");

    /// <summary>Записать ошибку в журнал (папка данных, crash.log).</summary>
    public static void Log(Exception? ex)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now}] {ex}\n\n"); } catch { }
    }

    static string WarnPath => Path.Combine(ProgressStore.Folder, "warn.log");
    static readonly object WarnLock = new();

    /// <summary>
    /// Некритичный сбой (нет звука, модель не загрузилась, сеть недоступна): пишется в warn.log,
    /// окно «произошла ошибка» не показывается. Журнал не растёт больше ~256 КБ — старая половина отбрасывается.
    /// </summary>
    public static void Warn(string where, Exception ex)
    {
        try
        {
            lock (WarnLock)
            {
                var f = new FileInfo(WarnPath);
                if (f.Exists && f.Length > 256 * 1024)
                {
                    var t = File.ReadAllText(WarnPath);
                    File.WriteAllText(WarnPath, t[(t.Length / 2)..]);
                }
                File.AppendAllText(WarnPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}: {ex.GetType().Name}: {ex.Message}\n");
            }
        }
        catch { }
    }

    static string WarnTail(int lines)
    {
        try { return File.Exists(WarnPath) ? string.Join("\n", File.ReadLines(WarnPath).TakeLast(lines)) : "(нет)"; }
        catch { return "(нет)"; }
    }

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
        if (entry.Length > 3000) entry = entry[..3000] + "\n…";
        var warn = WarnTail(8);
        var body = $"""
            **Что произошло:** {userNote ?? "опиши, что делал перед ошибкой"}

            **Версия:** {Updater.Current.ToString(3)} · **Система:** {System.Runtime.InteropServices.RuntimeInformation.OSDescription}

            <details><summary>Журнал ошибки</summary>

            ```
            {entry}
            ```
            </details>

            <details><summary>Предупреждения</summary>

            ```
            {warn}
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
