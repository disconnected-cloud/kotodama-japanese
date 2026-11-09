using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Windows;

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
    /// <summary>Портативная сборка содержит встроенный .NET — узнаём по нативным библиотекам WPF рядом с exe.</summary>
    static bool SelfContained => File.Exists(Path.Combine(AppDir, "wpfgfx_cor3.dll"));
    static string AssetName => SelfContained ? "Kotodama-portable-win-x64.zip" : "Kotodama-win-x64.zip";
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
        var zip = Path.Combine(work, "pkg.zip");
        using (var resp = await new HttpClient { Timeout = Timeout.InfiniteTimeSpan }.GetAsync(r.Url, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(zip);
            var buf = new byte[1 << 16]; long done = 0; int n;
            while ((n = await src.ReadAsync(buf)) > 0) { await dst.WriteAsync(buf.AsMemory(0, n)); done += n; progress.Report((double)done / Math.Max(1, r.Size)); }
        }
        var fresh = Path.Combine(work, "new");
        ZipFile.ExtractToDirectory(zip, fresh);
        // Если в архиве одна корневая папка — берём её содержимое
        var dirs = Directory.GetDirectories(fresh);
        if (Directory.GetFiles(fresh).Length == 0 && dirs.Length == 1) fresh = dirs[0];

        ProgressStore.Save();
        int pid = Environment.ProcessId;
        var exe = Path.Combine(AppDir, "Kotodama.exe");
        var script = Path.Combine(work, "update.cmd");
        File.WriteAllText(script, $"""
            @echo off
            chcp 65001 >nul
            :wait
            tasklist /FI "PID eq {pid}" | find "{pid}" >nul && (timeout /t 1 /nobreak >nul & goto wait)
            robocopy "{fresh.TrimEnd('\\')}" "{AppDir.TrimEnd('\\')}" /E /XD data /R:3 /W:1 /NFL /NDL /NJH /NJS /NP >nul
            start "" "{exe}"
            """);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        Application.Current.Shutdown();
    }
}
