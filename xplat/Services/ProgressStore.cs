using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Matsubakira.Services;

public sealed class SrsCard
{
    public double Ease { get; set; } = 2.5;
    public double Interval { get; set; }      // дни
    public int Reps { get; set; }
    public int Lapses { get; set; }
    public DateTime Due { get; set; }
    public double Stability { get; set; }      // FSRS: дни до падения вероятности вспомнить до 90%
    public double Difficulty { get; set; }     // FSRS: 1..10
    public DateTime LastReview { get; set; }

    [JsonIgnore] public bool Mastered => Math.Max(Interval, Stability) >= 21;
}

public sealed class Progress
{
    public Dictionary<string, SrsCard> Cards { get; set; } = new();
    public Dictionary<string, int> KanaScore { get; set; } = new();     // 0..5
    public Dictionary<string, int> SpeechBest { get; set; } = new();    // 0..100
    public HashSet<int> GrammarDone { get; set; } = new();
    public Dictionary<string, int> History { get; set; } = new();       // "yyyy-MM-dd" → XP
    public int Xp { get; set; }
    public int Streak { get; set; }
    public DateTime LastStudy { get; set; }
    public int DailyGoal { get; set; } = 50;
    public int NewPerSession { get; set; } = 10;
    public bool Petals { get; set; }
    public bool Intro { get; set; } = true;
    public bool LightTheme { get; set; }
    public double UiScale { get; set; } = 1.0;
    public bool Sounds { get; set; } = true;
    public string TtsEngine { get; set; } = "auto";   // auto | online | offline
    public uint OfflineStyle { get; set; }            // устарело (0 совпадало с реальным стилем)
    public int OfflineStyleId { get; set; } = -1;      // выбранный стиль VOICEVOX, -1 — лучший чёткий из установленных
    public string SpeechMode { get; set; } = "easy";     // easy — как в Duolingo, normal, strict
    public string MicMode { get; set; } = "auto";      // auto — стоп по паузе, manual — стоп кнопкой
    public double MicPause { get; set; } = 1.5;         // сколько секунд тишины считать концом фразы
    public int MicDevice { get; set; } = -1;            // индекс микрофона, -1 — по умолчанию
    public string AccentColor { get; set; } = "sakura"; // sakura | mint | indigo | amber
    public bool Reminders { get; set; } = true;
    public Dictionary<string, int> GameBest { get; set; } = new();
    public bool AutoUpdate { get; set; } = true;
    public string Goal { get; set; } = "";
    public string LlmModel { get; set; } = "";
    public bool ChatAutoSpeak { get; set; } = true;
    public string WordOfDayTime { get; set; } = "09:00";   // "" — выключено
    public DateTime LastWordOfDay { get; set; }
    public bool WordOfDaySpeak { get; set; } = true;
    public bool MinimizeToTray { get; set; }
    public DateTime LastCrashSeen { get; set; }
    public DateTime LastUpdateCheck { get; set; }
    public Dictionary<string, int> Mistakes { get; set; } = new();  // id ошибки → счётчик
    public HashSet<string> Achievements { get; set; } = new();
    public HashSet<string> Flags { get; set; } = new();
    public List<string> DictHistory { get; set; } = new();       // недавно открытые слова словаря
    public List<string[]> CustomWords { get; set; } = new();
    public bool ShowRomaji { get; set; } = true;
    public bool AutoSpeak { get; set; } = true;
    public bool MaleVoice { get; set; }
    public string AsrModel { get; set; } = "small-q5_1";
    public string Level { get; set; } = "N5";
}

[JsonSerializable(typeof(Progress))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal partial class ProgressJson : JsonSerializerContext { }

public enum Rating { Again = 1, Hard = 2, Good = 3, Easy = 4 }

/// <summary>Хранилище прогресса: JSON в %AppData%, отложенное сохранение (не дёргаем диск на каждый ответ).</summary>
public static class ProgressStore
{
    /// <summary>
    /// Портативный режим: если рядом с exe лежит файл «portable», все данные (прогресс, озвучка, модели)
    /// хранятся в папке data рядом с программой — её можно носить на флешке.
    /// </summary>
    public static readonly bool Portable = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable"));
    public static readonly string Folder = Portable
        ? Path.Combine(AppContext.BaseDirectory, "data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Matsubakira"); // старое имя папки — ради совместимости прогресса
    static readonly string FilePath = Path.Combine(Folder, "progress.json");

    public static Progress P { get; private set; } = new();
    public static event Action? Changed;

    static Timer? _saveTimer;

    public static bool IsLoaded { get; private set; }

    public static void Load()
    {
        IsLoaded = true;
        Directory.CreateDirectory(Folder);
        try
        {
            if (File.Exists(FilePath))
                P = JsonSerializer.Deserialize(File.ReadAllBytes(FilePath), ProgressJson.Default.Progress) ?? new();
        }
        catch
        {
            // Повреждённый файл — сохраняем копию и начинаем заново
            try { File.Copy(FilePath, FilePath + ".bak", true); } catch { }
            P = new();
        }
        // v0.3: декор по умолчанию выключен (лепестки заметно нагружали CPU) — сбрасываем один раз
        if (P.Flags.Add("mig-v3")) P.Petals = false;
        // Голоса 女声/男声 звучали с придыханием — переключаем на чёткий голос по умолчанию
        if (P.Flags.Add("mig-vv2") && P.OfflineStyleId >= 10000) P.OfflineStyleId = -1;
        if (P.Xp > 0) P.Flags.Add("onboarded"); // у кого уже есть прогресс — помощник первого запуска не нужен
        // Серия сгорает, если пропущен день
        if (P.LastStudy.Date < DateTime.Today.AddDays(-1)) P.Streak = 0;
    }

    /// <summary>Отладочные снимки экрана не должны менять настоящий прогресс.</summary>
    public static bool ReadOnly;

    public static void Save()
    {
        if (ReadOnly) return;
        try
        {
            var tmp = FilePath + ".tmp";
            File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(P, ProgressJson.Default.Progress));
            File.Move(tmp, FilePath, true);
        }
        catch { }
    }

    static void Touch()
    {
        Changed?.Invoke();
        // Сохранение через 2 с после последнего изменения (серия правок — одна запись на диск)
        _saveTimer ??= new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
        _saveTimer.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }

    static string TodayKey => DateTime.Today.ToString("yyyy-MM-dd");
    public static int TodayXp => P.History.TryGetValue(TodayKey, out var x) ? x : 0;

    public static void AddXp(int xp)
    {
        if (xp <= 0) return;
        var today = DateTime.Today;
        if (P.LastStudy.Date != today)
            P.Streak = P.LastStudy.Date == today.AddDays(-1) ? P.Streak + 1 : 1;
        P.LastStudy = DateTime.Now;
        P.Xp += xp;
        P.History[TodayKey] = TodayXp + xp;
        Touch();
    }

    public static int Level => (int)Math.Floor(Math.Sqrt(P.Xp / 50.0)) + 1;
    public static int XpForLevel(int lvl) => (lvl - 1) * (lvl - 1) * 50;

    // ─────────── SRS (FSRS-4.5, см. Fsrs.cs) ───────────
    public static SrsCard? Card(string id) => P.Cards.TryGetValue(id, out var c) ? c : null;
    public static bool IsDue(string id) => P.Cards.TryGetValue(id, out var c) && c.Due <= DateTime.Now;
    public static int DueCount(string prefix) =>
        P.Cards.Count(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal) && kv.Value.Due <= DateTime.Now);

    /// <summary>Интервал (дни), который получит карточка при данной оценке — для подписей на кнопках (FSRS).</summary>
    public static double Preview(SrsCard? c, Rating r) => Fsrs.Next(c, r).days;

    public static void Rate(string id, Rating r)
    {
        if (!P.Cards.TryGetValue(id, out var c)) P.Cards[id] = c = new SrsCard();
        Fsrs.Apply(c, r);
        AddXp(r == Rating.Again ? 1 : 3);
    }

    public static string FormatInterval(double days) => days switch
    {
        <= 0 => "<1 мин",
        < 1 => "12 ч",
        < 30 => $"{days:0} д",
        < 365 => $"{days / 30:0.#} мес",
        _ => $"{days / 365:0.#} г",
    };

    // ─────────── Кана ───────────
    public static int KanaScore(string key) => P.KanaScore.TryGetValue(key, out var s) ? s : 0;
    public static void KanaAnswer(string key, bool ok)
    {
        var s = KanaScore(key);
        P.KanaScore[key] = Math.Clamp(ok ? s + 1 : s - 2, 0, 5);
        AddXp(ok ? 2 : 0);
        if (!ok) Touch();
    }

    public static void SpeechResult(string key, int score)
    {
        if (!P.SpeechBest.TryGetValue(key, out var best) || score > best) P.SpeechBest[key] = score;
        AddXp(score >= 80 ? 5 : score >= 50 ? 3 : 1);
    }

    public static void GrammarComplete(int id) { P.GrammarDone.Add(id); AddXp(20); }

    public static bool Import(string path)
    {
        try
        {
            var np = JsonSerializer.Deserialize(File.ReadAllBytes(path), ProgressJson.Default.Progress);
            if (np == null) return false;
            Save();
            File.Copy(FilePath, FilePath + ".before-import", true);
            P = np;
            Touch();
            return true;
        }
        catch { return false; }
    }

    public static void Reset() { P = new Progress(); Touch(); }
    public static void Notify() => Touch();
}
