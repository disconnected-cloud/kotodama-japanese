using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matsubakira.Services;

public sealed record VoiceStyle(uint Id, string Character, string Style, string Pack)
{
    /// <summary>Имя латиницей: «Shikoku Metan · Sweet», «Female 1 (женский)».</summary>
    public string Title => Style == "ノーマル" ? VoiceNames.Name(Character) : $"{VoiceNames.Name(Character)} · {VoiceNames.Style(Style)}";
    public string Original => Style == "ノーマル" ? Character : $"{Character} · {Style}";
}

/// <summary>Транскрипция имён персонажей и стилей VOICEVOX латиницей.</summary>
public static class VoiceNames
{
    static readonly Dictionary<string, string> Names = new()
    {
        ["四国めたん"] = "Shikoku Metan", ["ずんだもん"] = "Zundamon", ["春日部つむぎ"] = "Kasukabe Tsumugi", ["雨晴はう"] = "Amehare Hau",
        ["冥鳴ひまり"] = "Meimei Himari", ["九州そら"] = "Kyushu Sora", ["波音リツ"] = "Namine Ritsu", ["中国うさぎ"] = "Chugoku Usagi",
        ["玄野武宏"] = "Kurono Takehiro (муж.)", ["剣崎雌雄"] = "Kenzaki Mesuo (муж.)", ["後鬼"] = "Goki", ["白上虎太郎"] = "Shirakami Kotaro (муж.)",
        ["ちび式じい"] = "Chibishiki Jii (муж.)", ["櫻歌ミコ"] = "Ouka Miko", ["ナースロボ＿タイプＴ"] = "Nurse Robo Type T",
        ["†聖騎士 紅桜†"] = "Holy Knight Benizakura (муж.)", ["雀松朱司"] = "Wakamatsu Akashi (муж.)", ["麒ヶ島宗麟"] = "Kigashima Sourin (муж.)",
        ["春歌ナナ"] = "Haruka Nana", ["猫使アル"] = "Nekotsuka Aru", ["猫使ビィ"] = "Nekotsuka Bi", ["栗田まろん"] = "Kurita Maron (муж.)",
        ["あいえるたん"] = "Aierutan", ["満別花丸"] = "Manbetsu Hanamaru", ["琴詠ニア"] = "Kotoyomi Nia", ["青山龍星"] = "Aoyama Ryusei (муж.)",
        ["もち子さん"] = "Mochiko-san", ["小夜/SAYO"] = "Sayo", ["ぞん子"] = "Zonko", ["中部つるぎ"] = "Chubu Tsurugi", ["離途"] = "Rito (муж.)",
        ["黒沢冴白"] = "Kurosawa Kohaku (муж.)", ["ユーレイちゃん"] = "Yurei-chan", ["東北ずん子"] = "Tohoku Zunko", ["東北きりたん"] = "Tohoku Kiritan",
        ["東北イタコ"] = "Tohoku Itako", ["あんこもん"] = "Ankomon", ["夜語トバリ"] = "Yogatari Tobari", ["暁記ミタマ"] = "Akatsuki Mitama",
        ["里石ユカ"] = "Satoishi Yuka", ["ブイブイ・コバコ"] = "Buibui Kobako",
        ["女声1"] = "Female 1 (женский)", ["女声2"] = "Female 2 (женский)", ["女声3"] = "Female 3 (женский)", ["女声4"] = "Female 4 (женский)",
        ["女声5"] = "Female 5 (женский)", ["女声6"] = "Female 6 (женский)", ["男声1"] = "Male 1 (мужской)", ["男声2"] = "Male 2 (мужской)", ["男声3"] = "Male 3 (мужской)",
    };
    static readonly Dictionary<string, string> Styles = new()
    {
        ["ノーマル"] = "Normal", ["あまあま"] = "Sweet", ["ツンツン"] = "Tsun", ["セクシー"] = "Sexy", ["ささやき"] = "Whisper", ["ヒソヒソ"] = "Hushed",
        ["ヘロヘロ"] = "Tired", ["なみだめ"] = "Teary", ["喜び"] = "Joy", ["怒り"] = "Angry", ["悲しみ"] = "Sad", ["ふつう"] = "Normal",
        ["熱血"] = "Passionate", ["不機嫌"] = "Grumpy", ["わーい"] = "Yay", ["びくびく"] = "Scared", ["おこ"] = "Mad", ["びえーん"] = "Crying",
        ["人間ver."] = "Human", ["ぬいぐるみver."] = "Plush", ["人間（怒り）ver."] = "Human angry", ["鬼ver."] = "Demon", ["しっとり"] = "Calm",
        ["かなしみ"] = "Sad", ["囁き"] = "Whisper", ["楽々"] = "Easy", ["恐怖"] = "Fear", ["内緒話"] = "Secret", ["アナウンス"] = "Announcer",
        ["読み聞かせ"] = "Storyteller", ["第二形態"] = "Form 2", ["ロリ"] = "Young", ["ぶりっ子"] = "Cutesy", ["ボーイ"] = "Boy", ["元気"] = "Cheerful",
        ["低血圧"] = "Sleepy", ["覚醒"] = "Awake", ["実況風"] = "Commentator", ["おちつき"] = "Calm", ["うきうき"] = "Excited", ["人見知り"] = "Shy",
        ["セクシー／あん子"] = "Sexy", ["泣き"] = "Crying", ["ひそひそ"] = "Hushed",
    };
    public static string Name(string jp) => Names.TryGetValue(jp, out var n) ? n : Translit(jp);
    public static string Style(string jp) => Styles.TryGetValue(jp, out var n) ? n : Translit(jp);
    static string Translit(string s)
    {
        var r = Kana.ToRomaji(s);
        return r.Length > 0 && char.IsLetter(r[0]) ? char.ToUpperInvariant(r[0]) + r[1..] : s;
    }
}
public sealed record VoicePack(string File, long Size, (string Name, (string Style, uint Id)[] Styles)[] Chars);

/// <summary>
/// Офлайн-синтез японской речи VOICEVOX (бесплатный, ~100 голосов). Компоненты скачиваются из официальных
/// релизов VOICEVOX на GitHub при первом использовании: ядро, ONNX Runtime, словарь Open JTalk и голосовые пакеты.
/// Условия: при публикации озвучки нужна подпись «VOICEVOX:имя персонажа».
/// </summary>
public static class Voicevox
{
    // Сборки ядра и ONNX Runtime под свою ОС (Windows — .dll, Linux — .so)
    static readonly bool Win = OperatingSystem.IsWindows();
    static string CoreUrl => $"https://github.com/VOICEVOX/voicevox_core/releases/download/0.17.1/voicevox_core-{(Win ? "windows" : "linux")}-x64-0.17.1.zip";
    static string OrtUrl => $"https://github.com/VOICEVOX/onnxruntime-builder/releases/download/voicevox_onnxruntime-1.23.2/voicevox_onnxruntime-{(Win ? "win" : "linux")}-x64-1.23.2.tgz";
    static string LibExt => Win ? ".dll" : ".so";
    const string DictUrl = "https://github.com/r9y9/open_jtalk/releases/download/v1.11.1/open_jtalk_dic_utf_8-1.11.tar.gz";
    const string VvmUrl = "https://github.com/VOICEVOX/voicevox_vvm/releases/download/0.17.1/";

    public static readonly string Dir = Path.Combine(ProgressStore.Folder, "voicevox");
    static string CoreDll => Path.Combine(Dir, "core", Win ? "voicevox_core.dll" : "libvoicevox_core.so");
    static string? OrtDll => Directory.Exists(Path.Combine(Dir, "ort"))
        ? Directory.EnumerateFiles(Path.Combine(Dir, "ort"), Win ? "voicevox_onnxruntime*.dll" : "libvoicevox_onnxruntime*.so*", SearchOption.AllDirectories).FirstOrDefault(f => !f.Contains("providers"))
        : null;
    static string? DictDir => Directory.Exists(Path.Combine(Dir, "dict"))
        ? Directory.EnumerateFiles(Path.Combine(Dir, "dict"), "sys.dic", SearchOption.AllDirectories).Select(Path.GetDirectoryName).FirstOrDefault()
        : null;
    static string ModelsDir => Path.Combine(Dir, "models");

    public static bool BaseInstalled => File.Exists(CoreDll) && OrtDll != null && DictDir != null;
    /// <summary>Установлен и прошёл проверку в отдельном процессе (несовместимая библиотека не уронит приложение).</summary>
    public static bool Installed => BaseInstalled && InstalledPacks.Count > 0 && ProgressStore.P.Flags.Contains("vv-ok");
    public static bool Downloaded => BaseInstalled && InstalledPacks.Count > 0;

    /// <summary>Пробный синтез в дочернем процессе: Matsubakira.exe --vv-check. Код 0 — всё работает.</summary>
    public static async Task<(bool ok, string message)> SelfCheckAsync()
    {
        try
        {
            var resultFile = Path.Combine(Dir, "check.txt");
            try { File.Delete(resultFile); } catch { }
            var psi = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--vv-check") { UseShellExecute = false, CreateNoWindow = true };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try { await proc.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { try { proc.Kill(); } catch { } return (false, "проверка зависла"); }
            var output = File.Exists(resultFile) ? await File.ReadAllTextAsync(resultFile) : "";
            bool ok = proc.ExitCode == 0;
            // прогресс меняется только в потоке интерфейса (проверка идёт в фоне)
            void Apply() { if (ok) ProgressStore.P.Flags.Add("vv-ok"); else ProgressStore.P.Flags.Remove("vv-ok"); ProgressStore.Notify(); }
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Apply(); else Avalonia.Threading.Dispatcher.UIThread.Post(Apply);
            return (ok, ok ? "VOICEVOX работает" : "VOICEVOX не запустился: " + (output.Trim().Length > 0 ? output.Trim() : $"код {proc.ExitCode}"));
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <summary>Вызывается в дочернем процессе проверки.</summary>
    public static int RunSelfCheck()
    {
        string msg; int code;
        try
        {
            var wav = Synthesize("こんにちは。今日はいい天気ですね。", PickStyleRaw());
            code = wav is { Length: > 1000 } ? 0 : 1;
            msg = code == 0 ? "ok" : LastError ?? "пустой результат";
        }
        catch (Exception ex) { code = 2; msg = ex.Message; }
        try { File.WriteAllText(Path.Combine(Dir, "check.txt"), msg); } catch { }
        return code;
    }

    static uint PickStyleRaw()
    {
        var inst = InstalledPacks;
        var st = Catalog.Where(p => inst.Contains(p.File)).SelectMany(p => p.Chars.SelectMany(c => c.Styles)).FirstOrDefault();
        return st.Id;
    }
    public static List<string> InstalledPacks => Directory.Exists(ModelsDir) ? Directory.EnumerateFiles(ModelsDir, "*.vvm").Select(Path.GetFileName).OfType<string>().ToList() : new();
    public static string? LastError { get; private set; }

    // ─────────── Каталог голосов (встроен: Data/voicevox.json) ───────────
    static readonly Lazy<VoicePack[]> _catalog = new(() =>
    {
        using var s = typeof(Voicevox).Assembly.GetManifestResourceStream("voicevox.json")!;
        using var doc = JsonDocument.Parse(s);
        return doc.RootElement.EnumerateArray().Select(p => new VoicePack(
            p.GetProperty("file").GetString()!, p.GetProperty("size").GetInt64(),
            p.GetProperty("chars").EnumerateArray().Select(c => (c.GetProperty("name").GetString()!,
                c.GetProperty("styles").EnumerateArray().Select(st => (st.GetProperty("name").GetString()!, st.GetProperty("id").GetUInt32())).ToArray())).ToArray()
        )).ToArray();
    });
    public static VoicePack[] Catalog => _catalog.Value;

    public static List<VoiceStyle> InstalledStyles()
    {
        var inst = InstalledPacks;
        return Catalog.Where(p => inst.Contains(p.File))
            .SelectMany(p => p.Chars.SelectMany(c => c.Styles.Select(s => new VoiceStyle(s.Id, c.Name, s.Style, p.File))))
            .GroupBy(v => v.Id).Select(g => g.First()).ToList();
    }

    static readonly string[] MaleNames = { "玄野武宏", "青山龍星", "白上虎太郎", "雀松朱司", "剣崎雌雄", "麒ヶ島宗麟", "離途", "黒沢冴白", "男声1", "男声2", "男声3", "ちび式じい" };

    /// <summary>
    /// Чёткие голоса для учёбы: ровная дикция, без придыхания, шёпота и эмоций. Порядок — от лучшего.
    /// Голоса 女声/男声 (пакет n0) звучат с придыханием, поэтому идут последними.
    /// </summary>
    static readonly uint[] ClearFemale = { 8, 30, 14, 16, 2, 107, 109, 29, 31, 10, 20, 54, 46, 23, 74, 90, 94, 118, 122, 10005, 10007, 10004 };
    static readonly uint[] ClearMale = { 11, 13, 12, 52, 21, 53, 99, 100, 10001, 10000, 10002 };
    public static readonly HashSet<uint> Clear = ClearFemale.Concat(ClearMale).ToHashSet();
    /// <summary>Рекомендуемый набор пакетов: 7 чётких голосов (женские и мужские) ≈ 300 МБ.</summary>
    public static readonly string[] Recommended = { "0.vvm", "1.vvm", "4.vvm", "6.vvm", "15.vvm" };
    public static bool IsMale(VoiceStyle s) => MaleNames.Contains(s.Character);

    /// <summary>Стиль для озвучки: выбранный пользователем, а для мужских ролей — чёткий мужской голос, если установлен.</summary>
    public static uint PickStyle(bool male)
    {
        var styles = InstalledStyles();
        if (styles.Count == 0) return 0;
        var ids = styles.Select(s => s.Id).ToHashSet();
        if (male)
        {
            foreach (var id in ClearMale) if (ids.Contains(id)) return id;
            var m = styles.FirstOrDefault(IsMale);
            if (m != null) return m.Id;
        }
        var chosen = ProgressStore.P.OfflineStyleId >= 0 ? styles.FirstOrDefault(s => s.Id == (uint)ProgressStore.P.OfflineStyleId) : null;
        if (chosen != null) return chosen.Id;
        foreach (var id in ClearFemale) if (ids.Contains(id)) return id;
        return styles[0].Id;
    }

    // ─────────── Установка ───────────
    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    static async Task<byte[]> Get(string url, IProgress<double>? p, double from, double to, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? 1;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        var ms = new MemoryStream();
        var buf = new byte[1 << 16]; int n; long done = 0;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            ms.Write(buf, 0, n); done += n;
            p?.Report(from + (to - from) * done / Math.Max(1, total));
        }
        return ms.ToArray();
    }

    static void ExtractTgz(byte[] data, string dest)
    {
        Directory.CreateDirectory(dest);
        using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gz, dest, overwriteFiles: true);
    }

    /// <summary>Скачивает ядро, ONNX Runtime и словарь (~30 МБ) и выбранные голосовые пакеты.</summary>
    public static async Task InstallAsync(IEnumerable<string> packs, IProgress<(double v, string what)> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Dir);
        var list = packs.Where(p => !InstalledPacks.Contains(p)).ToList();
        int steps = (BaseInstalled ? 0 : 3) + list.Count;
        int step = 0;
        IProgress<double> P(string what) => new Progress<double>(v => progress.Report(((step + v) / Math.Max(1, steps), what)));

        if (!BaseInstalled)
        {
            var core = await Get(CoreUrl, P("ядро VOICEVOX"), 0, 1, ct); step++;
            var coreDir = Path.Combine(Dir, "core"); Directory.CreateDirectory(coreDir);
            using (var zip = new ZipArchive(new MemoryStream(core)))
                foreach (var e in zip.Entries.Where(e => e.Name.EndsWith(LibExt, StringComparison.OrdinalIgnoreCase)))
                    e.ExtractToFile(Path.Combine(coreDir, e.Name), true);
            var ort = await Get(OrtUrl, P("ONNX Runtime"), 0, 1, ct); step++;
            ExtractTgz(ort, Path.Combine(Dir, "ort"));
            var dict = await Get(DictUrl, P("словарь произношения"), 0, 1, ct); step++;
            ExtractTgz(dict, Path.Combine(Dir, "dict"));
        }
        Directory.CreateDirectory(ModelsDir);
        foreach (var pack in list)
        {
            var data = await Get(VvmUrl + pack, P("голоса " + pack), 0, 1, ct); step++;
            await File.WriteAllBytesAsync(Path.Combine(ModelsDir, pack), data, ct);
        }
        progress.Report((1, "готово"));
        Reset();
    }

    // ─────────── Нативный API (voicevox_core 0.17) ───────────
    [StructLayout(LayoutKind.Sequential)] struct LoadOrtOptions { public IntPtr filename; }
    [StructLayout(LayoutKind.Sequential)] struct InitOptions { public int acceleration_mode; public ushort cpu_num_threads; }
    [StructLayout(LayoutKind.Sequential)] struct LoadModelOptions { public int on_conflict; }
    [StructLayout(LayoutKind.Sequential)] struct SynthOptions { public byte enable_interrogative_upspeak; }

    const string Lib = "voicevox_core";
    [DllImport(Lib)] static extern LoadOrtOptions voicevox_make_default_load_onnxruntime_options();
    [DllImport(Lib)] static extern int voicevox_onnxruntime_load_once(LoadOrtOptions options, out IntPtr ort);
    [DllImport(Lib)] static extern int voicevox_open_jtalk_rc_new(byte[] dictDir, out IntPtr rc);
    [DllImport(Lib)] static extern InitOptions voicevox_make_default_initialize_options();
    [DllImport(Lib)] static extern int voicevox_synthesizer_new(IntPtr ort, IntPtr rc, InitOptions options, out IntPtr synth);
    [DllImport(Lib)] static extern int voicevox_voice_model_file_open(byte[] path, out IntPtr model);
    [DllImport(Lib)] static extern void voicevox_voice_model_file_delete(IntPtr model);
    [DllImport(Lib)] static extern LoadModelOptions voicevox_make_default_load_voice_model_options();
    [DllImport(Lib)] static extern int voicevox_synthesizer_load_voice_model(IntPtr synth, IntPtr model, LoadModelOptions options);
    [DllImport(Lib)] static extern int voicevox_synthesizer_create_audio_query(IntPtr synth, byte[] text, uint style, out IntPtr json);
    [DllImport(Lib)] static extern int voicevox_synthesizer_create_accent_phrases(IntPtr synth, byte[] text, uint style, out IntPtr json);
    [DllImport(Lib)] static extern SynthOptions voicevox_make_default_synthesis_options();
    [DllImport(Lib)] static extern int voicevox_synthesizer_synthesis(IntPtr synth, byte[] query, uint style, SynthOptions options, out UIntPtr len, out IntPtr wav);
    [DllImport(Lib)] static extern void voicevox_json_free(IntPtr json);
    [DllImport(Lib)] static extern void voicevox_wav_free(IntPtr wav);
    [DllImport(Lib)] static extern IntPtr voicevox_error_result_to_message(int code);

    static bool _resolverSet;
    static IntPtr _synth;
    static readonly object Gate = new();
    static readonly HashSet<string> _loadedPacks = new();

    static byte[] U8(string s) => Encoding.UTF8.GetBytes(s + "\0");
    static void Check(int code, string what)
    {
        if (code == 0) return;
        var msg = Marshal.PtrToStringUTF8(voicevox_error_result_to_message(code));
        throw new InvalidOperationException($"{what}: {msg} ({code})");
    }

    static void Reset() { lock (Gate) _loadedPacks.Clear(); }

    /// <summary>Готовит синтезатор и загружает только пакет с нужным голосом (а не все ~60 МБ каждый).</summary>
    static void EnsureReady(uint style)
    {
        if (!BaseInstalled) throw new InvalidOperationException("VOICEVOX не установлен");
        if (!_resolverSet)
        {
            NativeLibrary.SetDllImportResolver(typeof(Voicevox).Assembly, (name, asm, path) =>
                name == Lib ? NativeLibrary.Load(CoreDll) : IntPtr.Zero);
            _resolverSet = true;
        }
        if (_synth == IntPtr.Zero)
        {
            var opt = voicevox_make_default_load_onnxruntime_options();
            var ortPath = Marshal.StringToCoTaskMemUTF8(OrtDll!);
            opt.filename = ortPath;
            Check(voicevox_onnxruntime_load_once(opt, out var ort), "ONNX Runtime");
            Check(voicevox_open_jtalk_rc_new(U8(DictDir!), out var rc), "Open JTalk");
            var init = voicevox_make_default_initialize_options();
            init.acceleration_mode = 1; // CPU: стабильно на любом ПК
            init.cpu_num_threads = (ushort)Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
            Check(voicevox_synthesizer_new(ort, rc, init, out _synth), "синтезатор");
        }
        var installed = InstalledPacks;
        var need = Catalog.Where(p => installed.Contains(p.File) && p.Chars.Any(c => c.Styles.Any(s => s.Id == style))).Select(p => p.File).Take(1).ToList();
        if (need.Count == 0) need = installed.Take(1).ToList();
        foreach (var pack in need.Where(p => !_loadedPacks.Contains(p)))
        {
            Check(voicevox_voice_model_file_open(U8(Path.Combine(ModelsDir, pack)), out var model), "модель " + pack);
            var lo = voicevox_make_default_load_voice_model_options();
            lo.on_conflict = 2; // SKIP: пакет уже загружен (например, после докачки новых) — не ошибка
            try { Check(voicevox_synthesizer_load_voice_model(_synth, model, lo), "загрузка " + pack); }
            finally { voicevox_voice_model_file_delete(model); }
            _loadedPacks.Add(pack);
        }
    }

    /// <summary>Синтез WAV. speed — множитель темпа (0.7 = медленно). Потокобезопасно.</summary>
    public static byte[]? Synthesize(string text, uint style, double speed = 1.0)
    {
        try
        {
            lock (Gate)
            {
                EnsureReady(style);
                var node = Query(text, style);
                // Меняем только существующие поля — формат AudioQuery строгий
                void Set(string key, double v) { if (node[key] != null) node[key] = v; }
                Set("speedScale", speed);
                Set("prePhonemeLength", 0.1);
                Set("postPhonemeLength", 0.2);
                Set("intonationScale", 1.0);   // естественная мелодика: завышенная даёт «подвывание» на гласных
                Set("volumeScale", 1.0);
                Check(voicevox_synthesizer_synthesis(_synth, U8(node.ToJsonString()), style, voicevox_make_default_synthesis_options(), out var len, out var wav), "синтез");
                var bytes = new byte[(int)len];
                Marshal.Copy(wav, bytes, 0, bytes.Length);
                voicevox_wav_free(wav);
                LastError = null;
                return Clean(bytes);
            }
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    static JsonNode RawQuery(string text, uint style)
    {
        Check(voicevox_synthesizer_create_audio_query(_synth, U8(text), style, out var qPtr), "разбор текста");
        string q = Marshal.PtrToStringUTF8(qPtr)!;
        voicevox_json_free(qPtr);
        return JsonNode.Parse(q)!;
    }

    /// <summary>
    /// AudioQuery с проверкой чтения: словарь OpenJTalk иногда читает кандзи не так (何 → なに вместо なん,
    /// 今日 → こんにち, 一人 → いちにん). Если фраза есть в материалах и чтение не совпало с правильной каной —
    /// озвучиваем по кане (частицы は/へ — как わ/え).
    /// </summary>
    static JsonNode Query(string text, uint style)
    {
        var node = RawQuery(text, style);
        if (FixedText(text, node) is { } kana) node = RawQuery(kana, style);
        return node;
    }

    static string? FixedText(string text, JsonNode query)
    {
        if (!text.Any(Reading.IsKanji) || Db.KanaFor(text) is not { } kana) return null;
        var heard = string.Concat(query["accent_phrases"]!.AsArray().SelectMany(ph => ph!["moras"]!.AsArray()).Select(m => m!["text"]!.GetValue<string>()));
        if (Reading.Phon(heard) == Reading.Phon(kana.Replace(" ", ""))) return null;
        return SpeakableKana(kana);
    }

    /// <summary>Границы мор в синтезированной фразе (секунды) — по ним можно проиграть один слог образца.</summary>
    public static List<(string kana, double start, double end)>? MoraTimes(string text, bool male, double speed = 1.0)
    {
        try
        {
            lock (Gate)
            {
                uint style = PickStyle(male);
                EnsureReady(style);
                var node = Query(text, style);
                double t = 0.1; // prePhonemeLength, как в Synthesize
                var res = new List<(string, double, double)>();
                static double L(JsonNode? n) => n == null || n.GetValueKind() == JsonValueKind.Null ? 0 : n.GetValue<double>();
                foreach (var ph in node["accent_phrases"]!.AsArray())
                {
                    foreach (var m in ph!["moras"]!.AsArray())
                    {
                        double d = L(m!["consonant_length"]) + L(m["vowel_length"]);
                        res.Add((m["text"]!.GetValue<string>(), t / speed, (t + d) / speed));
                        t += d;
                    }
                    if (ph["pause_mora"] is JsonNode pm && pm.GetValueKind() != JsonValueKind.Null) t += L(pm["vowel_length"]);
                }
                return res;
            }
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    /// <summary>Отладка: фразы, где словарь VOICEVOX читает кандзи иначе, чем в материалах.</summary>
    public static List<string> ReadingMismatches(IEnumerable<string> texts)
    {
        var res = new List<string>();
        lock (Gate)
        {
            uint style = PickStyle(false);
            EnsureReady(style);
            foreach (var t in texts)
            {
                var q = RawQuery(t, style);
                if (FixedText(t, q) is { } k)
                    res.Add($"{t} | vv: {string.Concat(q["accent_phrases"]!.AsArray().SelectMany(ph => ph!["moras"]!.AsArray()).Select(m => m!["text"]!.GetValue<string>()))} | надо: {k}");
            }
        }
        return res;
    }

    /// <summary>Кана для озвучки: отдельные частицы は/へ читаются как わ/え, пробелы убираются.</summary>
    public static string SpeakableKana(string kana) =>
        string.Concat(kana.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t switch { "は" => "わ", "へ" => "え", _ => t }));

    /// <summary>
    /// Чистка звука: убираем постоянную составляющую и низкочастотный гул (фильтр 70 Гц),
    /// мягко срезаем резкие «с/ш» выше 9 кГц, выравниваем громкость (пик −1.5 дБ) и делаем
    /// плавные края, чтобы не было щелчков в начале и конце.
    /// </summary>
    public static byte[] Clean(byte[] wav)
    {
        try
        {
            var (x, rate) = Platform.Pcm.Decode(wav, true);
            if (x.Length < rate / 20) return wav;
            // ВЧ-фильтр 1-го порядка (≈70 Гц)
            double rc = 1.0 / (2 * Math.PI * 70), dt = 1.0 / rate, a = rc / (rc + dt);
            double prevX = x[0], prevY = 0;
            for (int i = 0; i < x.Length; i++) { double y = a * (prevY + x[i] - prevX); prevX = x[i]; prevY = y; x[i] = (float)y; }
            // НЧ-фильтр 1-го порядка (≈9 кГц) — убирает «песок» и свист на шипящих
            if (rate > 20000)
            {
                double rc2 = 1.0 / (2 * Math.PI * 9000), b = dt / (rc2 + dt), z = x[0];
                for (int i = 0; i < x.Length; i++) { z += b * (x[i] - z); x[i] = (float)z; }
            }
            int fade = rate / 125; // 8 мс
            float peak = 0;
            for (int i = fade; i < x.Length - fade; i++) peak = Math.Max(peak, Math.Abs(x[i])); // края всё равно гасятся
            float gain = peak > 1e-4f ? Math.Min(4f, 0.84f / peak) : 1f;
            for (int i = 0; i < x.Length; i++)
            {
                float g = gain;
                if (i < fade) g *= (float)i / fade;
                else if (i > x.Length - fade) g *= (float)(x.Length - i) / fade;
                x[i] *= g;
            }
            return Platform.Pcm.ToWav(x, rate);
        }
        catch { return wav; }
    }

    /// <summary>
    /// Питч-акцент фразы: для каждой моры — кана и высота тона (из модели VOICEVOX).
    /// Используется для подсказки интонации над текстом.
    /// </summary>
    public static List<(string kana, double pitch, bool accentEnd)>? Accent(string text)
    {
        try
        {
            lock (Gate)
            {
                uint style = PickStyle(false);
                EnsureReady(style);
                if (FixedText(text, RawQuery(text, style)) is { } fixedKana) text = fixedKana;
                Check(voicevox_synthesizer_create_accent_phrases(_synth, U8(text), style, out var p), "акцент");
                var json = Marshal.PtrToStringUTF8(p)!;
                voicevox_json_free(p);
                var res = new List<(string, double, bool)>();
                foreach (var ph in JsonNode.Parse(json)!.AsArray())
                {
                    int accent = ph!["accent"]!.GetValue<int>();
                    int i = 0;
                    foreach (var m in ph["moras"]!.AsArray())
                    {
                        i++;
                        res.Add((m!["text"]!.GetValue<string>(), m["pitch"]!.GetValue<double>(), i == accent));
                    }
                    if (ph["pause_mora"] is JsonNode pm && pm.GetValueKind() != JsonValueKind.Null) res.Add(("、", 0, false));
                }
                return res;
            }
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }
}
