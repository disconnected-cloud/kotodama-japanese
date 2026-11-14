using System.IO;
using System.Security.Cryptography;
using System.Text;
using Matsubakira.Platform;

namespace Matsubakira.Services;

/// <summary>
/// Японская озвучка без интернета. Порядок: 1) VOICEVOX (нейросетевые голоса, ставятся из Настроек);
/// 2) системный японский голос (Windows — языковой пакет, Linux — Open JTalk; телефон — голос Android);
/// 3) фразы, озвученные прежними версиями онлайн, — только воспроизведение из кэша, кэш не пополняется.
/// Каждая фраза кэшируется: повторно звучит мгновенно.
/// </summary>
public static class Tts
{
    public static readonly string CacheDir = Path.Combine(ProgressStore.Folder, "audio");

    // Не даём параллельно синтезировать одну и ту же фразу дважды
    static readonly Dictionary<string, Task<string?>> Inflight = new();
    static readonly object Gate = new();

    public static string? LastError { get; private set; }

    /// <summary>Офлайн-голос VOICEVOX установлен и прошёл проверку — он звучит.</summary>
    public static bool UseOffline => Voicevox.Installed;

    /// <summary>Есть ли вообще чем озвучивать японский (иначе показываем, как установить голос).</summary>
    public static bool HasVoice => UseOffline || SystemVoice.HasJa || Hooks.SystemTts != null;

    public static string CachePath(string text, bool slow, bool male)
    {
        bool off = UseOffline;
        var voice = off ? "vv3-" + Voicevox.PickStyle(male) : "sys-" + SystemVoice.JaName; // vv3 — после чистки звука и проверки чтения кандзи
        return KeyPath($"{voice}|{(slow ? "s" : "n")}|{text.Trim()}", ".wav");
    }

    /// <summary>Фраза, озвученная онлайн-голосом прежних версий (Nanami/Keita): воспроизводим, но не пополняем.</summary>
    static string LegacyPath(string text, bool slow, bool male) => KeyPath($"{(male ? "m" : "f")}|{(slow ? "s" : "n")}|{text.Trim()}", ".mp3");

    static string KeyPath(string key, string ext) =>
        Path.Combine(CacheDir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..20] + ext);

    public static bool IsCached(string text, bool slow = false, bool? male = null)
    {
        text = Speech.Spoken(text);
        bool m = male ?? ProgressStore.P.MaleVoice;
        return File.Exists(CachePath(text, slow, m)) || (!UseOffline && File.Exists(LegacyPath(text, slow, m)));
    }

    /// <summary>Путь к звуку фразы (из кэша или синтезирует). null — нет ни одного японского голоса.</summary>
    public static Task<string?> GetAsync(string text, bool slow = false, bool? male = null)
    {
        text = Speech.Spoken(text); // отдельный は — «ха», а не частица «ва»
        bool m = male ?? ProgressStore.P.MaleVoice;
        var path = CachePath(text, slow, m);
        if (File.Exists(path)) return Task.FromResult<string?>(path);
        if (!UseOffline && LegacyPath(text, slow, m) is var old && File.Exists(old)) return Task.FromResult<string?>(old);
        lock (Gate)
        {
            if (Inflight.TryGetValue(path, out var t)) return t;
            t = Task.Run(() => Synthesize(text.Trim(), slow, m, path));
            Inflight[path] = t;
            t.ContinueWith(_ => { lock (Gate) Inflight.Remove(path); });
            return t;
        }
    }

    static async Task<string?> Synthesize(string text, bool slow, bool male, string path)
    {
        byte[]? data;
        if (UseOffline)
        {
            data = Voicevox.Synthesize(text, Voicevox.PickStyle(male), slow ? 0.8 : 1.0);
            if (data is not { Length: > 1000 }) LastError = "VOICEVOX: " + Voicevox.LastError;
        }
        else data = SystemVoice.SpeakJa(text, slow);
        if (data is not { Length: > 1000 })
        {
            if (Hooks.SystemTts is { } sys) return await sys(text, slow); // телефон: системный японский голос
            LastError ??= "Нет японского голоса. " + SystemVoice.HowToInstallJa;
            return null;
        }
        await WriteCache(path, data);
        LastError = null;
        return path;
    }

    static async Task WriteCache(string path, byte[] data)
    {
        Directory.CreateDirectory(CacheDir);
        try
        {
            var tmp = path + "." + Guid.NewGuid().ToString("N")[..6] + ".tmp";
            await File.WriteAllBytesAsync(tmp, data);
            File.Move(tmp, path, true);
        }
        catch (IOException) when (File.Exists(path)) { }
    }

    /// <summary>
    /// Русская озвучка перевода (аудиоплеер): системный русский голос (Windows — есть в русской системе,
    /// Linux — RHVoice или espeak-ng). null — голоса нет; тогда плеер просто показывает перевод.
    /// </summary>
    public static async Task<string?> GetRuAsync(string text)
    {
        var old = KeyPath("ru|" + text.Trim(), ".mp3"); // озвучено онлайн прежними версиями
        if (File.Exists(old)) return old;
        if (!SystemVoice.HasRu) return null;
        var path = KeyPath($"ru-sys-{SystemVoice.RuName}|{text.Trim()}", ".wav");
        if (File.Exists(path)) return path;
        var data = await Task.Run(() => SystemVoice.SpeakRu(text.Trim()));
        if (data is not { Length: > 1000 }) return null;
        await WriteCache(path, data);
        return path;
    }

    /// <summary>Озвучить весь курс заранее (VOICEVOX в фоне). Отмена — пауза: озвученное остаётся в кэше.</summary>
    public static async Task<int> PrefetchAsync(IReadOnlyList<string> texts, IProgress<(int done, int total)> progress, CancellationToken ct, long maxCacheBytes = long.MaxValue)
    {
        int done = 0, ok = 0;
        // синтез занимает процессор: два потока не мешают учиться в это время
        using var sem = new SemaphoreSlim(2);
        var tasks = texts.Distinct().Select(async t =>
        {
            await sem.WaitAsync(ct);
            try
            {
                if (ct.IsCancellationRequested || CacheSizeBytes() > maxCacheBytes) return;
                if (await GetAsync(t) != null) Interlocked.Increment(ref ok);
            }
            finally { sem.Release(); progress.Report((Interlocked.Increment(ref done), texts.Count)); }
        });
        try { await Task.WhenAll(tasks); } catch (OperationCanceledException) { }
        return ok;
    }

    static long _sizeAt, _size;

    /// <summary>Размер кэша озвучки (пересчитывается не чаще раза в 5 с — вызывается в цикле предзагрузки).</summary>
    public static long CacheSizeBytes()
    {
        if (Environment.TickCount64 - _sizeAt < 5000) return _size;
        try { _size = Directory.Exists(CacheDir) ? new DirectoryInfo(CacheDir).EnumerateFiles().Where(f => f.Extension is ".mp3" or ".wav").Sum(f => f.Length) : 0; }
        catch { _size = 0; }
        _sizeAt = Environment.TickCount64;
        return _size;
    }
}
