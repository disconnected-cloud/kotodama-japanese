using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace Matsubakira.Services;

/// <summary>
/// Локальная языковая модель (llama.cpp через LLamaSharp) для свободного разговора на японском.
/// Работает без интернета на процессоре; модель скачивается один раз (1–2 ГБ) и выгружается из памяти через 5 минут простоя.
/// </summary>
public static class Llm
{
    public sealed record ModelInfo(string Id, string Title, string Desc, long Size, string Url, string License);

    public static readonly ModelInfo[] Models =
    {
        new("qwen2.5-1.5b", "Быстрая", "Qwen 2.5 · 1,5 млрд параметров — шустрая, для любого ПК", 1_117_320_736,
            "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf", "Apache 2.0"),
        new("gemma2-2b-jpn", "Японская", "Gemma 2 JPN · дообучена на японском, говорит естественнее", 1_708_583_072,
            "https://huggingface.co/bartowski/gemma-2-2b-jpn-it-GGUF/resolve/main/gemma-2-2b-jpn-it-Q4_K_M.gguf", "Gemma Terms of Use"),
        new("qwen2.5-3b", "Умная", "Qwen 2.5 · 3 млрд параметров — лучше держит разговор, нужна 4+ ГБ памяти", 2_104_932_768,
            "https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf", "Qwen Research License"),
    };

    static string Dir => Path.Combine(ProgressStore.Folder, "llm");
    public static string PathFor(string id) => Path.Combine(Dir, id + ".gguf");
    public static string ModelId => Models.Any(m => m.Id == ProgressStore.P.LlmModel && File.Exists(PathFor(m.Id))) ? ProgressStore.P.LlmModel
        : Models.Select(m => m.Id).FirstOrDefault(id => File.Exists(PathFor(id))) ?? (string.IsNullOrEmpty(ProgressStore.P.LlmModel) ? Models[0].Id : ProgressStore.P.LlmModel);
    /// <summary>Для отладки: явный путь к файлу модели.</summary>
    public static string? OverridePath;
    public static bool Ready => OverridePath != null || File.Exists(PathFor(ModelId));
    public static ModelInfo Info => Models.FirstOrDefault(m => m.Id == ModelId) ?? Models[0];

    static LLamaWeights? _weights;
    static ModelParams? _params;
    static string? _loaded;
    static readonly SemaphoreSlim Gate = new(1, 1);
    static Timer? _idle;

    public static async Task DownloadAsync(string id, IProgress<double> progress, CancellationToken ct)
    {
        var m = Models.First(x => x.Id == id);
        Directory.CreateDirectory(Dir);
        var part = PathFor(id) + ".part";
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        long have = File.Exists(part) ? new FileInfo(part).Length : 0;
        using var req = new HttpRequestMessage(HttpMethod.Get, m.Url);
        if (have > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null); // докачка после обрыва
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        if (resp.StatusCode != System.Net.HttpStatusCode.PartialContent) have = 0;
        long total = have + (resp.Content.Headers.ContentLength ?? m.Size - have);
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create))
        {
            var buf = new byte[1 << 17];
            long done = have, last = 0; int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (done - last > 4 << 20) { progress.Report((double)done / total); last = done; }
            }
        }
        File.Move(part, PathFor(id), true);
        progress.Report(1);
    }

    static void EnsureLoaded()
    {
        var path = OverridePath ?? PathFor(ModelId);
        if (_weights != null && _loaded == path) return;
        _weights?.Dispose();
        _params = new ModelParams(path)
        {
            ContextSize = 2048,
            GpuLayerCount = 0,
            Threads = Math.Clamp(Environment.ProcessorCount - 1, 2, 8),
        };
        _weights = LLamaWeights.LoadFromFile(_params);
        _loaded = path;
    }

    /// <summary>Заранее загрузить модель (первый ответ быстрее).</summary>
    public static void Warmup()
    {
        if (!Ready) return;
        Task.Run(async () => { await Gate.WaitAsync(); try { EnsureLoaded(); } catch { } finally { Gate.Release(); ScheduleUnload(); } });
    }

    static void ScheduleUnload() => (_idle ??= new Timer(_ => Unload(), null, Timeout.Infinite, Timeout.Infinite)).Change(TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan);

    public static void Unload()
    {
        Gate.Wait();
        try { _weights?.Dispose(); _weights = null; _loaded = null; }
        finally { Gate.Release(); }
    }

    /// <summary>
    /// Ответ модели по истории сообщений (роли system / user / assistant) потоком кусочков текста.
    /// Шаблон диалога берётся из самой модели (у Qwen и Gemma он разный).
    /// </summary>
    public static async IAsyncEnumerable<string> ChatAsync(IReadOnlyList<(string role, string text)> messages, int maxTokens = 160, float temperature = 0.7f,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            EnsureLoaded();
            var tpl = new LLamaTemplate(_weights!) { AddAssistant = true };
            bool gemma = (OverridePath ?? ModelId).Contains("gemma");
            foreach (var (role, text) in messages)
            {
                // у Gemma нет роли system — инструкция идёт первым сообщением пользователя
                if (gemma && role == "system") { tpl.Add("user", text); tpl.Add("assistant", "はい、わかりました。"); continue; }
                tpl.Add(role, text);
            }
            var prompt = Encoding.UTF8.GetString(tpl.Apply());
            var exec = new StatelessExecutor(_weights!, _params!);
            var inf = new InferenceParams
            {
                MaxTokens = maxTokens,
                AntiPrompts = new[] { "<|im_end|>", "<end_of_turn>", "<|endoftext|>", "\nuser", "\nUser:" },
                SamplingPipeline = new DefaultSamplingPipeline { Temperature = temperature, TopP = 0.9f, RepeatPenalty = 1.1f },
            };
            await foreach (var piece in exec.InferAsync(prompt, inf, ct))
                yield return piece;
        }
        finally { Gate.Release(); ScheduleUnload(); }
    }

    /// <summary>Ответ целиком (для коротких служебных запросов: перевод, исправление, подсказка).</summary>
    public static async Task<string> AskAsync(IReadOnlyList<(string role, string text)> messages, int maxTokens = 120, float temperature = 0.3f, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (var p in ChatAsync(messages, maxTokens, temperature, ct)) sb.Append(p);
        return Clean(sb.ToString());
    }

    /// <summary>Убирает служебные метки шаблона и лишние пробелы.</summary>
    public static string Clean(string s)
    {
        foreach (var t in new[] { "<|im_end|>", "<end_of_turn>", "<|endoftext|>", "<start_of_turn>", "<|im_start|>", "assistant\n", "model\n" })
            s = s.Replace(t, "");
        return s.Trim();
    }
}
