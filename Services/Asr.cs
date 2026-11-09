using System.IO;
using System.Net.Http;
using System.Text;
using Whisper.net;

namespace Matsubakira.Services;

/// <summary>
/// Офлайн-распознавание японской речи моделью OpenAI Whisper (whisper.cpp через Whisper.net).
/// Модель скачивается один раз, дальше работает без интернета.
/// </summary>
public static class Asr
{
    public static readonly (string id, string title, string desc, long size)[] Models =
    {
        ("small-q5_1", "Точная", "Whisper small · лучше слышит ошибки", 190_085_487),
        ("base-q5_1", "Быстрая", "Whisper base · для слабых ПК", 59_707_625),
    };

    static string Dir => Path.Combine(ProgressStore.Folder, "models");
    public static string ModelId => Models.Any(m => m.id == ProgressStore.P.AsrModel) ? ProgressStore.P.AsrModel : Models[0].id;
    public static string PathFor(string id) => System.IO.Path.Combine(Dir, $"ggml-{id}.bin");
    public static bool Ready => File.Exists(PathFor(ModelId));

    static WhisperFactory? _factory;
    static string? _loaded;
    static readonly SemaphoreSlim Gate = new(1, 1);
    static Timer? _idle;
    /// <summary>Модель (~300 МБ в памяти) выгружается через 3 минуты без распознавания.</summary>
    static void ScheduleUnload() => (_idle ??= new Timer(_ => Unload(), null, Timeout.Infinite, Timeout.Infinite)).Change(TimeSpan.FromMinutes(3), Timeout.InfiniteTimeSpan);

    public static async Task DownloadAsync(string id, IProgress<double> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Dir);
        var url = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{id}.bin";
        var part = PathFor(id) + ".part";
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? Models.First(m => m.id == id).size;
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(part))
        {
            var buf = new byte[1 << 16];
            long done = 0; int n; long lastReport = 0;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (done - lastReport > 1 << 20) { progress.Report((double)done / total); lastReport = done; }
            }
        }
        File.Move(part, PathFor(id), true);
        progress.Report(1);
    }

    /// <summary>Заранее загрузить модель в память, чтобы первое распознавание было быстрым.</summary>
    public static void Warmup()
    {
        _ = JDict.LoadAsync(); // словарь нужен, чтобы читать распознанные кандзи
        if (!Ready) return;
        Task.Run(async () =>
        {
            await Gate.WaitAsync();
            try { EnsureLoaded(); } catch { } finally { Gate.Release(); }
        });
    }

    static void EnsureLoaded()
    {
        if (_factory != null && _loaded == ModelId) return;
        _factory?.Dispose();
        _factory = WhisperFactory.FromPath(PathFor(ModelId));
        _loaded = ModelId;
    }

    /// <summary>
    /// Распознать WAV (16 кГц моно). Возвращает текст и уверенность 0..1.
    /// Ускорение: Whisper по умолчанию кодирует 30-секундное окно; для короткой фразы
    /// уменьшаем аудиоконтекст под реальную длину (в разы быстрее) и отключаем повторные проходы.
    /// </summary>
    /// <param name="prompt">Ожидаемая фраза: подсказывает модели словарь и написание (короткие слова распознаются намного надёжнее).</param>
    public static async Task<(string text, float confidence)> TranscribeAsync(byte[] wav, CancellationToken ct, string? prompt = null)
    {
        await Gate.WaitAsync(ct);
        try
        {
            EnsureLoaded();
            wav = Pad(wav);
            double sec = (wav.Length - 44) / 32000.0;
            int ctx = Math.Clamp((int)(sec * 50) + 128, 256, 1500);
            var b = _factory!.CreateBuilder()
                .WithLanguage("ja")
                .WithThreads(Math.Clamp(Environment.ProcessorCount - 1, 2, 8))
                .WithAudioContextSize(ctx)
                .WithSingleSegment()
                .WithNoContext()
                .WithProbabilities()
                .WithTemperatureInc(0);
            if (!string.IsNullOrWhiteSpace(prompt)) b = b.WithPrompt(prompt);
            using var proc = b.Build();
            var sb = new StringBuilder();
            float p = 0; int n = 0;
            using var ms = new MemoryStream(wav);
            await foreach (var seg in proc.ProcessAsync(ms, ct))
            {
                sb.Append(seg.Text);
                p += seg.Probability; n++;
            }
            return (sb.ToString().Trim(), n > 0 ? p / n : 0);
        }
        finally { Gate.Release(); ScheduleUnload(); }
    }

    /// <summary>
    /// Тишина по краям (0,35 с) и минимум 1,6 с: whisper.cpp отбрасывает записи короче секунды,
    /// а короткие слова («はい», «え») без запаса тишины часто превращаются в пустую строку.
    /// </summary>
    public static byte[] Pad(byte[] wav)
    {
        if (wav.Length < 44) return wav;
        const int edge = 16000 * 2 * 35 / 100, min = 16000 * 2 * 16 / 10;
        int pcmLen = wav.Length - 44;
        int total = Math.Max(min, pcmLen + 2 * edge);
        total += total & 1;
        var pcm = new byte[total];
        Buffer.BlockCopy(wav, 44, pcm, edge, pcmLen);
        return Recorder.ToWav(pcm);
    }

    public static void Unload()
    {
        Gate.Wait();
        try { _factory?.Dispose(); _factory = null; _loaded = null; }
        finally { Gate.Release(); }
    }
}
