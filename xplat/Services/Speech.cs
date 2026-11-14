using System.IO;
using System.Text;
using Matsubakira.Platform;

namespace Matsubakira.Services;

public sealed record SpeechCheck(bool Ok, int Score, string Heard, string Verdict, string? Error = null, string HeardKana = "", string[]? Hints = null, (string text, float p)[]? Tokens = null)
{
    /// <summary>Тот же результат с другим баллом (вердикт пересчитывается).</summary>
    public SpeechCheck WithScore(int score) => this with { Score = score, Ok = score >= Speech.PassScore, Verdict = Speech.VerdictFor(score, Heard) };
}

/// <summary>
/// Единая точка для звука: озвучка фраз (VOICEVOX или системный голос, с кэшем), воспроизведение своих записей
/// и оценка произношения. Звук играется через свой плеер (Windows — winmm, Linux — PulseAudio/PipeWire/ALSA),
/// медленный режим — замедление без изменения высоты голоса.
/// </summary>
public static class Speech
{
    static readonly AudioPlayer Player = new();
    static int _gen;
    static bool _playing;

    public static event Action<bool>? PlayingChanged;



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
    public static async void Say(string text, bool slow = false) { if (Platform.AudioPlayer.Muted) return; try { await SayAsync(text, slow); } catch (Exception e) { CrashReport.Warn("Озвучка", e); } }

    /// <summary>
    /// Отдельный знак хираганы синтезатор читает как частицу: は — «ва», へ — «э», を — «о».
    /// Одиночный слог (в т.ч. しゃ) отдаём катаканой — ハ, ヘ читаются как «ха», «хэ».
    /// </summary>
    public static string Spoken(string text)
    {
        var t = text.Trim();
        bool single = t.Length == 1 || t.Length == 2 && "ゃゅょぁぃぅぇぉ".Contains(t[1]);
        return single && t.All(c => c >= 'ぁ' && c <= 'ゖ') ? Kana.ToKatakana(t) : text;
    }

    /// <summary>Произнести и дождаться окончания (нужно для диалогов).</summary>
    public static async Task<bool> SayAsync(string text, bool slow = false, bool? male = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        int my = ++_gen;
        // Системный голос и старый кэш — медленный режим делает плеер; VOICEVOX синтезирует медленную речь сам.
        bool synthSlow = slow && Tts.UseOffline;
        var path = await Tts.GetAsync(text, synthSlow, male);
        if (my != _gen || path == null) return false;
        return await PlayFileAsync(path, my, slow && !synthSlow ? 0.72 : 1.0);
    }

    /// <summary>
    /// Длинный текст (дневник) по предложениям: первое звучит, как только готово, а следующее синтезируется,
    /// пока играет текущее — не нужно ждать озвучки всего текста. Stop() прерывает между предложениями.
    /// </summary>
    public static async void SayLong(string text, bool slow = false)
    {
        if (Platform.AudioPlayer.Muted) return;
        var parts = System.Text.RegularExpressions.Regex.Split(text, @"(?<=[。！？!?
])").Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (parts.Count <= 1) { Say(text, slow); return; }
        try
        {
            bool synthSlow = slow && Tts.UseOffline;
            int start = ++_gen;
            var next = Tts.GetAsync(parts[0], synthSlow);
            for (int i = 0; i < parts.Count; i++)
            {
                var path = await next;
                if (i + 1 < parts.Count) next = Tts.GetAsync(parts[i + 1], synthSlow);
                if (_gen != start && i > 0) return; // нажали «стоп» или зазвучало другое
                if (path == null) continue;
                start = ++_gen;
                if (!await PlayFileAsync(path, start, slow && !synthSlow ? 0.72 : 1.0)) return;
            }
        }
        catch (Exception e) { CrashReport.Warn("Озвучка текста", e); }
    }

    /// <summary>Произнести по-русски (перевод в аудиоплеере). false — нет русского голоса или прервано.</summary>
    public static async Task<bool> SayRuAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || Platform.AudioPlayer.Muted) return false;
        int my = ++_gen;
        var path = await Tts.GetRuAsync(text);
        if (my != _gen || path == null) return false;
        return await PlayFileAsync(path, my, 1.0);
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

    /// <summary>Фрагмент файла (start..end, секунды) с замедлением — например, одна мора образца.</summary>
    public static async Task<bool> PlaySliceAsync(string path, double start, double end, double speed = 0.75)
    {
        int my = ++_gen;
        float[] x; int rate;
        try
        {
            (x, rate) = await Task.Run(() =>
            {
                var (d, r) = Pcm.Decode(path);
                int a = Math.Clamp((int)(start * r), 0, d.Length), b = Math.Clamp((int)(end * r), a, d.Length);
                var part = d[a..b];
                int fade = Math.Min(part.Length / 4, r / 100); // 10 мс — без щелчков на срезе
                for (int i = 0; i < fade; i++) { part[i] *= (float)i / fade; part[^(i + 1)] *= (float)i / fade; }
                return (Pcm.TimeStretch(part, r, speed), r);
            });
        }
        catch { return false; }
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
        catch { } // не открылись настройки Windows — кнопка просто ничего не делает
    }

    // ─────────── Оценка произношения ───────────
    public static bool Easy => ProgressStore.P.SpeechMode == "easy";
    /// <summary>Порог «засчитано»: лёгкий режим прощает акцент — главное, чтобы фразу поняли.</summary>
    public static int PassScore => ProgressStore.P.SpeechMode switch { "strict" => 82, "normal" => 70, _ => 55 };

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
        double sound = Reading.SoundSimilarity(ph, pt, ProgressStore.P.SpeechMode != "strict"); // акцент прощается везде, кроме строгого режима
        // Whisper любит дописывать частицу или междометие в конце («ここ» → «ここは»): фраза целиком внутри — почти зачёт
        if (pt.Length >= 2 && ph.Contains(pt, StringComparison.Ordinal))
        {
            int extra = ph.Length - pt.Length;
            sound = Math.Max(sound, extra <= 3 ? 0.95 : 1 - 0.5 * extra / ph.Length);
        }
        double sim = Math.Max(surface, sound);
        double conf = confidence <= 0.001 ? 1 : 0.85 + 0.15 * Math.Clamp(confidence, 0, 1);
        double curve = ProgressStore.P.SpeechMode switch { "strict" => 1.5, "normal" => 1.3, _ => 0.85 };
        int score = (int)Math.Round(100 * Math.Pow(sim, curve) * conf);
        if (sim > 0.999) score = Math.Max(score, 92); // полное совпадение — всегда высокий балл
        else if (Easy && sim >= 0.8) score = Math.Max(score, 80); // мелкие неточности не мешают пониманию
        score = Math.Min(score, cap);
        var hints = sim > 0.999 ? new List<string>() : Reading.Hints(tKana, hKana);
        return new SpeechCheck(score >= PassScore, score, heardRaw.Trim(), Verdict(score, sim < 0.999 ? heardRaw.Trim() : null), null, hKana, hints.ToArray());
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

    static string Verdict(int score, string? heardOther) => Easy ? score switch
    {
        >= 90 => "Превосходно! 素晴らしい！",
        >= 75 => "Отлично! Тебя прекрасно поймут.",
        >= 55 => "Засчитано! Понятно — можно чуть чётче.",
        >= 30 => heardOther != null ? $"Почти! Я услышал «{heardOther}». Попробуй ещё раз." : "Почти! Попробуй ещё раз.",
        _ => "Не расслышал. Послушай образец и скажи громче.",
    } : score switch
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
