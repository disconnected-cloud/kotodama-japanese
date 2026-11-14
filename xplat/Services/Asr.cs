using System.IO;
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
        ("large-v3-turbo-q5_0", "Максимальная", "Whisper large-v3-turbo · лучше всех понимает речь с акцентом и ошибками; для ПК с 8 ГБ памяти и больше", 574_041_195),
        ("small-q5_1", "Точная", "Whisper small · хороший баланс скорости и точности", 190_085_487),
        ("base-q5_1", "Быстрая", OperatingSystem.IsAndroid() ? "Whisper base · для слабых телефонов, втрое быстрее" : "Whisper base · для слабых ПК", 59_707_625),
    };

    static string UrlFor(string id) => $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{id}.bin";

    /// <summary>Модель по умолчанию — первая скачанная из списка (максимальная → точная → быстрая).</summary>
    public static string BestInstalled => Models.Select(m => m.id).FirstOrDefault(id => File.Exists(PathFor(id))) ?? DefaultId;

    /// <summary>Что предложить скачать, пока пользователь не выбрал сам: большая модель — только для ПК с 8 ГБ памяти и больше,
    /// на слабом телефоне даже точная думает слишком долго.</summary>
    public static string DefaultId => Anim.LowEndDevice ? "base-q5_1" : BigOk ? Models[0].id : "small-q5_1";
    static bool BigOk => !OperatingSystem.IsAndroid() && GC.GetGCMemoryInfo().TotalAvailableMemoryBytes >= 7L << 30;

    static string Dir => Path.Combine(ProgressStore.Folder, "models");
    public static string ModelId => Models.Any(m => m.id == ProgressStore.P.AsrModel) && File.Exists(PathFor(ProgressStore.P.AsrModel)) ? ProgressStore.P.AsrModel
        : File.Exists(PathFor(BestInstalled)) ? BestInstalled : Models.Any(m => m.id == ProgressStore.P.AsrModel) ? ProgressStore.P.AsrModel : DefaultId;
    public static string PathFor(string id) => System.IO.Path.Combine(Dir, $"ggml-{id}.bin");
    /// <summary>Для отладочного сравнения моделей: явный путь к файлу модели.</summary>
    public static string? OverridePath;
    public static bool Ready => OverridePath != null || File.Exists(PathFor(ModelId));

    static WhisperFactory? _factory;
    static string? _loaded;
    static readonly SemaphoreSlim Gate = new(1, 1);
    static Timer? _idle;
    /// <summary>Модель (~300 МБ в памяти) выгружается через 3 минуты без распознавания.</summary>
    static void ScheduleUnload() => (_idle ??= new Timer(_ => Unload(), null, Timeout.Infinite, Timeout.Infinite)).Change(TimeSpan.FromMinutes(3), Timeout.InfiniteTimeSpan);

    /// <summary>Загрузка модели: потоком, с докачкой после обрыва и проверкой SHA-256 (Б2).</summary>
    public static Task DownloadAsync(string id, IProgress<double> progress, CancellationToken ct) =>
        Downloader.FileAsync(UrlFor(id), PathFor(id), progress, ct, Models.First(m => m.id == id).size);

    /// <summary>
    /// Замер скорости (мастер первого запуска): распознавание трёхсекундной фразы на этом компьютере, секунды.
    /// Образец — фраза курса голосом VOICEVOX или системным, иначе синтетический звук той же длины. Больше 2 с —
    /// мастер предлагает модель поменьше (5.2).
    /// </summary>
    public static async Task<double> BenchmarkAsync(CancellationToken ct = default)
    {
        byte[] wav;
        if (await Tts.GetAsync("こんにちは、よろしくお願いします。") is { } path)
        {
            var (x, rate) = Platform.Pcm.Decode(path);
            wav = Recorder.ToWav(Platform.Pcm.ToPcm16(Platform.Pcm.Resample(x, rate, 16000)));
        }
        else
        {
            var x = new float[16000 * 3];
            for (int i = 0; i < x.Length; i++) x[i] = 0.2f * (float)(Math.Sin(i * 0.05) * Math.Sin(i * 0.0021));
            wav = Recorder.ToWav(Platform.Pcm.ToPcm16(x));
        }
        await TranscribeDetailedAsync(wav, ct); // первый проход загружает модель — не считаем
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await TranscribeDetailedAsync(wav, ct);
        return sw.Elapsed.TotalSeconds;
    }

    /// <summary>Следующая модель поменьше (для медленного компьютера) или null.</summary>
    public static string? Smaller(string id)
    {
        int i = Array.FindIndex(Models, m => m.id == id);
        return i >= 0 && i + 1 < Models.Length ? Models[i + 1].id : null;
    }

    /// <summary>Заранее загрузить модель в память, чтобы первое распознавание было быстрым.</summary>
    public static void Warmup()
    {
        _ = JDict.LoadAsync(); // словарь нужен, чтобы читать распознанные кандзи
        if (!Ready) return;
        Task.Run(async () =>
        {
            await Gate.WaitAsync();
            try { EnsureLoaded(); } catch (Exception e) { CrashReport.Warn("Whisper: загрузка модели", e); } finally { Gate.Release(); }
        });
    }

    static void EnsureLoaded()
    {
        var path = OverridePath ?? PathFor(ModelId);
        if (_factory != null && _loaded == path) return;
        _factory?.Dispose();
        _factory = WhisperFactory.FromPath(path);
        _loaded = path;
    }

    /// <summary>
    /// Распознать WAV (16 кГц моно). Возвращает текст и уверенность 0..1.
    /// Ускорение: Whisper по умолчанию кодирует 30-секундное окно; для короткой фразы
    /// уменьшаем аудиоконтекст под реальную длину (в разы быстрее) и отключаем повторные проходы.
    /// </summary>
    /// <param name="prompt">Ожидаемая фраза: подсказывает модели словарь и написание (короткие слова распознаются намного надёжнее).</param>
    public static async Task<(string text, float confidence)> TranscribeAsync(byte[] wav, CancellationToken ct, string? prompt = null)
    {
        var r = await TranscribeDetailedAsync(wav, ct, prompt);
        return (r.Text, r.Confidence);
    }

    /// <summary>Распознавание с уверенностью модели по каждому фрагменту (токену) — чтобы показать, что прозвучало нечётко.</summary>
    public static async Task<AsrResult> TranscribeDetailedAsync(byte[] wav, CancellationToken ct, string? prompt = null)
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
                // телефон: у big.LITTLE половина ядер медленные — с ними все потоки ждут отстающих, 4 потока быстрее 8
                .WithThreads(OperatingSystem.IsAndroid() ? Math.Clamp(Environment.ProcessorCount / 2, 2, 4) : Math.Clamp(Environment.ProcessorCount - 1, 2, 8))
                .WithAudioContextSize(ctx)
                .WithSingleSegment()
                .WithNoContext()
                .WithProbabilities()
                .WithTemperatureInc(0);
            // На ПК — поиск лучом: короткие фразы с акцентом распознаются заметно точнее, а время для 2–3 секунд записи почти то же
            if (!OperatingSystem.IsAndroid()) b = b.WithBeamSearchSamplingStrategy(s => s.WithBeamSize(4));
            if (!string.IsNullOrWhiteSpace(prompt)) b = b.WithPrompt(prompt);
            using var proc = b.Build();
            var sb = new StringBuilder();
            var tokens = new List<(string, float)>();
            float p = 0; int n = 0;
            using var ms = new MemoryStream(wav);
            await foreach (var seg in proc.ProcessAsync(ms, ct))
            {
                sb.Append(seg.Text);
                p += seg.Probability; n++;
                if (seg.Tokens != null)
                    foreach (var t in seg.Tokens)
                        if (!string.IsNullOrEmpty(t.Text) && !t.Text.StartsWith("[_") && !t.Text.StartsWith("<|")) tokens.Add((t.Text, t.Probability));
            }
            return new AsrResult(sb.ToString().Trim(), n > 0 ? p / n : 0, tokens.ToArray());
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
        Clean(pcm, edge, pcmLen);
        return Recorder.ToWav(pcm);
    }

    /// <summary>
    /// Чистка перед распознаванием: шумоподавление (только на шумной записи), фильтр 80 Гц против гула
    /// (вентилятор, сеть, удары по столу), затем громкость по 99,9-му перцентилю до −1 дБ —
    /// одиночный щелчок больше не «съедает» усиление, тихий голос становится нормальным.
    /// </summary>
    public static bool UseDenoise = true;

    static void Clean(byte[] pcm, int offset, int len)
    {
        int n = len / 2;
        if (n < 32) return;
        var x = new float[n];
        for (int i = 0; i < n; i++) x[i] = BitConverter.ToInt16(pcm, offset + i * 2);
        if (UseDenoise) Denoise.Apply(x);
        const double rc = 1.0 / (2 * Math.PI * 80), dt = 1.0 / 16000, a = rc / (rc + dt);
        double prevX = 0, prevY = 0;
        for (int i = 0; i < n; i++)
        {
            double v = x[i];
            prevY = a * (prevY + v - prevX); prevX = v;
            x[i] = (float)prevY;
        }
        var abs = x.Select(Math.Abs).ToArray();
        Array.Sort(abs);
        double peak = abs[Math.Min(n - 1, (int)(n * 0.999))];
        double gain = peak > 50 ? Math.Min(12, 0.89 * 32767 / peak) : 1;
        for (int i = 0; i < n; i++)
        {
            int v = (int)Math.Clamp(x[i] * gain, short.MinValue, short.MaxValue);
            pcm[offset + i * 2] = (byte)(v & 0xFF); pcm[offset + i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
    }

    /// <summary>Выгрузка из фона (таймер простоя): синхронное ожидание здесь безопасно.</summary>
    public static void Unload() => UnloadAsync().GetAwaiter().GetResult();

    /// <summary>Выгрузка из интерфейса: если идёт распознавание, окно не замирает, а выгрузка дождётся его конца (Д3).</summary>
    public static async Task UnloadAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try { _factory?.Dispose(); _factory = null; _loaded = null; }
        finally { Gate.Release(); }
    }
}

/// <summary>Текст, средняя уверенность и уверенность по фрагментам.</summary>
public sealed record AsrResult(string Text, float Confidence, (string text, float p)[] Tokens);
