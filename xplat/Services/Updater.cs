using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Matsubakira.Services;

/// <summary>
/// Автообновление из релизов GitHub. Скачивает архив своей сборки (обычная / портативная),
/// после закрытия программы заменяет файлы (папка data не трогается) и перезапускает.
/// </summary>
public static class Updater
{
    public const string Repo = "disconnected-cloud/kotodama-japanese";
    public sealed record Release(Version Version, string Tag, string Notes, string Url, long Size);

    public static Version Current => typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0);
    static string AppDir => AppContext.BaseDirectory;
#if SELFCONTAINED
    static bool SelfContained => true;
#else
    static bool SelfContained => false;
#endif
    /// <summary>Архив своей сборки: Linux — tar.gz (права на запуск сохраняются), Windows — обычная или портативная.</summary>
    static string AssetName => OperatingSystem.IsLinux() ? "Kotodama-linux-x64.tar.gz"
        : SelfContained ? "Kotodama-portable-win-x64.zip" : "Kotodama-win-x64.zip";
    public static Release? Available { get; private set; }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<Release?> CheckAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            req.Headers.UserAgent.ParseAdd("Kotodama-Updater");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return null;
            foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
                if (a.GetProperty("name").GetString() == AssetName)
                {
                    var r = new Release(v, tag, doc.RootElement.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                        a.GetProperty("browser_download_url").GetString()!, a.GetProperty("size").GetInt64());
                    ProgressStore.P.LastUpdateCheck = DateTime.Now;
                    Available = Normalize(v) > Normalize(Current) ? r : null;
                    return Available;
                }
        }
        catch { }
        return null;
    }

    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    /// <summary>Фоновая проверка раз в сутки при запуске.</summary>
    public static async void AutoCheck()
    {
        if (!ProgressStore.P.AutoUpdate || (DateTime.Now - ProgressStore.P.LastUpdateCheck).TotalHours < 20) return;
        var r = await CheckAsync();
        if (r != null) Shell.Toast($"Доступна новая версия {r.Tag} — Настройки → Обновления", "新");
    }

    public static async Task InstallAsync(Release r, IProgress<double> progress)
    {
        var work = Path.Combine(Path.GetTempPath(), "kotodama-update");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        var pkg = Path.Combine(work, OperatingSystem.IsLinux() ? "pkg.tar.gz" : "pkg.zip");
        using (var resp = await new HttpClient { Timeout = Timeout.InfiniteTimeSpan }.GetAsync(r.Url, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(pkg);
            var buf = new byte[1 << 16]; long done = 0; int n;
            while ((n = await src.ReadAsync(buf)) > 0) { await dst.WriteAsync(buf.AsMemory(0, n)); done += n; progress.Report((double)done / Math.Max(1, r.Size)); }
        }
        var fresh = Path.Combine(work, "new");
        Directory.CreateDirectory(fresh);
        if (OperatingSystem.IsLinux())
        {
            await using var fs = File.OpenRead(pkg);
            await using var gz = new GZipStream(fs, CompressionMode.Decompress);
            await System.Formats.Tar.TarFile.ExtractToDirectoryAsync(gz, fresh, true);
        }
        else ZipFile.ExtractToDirectory(pkg, fresh);
        // Если в архиве одна корневая папка — берём её содержимое
        var dirs = Directory.GetDirectories(fresh);
        if (Directory.GetFiles(fresh).Length == 0 && dirs.Length == 1) fresh = dirs[0];

        ProgressStore.Save();
        int pid = Environment.ProcessId;
        if (OperatingSystem.IsLinux())
        {
            var exeL = Path.Combine(AppDir, "Kotodama");
            var sh = Path.Combine(work, "update.sh");
            // Ждём выхода программы, копируем новые файлы поверх (папку data не трогаем) и перезапускаем
            File.WriteAllText(sh, $"""
                #!/bin/sh
                while kill -0 {pid} 2>/dev/null; do sleep 1; done
                SRC='{fresh}'
                DST='{AppDir.TrimEnd('/')}'
                cd "$SRC" || exit 1
                find . -path ./data -prune -o -type f -print | while read -r f; do
                  mkdir -p "$DST/$(dirname "$f")"
                  cp -f "$f" "$DST/$f"
                done
                chmod +x '{exeL}'
                nohup '{exeL}' >/dev/null 2>&1 &
                """.Replace("\r", ""));
            Process.Start(new ProcessStartInfo("/bin/sh", $"\"{sh}\"") { UseShellExecute = false, CreateNoWindow = true });
        }
        else
        {
            var exe = Path.Combine(AppDir, "Kotodama.exe");
            var script = Path.Combine(work, "update.cmd");
            File.WriteAllText(script, $"""
                @echo off
                chcp 65001 >nul
                :wait
                tasklist /FI "PID eq {pid}" | find "{pid}" >nul && (timeout /t 1 /nobreak >nul & goto wait)
                robocopy "{fresh.TrimEnd('\\')}" "{AppDir.TrimEnd('\\')}" /E /XD data{(ProgressStore.Portable ? "" : " /XF portable ПРОЧТИ.txt")} /R:3 /W:1 /NFL /NDL /NJH /NJS /NP >nul
                start "" "{exe}"
                """);
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
