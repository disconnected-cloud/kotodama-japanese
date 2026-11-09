using System.IO;
using System.Text;
using Matsubakira.Platform;

namespace Matsubakira.Services;

public sealed record SpeechCheck(bool Ok, int Score, string Heard, string Verdict, string? Error = null, string HeardKana = "", string[]? Hints = null)
{
    /// <summary>Тот же результат с другим баллом (вердикт пересчитывается).</summary>
    public SpeechCheck WithScore(int score) => this with { Score = score, Ok = score >= 70, Verdict = Speech.VerdictFor(score, Heard) };
}

/// <summary>
/// Единая точка для звука: озвучка фраз (нейро-голос с кэшем или офлайн VOICEVOX), воспроизведение своих записей
/// и оценка произношения. Звук играется через свой плеер (Windows — winmm, Linux — PulseAudio/PipeWire/ALSA),
/// медленный режим — замедление без изменения высоты голоса.
/// </summary>
public static class Speech
{
    static readonly AudioPlayer Player = new();
    static int _gen;
    static bool _playing;

    public static event Action<bool>? PlayingChanged;

    public static bool TtsAvailable => true;
    public static string VoiceName => ProgressStore.P.MaleVoice ? "Keita (мужской, нейросеть)" : "Nanami (женский, нейросеть)";

    public static void Init() { }

    public static void Stop()
    {
        _gen++;
        Player.Stop();
        SetPlaying(false);
    }

    static void SetPlaying(bool on)
    {
        if (_playing == on) return;
        _playing = on;
        PlayingChanged?.Invoke(on);
    }

    /// <summary>Произнести (fire-and-forget). slow — медленный темп для разбора.</summary>
    public static async void Say(string text, bool slow = false) { try { await SayAsync(text, slow); } catch { } }

    /// <summary>Произнести и дождаться окончания (нужно для диалогов).</summary>
    public static async Task<bool> SayAsync(string text, bool slow = false, bool? male = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        int my = ++_gen;
        // Онлайн-сервис игнорирует темп, поэтому медленный режим делает плеер; VOICEVOX синтезирует медленную речь сам.
        bool synthSlow = slow && Tts.UseOffline;
        var path = await Tts.GetAsync(text, synthSlow, male);
        if (my != _gen || path == null) return false;
        return await PlayFileAsync(path, my, slow && !synthSlow ? 0.72 : 1.0);
    }

    public static Task<bool> PlayFileAsync(string path, int? gen = null) => PlayFileAsync(path, gen ?? ++_gen, 1.0);

    static async Task<bool> PlayFileAsync(string path, int gen, double speed)
    {
        float[] x; int rate;
        try
        {
            (x, rate) = await Task.Run(() =>
            {
                var (d, r) = Pcm.Decode(path);
                return (Pcm.TimeStretch(d, r, speed), r);
            });
        }
        catch { return false; }
        return await PlayPcmAsync(x, rate, gen);
    }

    /// <summary>Воспроизвести свою запись прямо из памяти.</summary>
    public static async Task<bool> PlayWavAsync(byte[] wav)
    {
        int my = ++_gen;
        var (x, rate) = Pcm.Decode(wav, true);
        return await PlayPcmAsync(x, rate, my);
    }

    static async Task<bool> PlayPcmAsync(float[] x, int rate, int gen)
    {
        if (gen != _gen || x.Length == 0) return false;
        SetPlaying(true);
        try { return await Player.PlayAsync(x, rate); }
        finally { if (gen == _gen) SetPlaying(false); }
    }

    public static void OpenMicPrivacy()
    {
        if (!OperatingSystem.IsWindows()) return;
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
