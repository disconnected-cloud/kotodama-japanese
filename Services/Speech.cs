using System.IO;
using System.Text;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Matsubakira.Services;

public sealed record SpeechCheck(bool Ok, int Score, string Heard, string Verdict, string? Error = null, string HeardKana = "", string[]? Hints = null)
{
    /// <summary>Тот же результат с другим баллом (вердикт пересчитывается).</summary>
    public SpeechCheck WithScore(int score) => this with { Score = score, Ok = score >= 70, Verdict = Speech.VerdictFor(score, Heard) };
}

/// <summary>
/// Единая точка для звука: озвучка фраз (нейро-голос с кэшем → голос Windows, если есть),
/// воспроизведение своих записей и оценка произношения по распознанному тексту.
/// </summary>
public static class Speech
{
    static MediaPlayer? _player;
    static TaskCompletionSource<bool>? _playing;
    static int _gen;
    static SpeechSynthesizer? _winSynth;
    static bool _initDone;

    public static event Action<bool>? PlayingChanged;

    /// <summary>Озвучка доступна всегда, когда есть сеть или кэш.</summary>
    public static bool TtsAvailable => true;
    public static string VoiceName => ProgressStore.P.MaleVoice ? "Keita (мужской, нейросеть)" : "Nanami (женский, нейросеть)";

    public static void Init()
    {
        if (_initDone) return;
        _initDone = true;
        try
        {
            var v = SpeechSynthesizer.AllVoices.FirstOrDefault(x => x.Language.StartsWith("ja", StringComparison.OrdinalIgnoreCase));
            if (v != null) _winSynth = new SpeechSynthesizer { Voice = v };
        }
        catch { }
    }

    static MediaPlayer Player()
    {
        if (_player != null) return _player;
        _player = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Speech };
        _player.MediaEnded += (_, _) => Done(true);
        _player.MediaFailed += (_, _) => Done(false);
        return _player;
    }

    static void Done(bool ok)
    {
        var t = _playing; _playing = null;
        t?.TrySetResult(ok);
        PlayingChanged?.Invoke(false);
    }

    public static void Stop()
    {
        _gen++;
        try { _player?.Pause(); } catch { }
        Done(false);
    }

    /// <summary>Произнести (fire-and-forget). slow — медленный темп для разбора.</summary>
    public static async void Say(string text, bool slow = false) => await SayAsync(text, slow);

    /// <summary>Произнести и дождаться окончания (нужно для диалогов).</summary>
    public static async Task<bool> SayAsync(string text, bool slow = false, bool? male = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        int my = ++_gen;
        // Онлайн-сервис игнорирует темп в SSML, поэтому медленный режим делает проигрыватель (с сохранением высоты голоса);
        // офлайн VOICEVOX синтезирует медленную речь сам.
        bool synthSlow = slow && Tts.UseOffline;
        var path = await Tts.GetAsync(text, synthSlow, male);
        if (my != _gen) return false; // пользователь уже запросил другую фразу
        if (path != null) return await PlaySourceAsync(MediaSource.CreateFromUri(new Uri(path)), my, slow && !synthSlow ? 0.7 : 1.0);

        // Нет сети и кэша — пробуем системный японский голос, если он всё-таки установлен
        Init();
        if (_winSynth == null) return false;
        try
        {
            _winSynth.Options.SpeakingRate = slow ? 0.65 : 0.95;
            var stream = await _winSynth.SynthesizeTextToStreamAsync(text);
            return await PlaySourceAsync(MediaSource.CreateFromStream(stream, stream.ContentType), my);
        }
        catch { return false; }
    }

    public static Task<bool> PlayFileAsync(string path, int? gen = null) =>
        PlaySourceAsync(MediaSource.CreateFromUri(new Uri(path)), gen ?? ++_gen);

    /// <summary>Воспроизвести свою запись прямо из памяти — без временных файлов (раньше файл мог быть занят плеером).</summary>
    public static async Task<bool> PlayWavAsync(byte[] wav)
    {
        try
        {
            var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var dw = new Windows.Storage.Streams.DataWriter(ras))
            {
                dw.WriteBytes(wav);
                await dw.StoreAsync();
                dw.DetachStream();
            }
            ras.Seek(0);
            return await PlaySourceAsync(MediaSource.CreateFromStream(ras, "audio/wav"), ++_gen);
        }
        catch { return false; }
    }

    static Task<bool> PlaySourceAsync(MediaSource src, int gen, double rate = 1.0)
    {
        if (gen != _gen) return Task.FromResult(false);
        Done(false);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _playing = tcs;
        try
        {
            var p = Player();
            p.Source = src;
            p.PlaybackSession.PlaybackRate = rate;
            p.Play();
            PlayingChanged?.Invoke(true);
        }
        catch { tcs.TrySetResult(false); }
        // Страховка: не ждём вечно, если событие окончания потеряется
        _ = Task.Delay(TimeSpan.FromSeconds(20)).ContinueWith(_ => tcs.TrySetResult(false));
        return tcs.Task;
    }

    public static void OpenMicPrivacy()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true }); }
        catch { }
    }

    // ─────────── Оценка произношения ───────────
    /// <summary>
    /// Сравнивает распознанное Whisper с целевой фразой. Главное — звучание: распознанный текст переводится
    /// в кану (кандзи, цифры) и сравнивается с чтением фразы по латинице, поэтому «お願いします» и «おねがいします»,
    /// «3時» и «さんじ», «こんにちわ» и «こんにちは» — одно и то же. cap ограничивает балл (защита от подсказки модели).
    /// </summary>
    public static SpeechCheck Score(string heardRaw, float confidence, string targetJp, string targetKana, int cap = 100)
    {
        heardRaw = DropHallucination(heardRaw, targetJp);
        var heard = Normalize(heardRaw);
        if (heard.Length == 0) return new SpeechCheck(false, 0, "", "Ничего не распознано — попробуй громче и ближе к микрофону.");
        var tKana = Kana.ToHiragana(Normalize(targetKana.Length > 0 ? targetKana : targetJp));
        var hKana = Reading.Of(heard, Reading.Align(targetJp, targetKana), tKana);
        double surface = Similarity(heard, Normalize(targetJp));
        string ph = Reading.Phon(hKana), pt = Reading.Phon(tKana);
        double sound = Reading.SoundSimilarity(ph, pt);
        // Whisper любит дописывать частицу или междометие в конце («ここ» → «ここは»): фраза целиком внутри — почти зачёт
        if (pt.Length >= 2 && ph.Contains(pt, StringComparison.Ordinal))
        {
            int extra = ph.Length - pt.Length;
            sound = Math.Max(sound, extra <= 3 ? 0.95 : 1 - 0.5 * extra / ph.Length);
        }
        double sim = Math.Max(surface, sound);
        double conf = confidence <= 0.001 ? 1 : 0.85 + 0.15 * Math.Clamp(confidence, 0, 1);
        int score = (int)Math.Round(100 * Math.Pow(sim, 1.3) * conf);
        if (sim > 0.999) score = Math.Max(score, 92); // полное совпадение — всегда высокий балл
        score = Math.Min(score, cap);
        var hints = sim > 0.999 ? new List<string>() : Reading.Hints(tKana, hKana);
        return new SpeechCheck(score >= 70, score, heardRaw.Trim(), Verdict(score, sim < 0.999 ? heardRaw.Trim() : null), null, hKana, hints.ToArray());
    }

    /// <summary>Whisper на тишине и шуме «досочиняет» фразы из субтитров YouTube — такие ответы отбрасываем.</summary>
    static readonly string[] Hallucinations = { "ご視聴", "チャンネル登録", "字幕", "おやすみなさい。おやすみなさい", "次回", "ありがとうございました。ありがとうございました" };
    public static string DropHallucination(string heard, string target)
    {
        foreach (var h in Hallucinations)
            if (heard.Contains(h, StringComparison.Ordinal) && !target.Contains(h, StringComparison.Ordinal)) return "";
        return heard;
    }

    public static string VerdictFor(int score, string heard) => Verdict(score, score < 90 && heard.Length > 0 ? heard : null);

    static string Verdict(int score, string? heardOther) => score switch
    {
        >= 90 => "Превосходно! Звучит как у носителя. 素晴らしい！",
        >= 75 => "Отлично! Очень близко к образцу.",
        >= 60 => "Хорошо, но есть неточности — сравни волны и послушай медленно.",
        >= 35 => heardOther != null ? $"Похоже на «{heardOther}». Обрати внимание на звуки и долготу." : "Пока неточно — повтори ещё раз.",
        _ => "Не получилось распознать фразу. Послушай медленно и попробуй ещё.",
    };

    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (!char.IsWhiteSpace(c) && !char.IsPunctuation(c) && !char.IsSymbol(c) && c is not ('。' or '、' or '？' or '！' or '・' or '〜'))
                sb.Append(c);
        return sb.ToString();
    }

    /// <summary>Нормализованная схожесть по расстоянию Левенштейна (0..1).</summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 1;
        if (a.Length == 0 || b.Length == 0) return 0;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return 1.0 - (double)prev[b.Length] / Math.Max(a.Length, b.Length);
    }
}
