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
    public bool NightTheme { get; set; }               // «Ночь»: чёрный фон, приглушённые цвета
    public string JpFontName { get; set; } = "";       // шрифт японского текста, "" — системный по умолчанию
    public double UiScale { get; set; } = 1.0;
    public double JpScale { get; set; } = 1.0;                // размер японского текста отдельно от интерфейса
    public bool HighContrast { get; set; }                   // высокий контраст: ярче текст и границы
    public bool Sounds { get; set; } = true;
    public bool NotifySound { get; set; } = true;
    public string NotifyTone { get; set; } = "bell";    // bell | drop | koto | furin      // мягкий колокольчик у уведомлений и напоминаний
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
    public string LastPlace { get; set; } = "";               // «Продолжить»: последний урок/текст/диалог (Resume)
    public string LastPlaceTitle { get; set; } = "";
    public bool QuietMode { get; set; }                       // «тихо»: без автоозвучки, аудирования и говорения — только текст
    public bool StudyNoSpeech { get; set; }                   // сессия «Учиться» без шага говорения
    public bool MaleVoice { get; set; }
    public string AsrModel { get; set; } = "small-q5_1";
    public string Level { get; set; } = "N5";
    public string DayDate { get; set; } = "";                     // счётчики испытаний дня
    public Dictionary<string, int> DayCounts { get; set; } = new();
    public Dictionary<string, Dictionary<string, int>> DayLog { get; set; } = new(); // счётчики прошлых дней (60 дней) — для итогов недели
    public int Shields { get; set; } = 1;                          // щиты серии (первый — в подарок)
    public int ShieldsUsed { get; set; }
    public int PerfectDays { get; set; }                           // дни, когда выполнены все испытания
    public List<string> Badges { get; set; } = new();             // значки недель (понедельник недели)
    public int StudyMode { get; set; } = 1;
    public string StudyDay { get; set; } = "";                     // незаконченная сессия «Учиться» (дата)
    public List<string> StudyDone { get; set; } = new();
    public List<string> StudySkipped { get; set; } = new();
    public int StudySeconds { get; set; }
    public int StudyXpStart { get; set; }
    public int StudySessions { get; set; }                         // всего пройдено сессий            // длина сессии «Учиться»: 0 — 5 мин, 1 — 15, 2 — 30
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
    /// <summary>Сколько щитов серии потрачено при запуске (пропущенные дни) — для уведомления.</summary>
    public static int ShieldsUsedOnLoad { get; private set; }

    public static void Load()
    {
        IsLoaded = true;
        Directory.CreateDirectory(Folder);
        try
        {
            if (File.Exists(FilePath))
                P = JsonSerializer.Deserialize(File.ReadAllBytes(FilePath), ProgressJson.Default.Progress) ?? new();
            DailyBackup();
        }
        catch
        {
            // Повреждённый файл — сохраняем копию и берём последнюю целую автокопию (если есть)
            try { File.Copy(FilePath, FilePath + ".bak", true); } catch { }
            P = Backups().Select(TryRead).FirstOrDefault(x => x != null) ?? new();
        }
        // v0.3: декор по умолчанию выключен (лепестки заметно нагружали CPU) — сбрасываем один раз
        if (P.Flags.Add("mig-v3")) P.Petals = false;
        // Голоса 女声/男声 звучали с придыханием — переключаем на чёткий голос по умолчанию
        if (P.Flags.Add("mig-vv2") && P.OfflineStyleId >= 10000) P.OfflineStyleId = -1;
        if (P.Xp > 0) P.Flags.Add("onboarded");
        // 2.0: число сессий — счётчиком; дневные флаги старше двух недель больше не нужны (файл не растёт)
        if (P.StudySessions == 0) P.StudySessions = P.Flags.Count(f => f.StartsWith("study:"));
        var cutoff = DateTime.Today.AddDays(-14).ToString("yyyy-MM-dd");
        P.Flags.RemoveWhere(f => (f.StartsWith("study:") || f.StartsWith("perfect:") || f.StartsWith("ch:") || f.StartsWith("plan:") || f.StartsWith("week:"))
            && f.IndexOf(':') is var c && f.Length >= c + 11 && string.CompareOrdinal(f.Substring(c + 1, 10), cutoff) < 0); // у кого уже есть прогресс — помощник первого запуска не нужен
        // Серия сгорает, если пропущен день — если нет щита
        ShieldsUsedOnLoad = Challenges.UseShields(P);
        if (P.LastStudy.Date < DateTime.Today.AddDays(-1)) P.Streak = 0;
    }

    /// <summary>Отладочные снимки экрана не должны менять настоящий прогресс.</summary>
    public static bool ReadOnly;

    static readonly object SaveGate = new();

    /// <summary>Сохранить прогресс. Вызывать из потока интерфейса: данные меняются только там, и сериализация
    /// из фонового потока могла сорваться («коллекция изменена») — тогда изменения терялись до следующей записи.</summary>
    // ─────────── Автокопии: раз в день, последние 14 ───────────
    public static string BackupDir => Path.Combine(Folder, "backups");
    const int KeepBackups = 14;

    /// <summary>Автокопии прогресса, новые первыми.</summary>
    public static List<string> Backups() =>
        Directory.Exists(BackupDir) ? Directory.GetFiles(BackupDir, "progress-*.json").OrderByDescending(f => f, StringComparer.Ordinal).ToList() : new();

    static Progress? TryRead(string path)
    {
        try { return JsonSerializer.Deserialize(File.ReadAllBytes(path), ProgressJson.Default.Progress); }
        catch { return null; }
    }

    /// <summary>При запуске: если сегодня копии ещё нет — копируем progress.json (до любых изменений этого дня).</summary>
    static void DailyBackup()
    {
        if (ReadOnly || !File.Exists(FilePath)) return;
        try
        {
            Directory.CreateDirectory(BackupDir);
            var today = Path.Combine(BackupDir, $"progress-{DateTime.Today:yyyy-MM-dd}.json");
            if (!File.Exists(today)) File.Copy(FilePath, today);
            foreach (var old in Backups().Skip(KeepBackups)) File.Delete(old);
        }
        catch (Exception e) { CrashReport.Warn("Автокопия прогресса", e); }
    }

    /// <summary>Восстановить прогресс из автокопии (текущий перед этим тоже сохраняется рядом).</summary>
    public static bool RestoreBackup(string path)
    {
        if (TryRead(path) is not { } p) return false;
        try { File.Copy(FilePath, FilePath + ".before-restore", true); } catch { }
        P = p;
        Save();
        Touch();
        return true;
    }

    /// <summary>Озвучивать автоматически: включено и не «тихий режим».</summary>
    public static bool AutoSpeakOn => P.AutoSpeak && !P.QuietMode;

    public static void Save()
    {
        if (ReadOnly) return;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(P, ProgressJson.Default.Progress);
            lock (SaveGate)
            {
                var tmp = FilePath + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                File.Move(tmp, FilePath, true);
            }
        }
        catch (Exception ex) { CrashReport.Log(ex); }
    }

    /// <summary>Номер изменения прогресса — для кэшей, которые надо пересчитать после изменений.</summary>
    public static int Version { get; private set; }

    static void Touch()
    {
        Version++;
        Changed?.Invoke();
        // Сохранение через 2 с после последнего изменения (серия правок — одна запись на диск)
        _saveTimer ??= new Timer(_ => Avalonia.Threading.Dispatcher.UIThread.Post(Save), null, Timeout.Infinite, Timeout.Infinite);
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
        if (!P.Cards.TryGetValue(id, out var c)) { P.Cards[id] = c = new SrsCard(); Challenges.Bump("new"); }
        else Challenges.Bump("review");
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
        if (score >= Speech.PassScore) Challenges.Bump("speak");
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
