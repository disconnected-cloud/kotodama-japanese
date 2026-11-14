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
    public sealed record Release(Version Version, string Tag, string Notes, string Url, long Size, string? SumsUrl = null);

    public static Version Current => typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0);
    static string AppDir => AppContext.BaseDirectory;
#if SELFCONTAINED
    static bool SelfContained => true;
#else
    static bool SelfContained => false;
#endif
    /// <summary>Архив своей сборки: Linux — tar.gz (права на запуск сохраняются), Windows — обычная или портативная.</summary>
    static string AssetName => OperatingSystem.IsAndroid() ? "Kotodama-android.apk"
        : OperatingSystem.IsLinux() ? "Kotodama-linux-x64.tar.gz"
        : SelfContained ? "Kotodama-portable-win-x64.zip" : "Kotodama-win-x64.zip";
    public static Release? Available { get; private set; }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static readonly HttpClient Download = new() { Timeout = Timeout.InfiniteTimeSpan };

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
            var r = Parse(doc.RootElement);
            if (r != null)
            {
                ProgressStore.P.LastUpdateCheck = DateTime.Now;
                Available = Normalize(r.Version) > Normalize(Current) ? r : null;
                return Available;
            }
        }
        catch (Exception e) { CrashReport.Warn("Проверка обновлений", e); }
        return null;
    }

    /// <summary>Разбор ответа GitHub о релизе: архив своей сборки и файл контрольных сумм SHA256SUMS.</summary>
    public static Release? Parse(JsonElement root)
    {
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v) || !root.TryGetProperty("assets", out var assets)) return null;
        string? url = null, sums = null; long size = 0;
        foreach (var a in assets.EnumerateArray())
        {
            var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
            var link = a.TryGetProperty("browser_download_url", out var l) ? l.GetString() : null;
            if (name == AssetName) { url = link; size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0; }
            else if (name == "SHA256SUMS") sums = link;
        }
        if (url == null) return null;
        return new Release(v, tag, root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "", url, size, sums);
    }

    /// <summary>Хэш файла из SHA256SUMS (формат sha256sum: «хэш␠␠имя» или «хэш␠*имя»).</summary>
    public static string? HashFor(string sums, string file)
    {
        foreach (var line in sums.Split('\n'))
        {
            var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].Trim().TrimStart('*') == file && parts[0].Length == 64) return parts[0].ToLowerInvariant();
        }
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
        // Телефон: APK ставит сама система — открываем страницу релиза, там файл для скачивания
        if (OperatingSystem.IsAndroid())
        {
            var launcher = Avalonia.Controls.TopLevel.GetTopLevel(Shell.Main)?.Launcher;
            if (launcher != null) await launcher.LaunchUriAsync(new Uri($"https://github.com/{Repo}/releases/latest"));
            return;
        }
        var work = Path.Combine(Path.GetTempPath(), "kotodama-update");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        var pkg = Path.Combine(work, OperatingSystem.IsLinux() ? "pkg.tar.gz" : "pkg.zip");
        // Без контрольной суммы не ставим: архив целиком заменит программу
        if (r.SumsUrl == null) throw new InvalidDataException("в релизе нет файла контрольных сумм — скачай новую версию вручную со страницы релиза");
        var want = HashFor(await Http.GetStringAsync(r.SumsUrl), AssetName) ?? throw new InvalidDataException("в SHA256SUMS нет строки для " + AssetName);
        using (var resp = await Download.SendAsync(new HttpRequestMessage(HttpMethod.Get, r.Url), HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(pkg);
            var buf = new byte[1 << 16]; long done = 0; int n;
            while ((n = await src.ReadAsync(buf)) > 0) { await dst.WriteAsync(buf.AsMemory(0, n)); done += n; progress.Report((double)done / Math.Max(1, r.Size)); }
        }
        await using (var f = File.OpenRead(pkg))
            if (Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(f)).ToLowerInvariant() != want)
                throw new InvalidDataException("архив обновления повреждён или подменён (контрольная сумма не совпала)");
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
        if (!File.Exists(Path.Combine(fresh, OperatingSystem.IsLinux() ? "Kotodama" : "Kotodama.exe")))
            throw new InvalidDataException("в архиве обновления нет программы Kotodama");

        ProgressStore.Save();
        int pid = Environment.ProcessId;
        if (OperatingSystem.IsLinux())
        {
            var exeL = Path.Combine(AppDir, "Kotodama");
            var sh = Path.Combine(work, "update.sh");
            // Ждём выхода программы, копируем новые файлы поверх (папку data не трогаем) и перезапускаем
            File.WriteAllText(sh, """
                #!/bin/sh
                while kill -0 "$KD_PID" 2>/dev/null; do sleep 1; done
                cd "$KD_SRC" || exit 1
                find . -path ./data -prune -o -type f -print | while read -r f; do
                  mkdir -p "$KD_DST/$(dirname "$f")"
                  cp -f "$f" "$KD_DST/$f"
                done
                chmod +x "$KD_EXE"
                nohup "$KD_EXE" >/dev/null 2>&1 &
                """.Replace("\r", ""));
            var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add(sh);
            psi.Environment["KD_PID"] = pid.ToString();
            psi.Environment["KD_SRC"] = fresh;
            psi.Environment["KD_DST"] = AppDir.TrimEnd('/');
            psi.Environment["KD_EXE"] = exeL;
            Process.Start(psi);
        }
        else
        {
            var exe = Path.Combine(AppDir, "Kotodama.exe");
            var script = Path.Combine(work, "update.cmd");
            // Пути — только через переменные окружения с отложенным раскрытием (!VAR!): кавычки, &, % и ' в пути не ломают скрипт
            File.WriteAllText(script, $"""
                @echo off
                setlocal EnableDelayedExpansion
                chcp 65001 >nul
                :wait
                tasklist /FI "PID eq !KD_PID!" | find "!KD_PID!" >nul && (timeout /t 1 /nobreak >nul & goto wait)
                robocopy "!KD_SRC!" "!KD_DST!" /E /XD data{(ProgressStore.Portable ? "" : " /XF portable ПРОЧТИ.txt")} /R:3 /W:1 /NFL /NDL /NJH /NJS /NP >nul
                start "" "!KD_EXE!"
                """);
            var psi = new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
            psi.Environment["KD_PID"] = pid.ToString();
            psi.Environment["KD_SRC"] = fresh.TrimEnd('\\');
            psi.Environment["KD_DST"] = AppDir.TrimEnd('\\');
            psi.Environment["KD_EXE"] = exe;
            Process.Start(psi);
        }
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
