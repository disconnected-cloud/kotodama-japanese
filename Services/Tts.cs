using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace Matsubakira.Services;

/// <summary>
/// Нейросетевая японская озвучка без установки языковых пакетов.
/// 1) Microsoft Neural (Nanami/Keita) — качество носителя; 2) запасной — Google.
/// Каждая фраза кэшируется в mp3: повторное воспроизведение мгновенное и работает офлайн.
/// </summary>
public static class Tts
{
    public static readonly string CacheDir = Path.Combine(ProgressStore.Folder, "audio");
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    const string Token = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    const string ChromiumVer = "130.0.2849.68";
    const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0";

    // Не даём параллельно синтезировать одну и ту же фразу дважды
    static readonly Dictionary<string, Task<string?>> Inflight = new();
    static readonly object Gate = new();

    public static string? LastError { get; private set; }

    /// <summary>Офлайн-голос VOICEVOX используется, если он установлен и выбран (или режим «авто»).</summary>
    public static bool UseOffline => ProgressStore.P.TtsEngine switch
    {
        "online" => false,
        _ => Voicevox.Installed,
    };

    public static string CachePath(string text, bool slow, bool male)
    {
        bool off = UseOffline;
        var voice = off ? "vv" + Voicevox.PickStyle(male) : male ? "m" : "f";
        var key = $"{voice}|{(slow ? "s" : "n")}|{text.Trim()}";
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..20];
        return Path.Combine(CacheDir, hash + (off ? ".wav" : ".mp3"));
    }

    public static bool IsCached(string text, bool slow = false, bool? male = null) =>
        File.Exists(CachePath(text, slow, male ?? ProgressStore.P.MaleVoice));

    /// <summary>Возвращает путь к mp3 (из кэша или синтезирует). null — нет сети и нет кэша.</summary>
    public static Task<string?> GetAsync(string text, bool slow = false, bool? male = null)
    {
        bool m = male ?? ProgressStore.P.MaleVoice;
        var path = CachePath(text, slow, m);
        if (File.Exists(path)) return Task.FromResult<string?>(path);
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
        byte[]? data = null;
        if (path.EndsWith(".wav"))
        {
            // Офлайн: VOICEVOX
            data = Voicevox.Synthesize(text, Voicevox.PickStyle(male), slow ? 0.72 : 1.0);
            if (data is { Length: > 1000 })
            {
                Directory.CreateDirectory(CacheDir);
                try
                {
                    await File.WriteAllBytesAsync(path + ".tmp", data);
                    File.Move(path + ".tmp", path, true);
                }
                catch (IOException) when (File.Exists(path)) { }
                return path;
            }
            LastError = "VOICEVOX: " + Voicevox.LastError;
            if (ProgressStore.P.TtsEngine == "offline") return null;
            path = Path.ChangeExtension(path, ".mp3"); // авто-режим: пробуем онлайн
            data = null;
        }
        try { data = await EdgeAsync(text, male ? "ja-JP, KeitaNeural" : "ja-JP, NanamiNeural", slow ? "-35%" : "-5%"); }
        catch (Exception ex) { LastError = "Neural: " + ex.Message; }
        if (data is not { Length: > 600 })
        {
            try { data = await GoogleAsync(text, slow); }
            catch (Exception ex) { LastError += " · Google: " + ex.Message; }
        }
        if (data is not { Length: > 600 }) return null;
        Directory.CreateDirectory(CacheDir);
        try
        {
            var tmp = path + "." + Guid.NewGuid().ToString("N")[..6] + ".tmp";
            await File.WriteAllBytesAsync(tmp, data);
            File.Move(tmp, path, true);
        }
        catch (IOException) when (File.Exists(path)) { }
        LastError = null;
        return path;
    }

    // ─────────── Microsoft Edge Neural (тот же движок, что «Читать вслух» в Edge) ───────────
    static string Gec()
    {
        long ticks = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 11644473600L;
        ticks -= ticks % 300;
        var s = (ticks * 10_000_000L).ToString(CultureInfo.InvariantCulture) + Token;
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(s)));
    }

    static async Task<byte[]?> EdgeAsync(string text, string voice, string rate)
    {
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Pragma", "no-cache");
        ws.Options.SetRequestHeader("Cache-Control", "no-cache");
        ws.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        ws.Options.SetRequestHeader("User-Agent", UA);
        ws.Options.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");
        ws.Options.SetRequestHeader("Cookie", "muid=" + Guid.NewGuid().ToString("N").ToUpperInvariant() + ";");
        var url = $"wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1?TrustedClientToken={Token}" +
                  $"&Sec-MS-GEC={Gec()}&Sec-MS-GEC-Version=1-{ChromiumVer}&ConnectionId={Guid.NewGuid():N}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        await ws.ConnectAsync(new Uri(url), cts.Token);

        string date = DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT+0000 (Coordinated Universal Time)";
        string config = $"X-Timestamp:{date}\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n" +
                        "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"},\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}\r\n";
        await ws.SendAsync(Encoding.UTF8.GetBytes(config), WebSocketMessageType.Text, true, cts.Token);

        string ssml = "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='ja-JP'>" +
                      $"<voice name='Microsoft Server Speech Text to Speech Voice ({voice})'><prosody pitch='+0Hz' rate='{rate}' volume='+0%'>" +
                      SecurityElement.Escape(text) + "</prosody></voice></speak>";
        string req = $"X-RequestId:{Guid.NewGuid():N}\r\nContent-Type:application/ssml+xml\r\nX-Timestamp:{date}Z\r\nPath:ssml\r\n\r\n{ssml}";
        await ws.SendAsync(Encoding.UTF8.GetBytes(req), WebSocketMessageType.Text, true, cts.Token);

        var audio = new MemoryStream();
        var buf = new byte[32 * 1024];
        while (ws.State == WebSocketState.Open)
        {
            using var msg = new MemoryStream();
            WebSocketReceiveResult r;
            do { r = await ws.ReceiveAsync(buf, cts.Token); msg.Write(buf, 0, r.Count); } while (!r.EndOfMessage);
            if (r.MessageType == WebSocketMessageType.Close) break;
            var bytes = msg.GetBuffer();
            int len = (int)msg.Length;
            if (r.MessageType == WebSocketMessageType.Text)
            {
                if (Encoding.UTF8.GetString(bytes, 0, len).Contains("Path:turn.end")) break;
            }
            else if (len >= 2)
            {
                int hl = (bytes[0] << 8) | bytes[1];
                if (2 + hl <= len && Encoding.ASCII.GetString(bytes, 2, hl).Contains("Path:audio"))
                    audio.Write(bytes, 2 + hl, len - 2 - hl);
            }
        }
        try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        return audio.ToArray();
    }

    // ─────────── Запасной вариант: Google ───────────
    static async Task<byte[]?> GoogleAsync(string text, bool slow)
    {
        if (text.Length > 190) text = text[..190];
        var url = $"https://translate.google.com/translate_tts?ie=UTF-8&tl=ja&client=tw-ob&ttsspeed={(slow ? "0.4" : "1")}&q={Uri.EscapeDataString(text)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd(UA);
        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync();
    }

    /// <summary>Скачать озвучку всего курса заранее — дальше всё работает без интернета.</summary>
    public static async Task<int> PrefetchAsync(IReadOnlyList<string> texts, IProgress<(int done, int total)> progress, CancellationToken ct)
    {
        int done = 0, ok = 0;
        using var sem = new SemaphoreSlim(4);
        var tasks = texts.Distinct().Select(async t =>
        {
            await sem.WaitAsync(ct);
            try { if (await GetAsync(t) != null) Interlocked.Increment(ref ok); }
            finally { sem.Release(); progress.Report((Interlocked.Increment(ref done), texts.Count)); }
        });
        await Task.WhenAll(tasks);
        return ok;
    }

    public static long CacheSizeBytes()
    {
        try { return Directory.Exists(CacheDir) ? new DirectoryInfo(CacheDir).EnumerateFiles("*.mp3").Sum(f => f.Length) : 0; }
        catch { return 0; }
    }
}
