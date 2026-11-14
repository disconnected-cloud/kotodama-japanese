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
    public bool FuriUnknownOnly { get; set; }               // читалка: фуригана только над незнакомыми кандзи
    public int Motion { get; set; }                          // анимации: 0 — сами (на слабом телефоне выключены), 1 — всегда, 2 — без анимаций
    public bool HighContrast { get; set; }                   // высокий контраст: ярче текст и границы
    public bool Sounds { get; set; } = true;
    public bool NotifySound { get; set; } = true;
    public string NotifyTone { get; set; } = "bell";    // bell | drop | koto | furin      // мягкий колокольчик у уведомлений и напоминаний
    public string TtsEngine { get; set; } = "offline"; // 4.1: только офлайн (VOICEVOX → системный голос); auto/online — старые значения
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
    public bool SidebarRail { get; set; }
    public int FocusSessions { get; set; }                    // завершённые «помидоры» фокус-таймера
    public Dictionary<string, string> Journal { get; set; } = new();  // дневник: "yyyy-MM-dd" → текст записи
    public Dictionary<string, string> Notes { get; set; } = new();   // личные заметки к словам (id слова → текст)                     // ПК: боковое меню свёрнуто до иконок (Ctrl+B)
    public string AsrModel { get; set; } = ""; // пусто — Asr.DefaultId (на слабом телефоне — быстрая)
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
    public int Schema { get; set; }                                // номер схемы прогресса: применённые миграции (ProgressStore.Migrations)
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
    /// <summary>KOTODAMA_DATA_DIR — своя папка данных (тесты, отладка), иначе портативная или %AppData%.</summary>
    public static readonly string Folder = Environment.GetEnvironmentVariable("KOTODAMA_DATA_DIR") is { Length: > 0 } custom ? custom
        : Portable ? Path.Combine(AppContext.BaseDirectory, "data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Matsubakira"); // старое имя папки — ради совместимости прогресса
    static readonly string FilePath = Path.Combine(Folder, "progress.json");

    public static Progress P { get; private set; } = new();
    public static event Action? Changed;

    static Timer? _saveTimer;

    public static bool IsLoaded { get; private set; }
    /// <summary>Сколько щитов серии потрачено при запуске (пропущенные дни) — для уведомления.</summary>
    public static int ShieldsUsedOnLoad { get; private set; }

    /// <summary>Почему прогресс открыт только для чтения (файл занят другой программой) — для сообщения пользователю.</summary>
    public static string? LoadProblem { get; private set; }

    public static void Load()
    {
        IsLoaded = true;
        Directory.CreateDirectory(Folder);
        P = ReadMain() ?? new();
        Prepare(P);
        DailyBackup();
        Challenges.RollDay(P);
        // Серия сгорает, если пропущен день — если нет щита
        ShieldsUsedOnLoad = Challenges.UseShields(P);
        if (P.LastStudy.Date < DateTime.Today.AddDays(-1)) P.Streak = 0;
    }

    /// <summary>
    /// Чтение progress.json. Занятый файл (антивирус, облако, вторая копия) — не повреждение: несколько повторов,
    /// потом режим только чтения, чтобы не затереть настоящий прогресс. Битый JSON сохраняется под именем с датой
    /// и временем, а прогресс берётся из последней целой автокопии.
    /// </summary>
    static Progress? ReadMain()
    {
        if (!File.Exists(FilePath)) return null;
        for (int attempt = 0; ; attempt++)
        {
            byte[] bytes;
            try { bytes = ReadShared(FilePath); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt < 5) { Thread.Sleep(300); continue; }
                CrashReport.Warn("Прогресс: файл занят", e);
                ReadOnly = true;
                LoadProblem = "Файл прогресса занят другой программой (антивирус, облачная синхронизация или вторая копия Kotodama). " +
                              "Изменения в этот раз не сохраняются — закрой мешающую программу и перезапусти Kotodama.";
                return Backups().Select(TryRead).FirstOrDefault(x => x != null);
            }
            try { return JsonSerializer.Deserialize(bytes, ProgressJson.Default.Progress); }
            catch (Exception e)
            {
                CrashReport.Warn("Прогресс: файл повреждён", e);
                try { File.Copy(FilePath, Path.Combine(Folder, $"progress.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true); } catch { }
                return Backups().Select(TryRead).FirstOrDefault(x => x != null);
            }
        }
    }

    /// <summary>Чтение без запрета чужой записи: замок другой программы не должен мешать, если она разрешает читать.</summary>
    static byte[] ReadShared(string path)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        f.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Нормализация и миграции — для загрузки, импорта и восстановления из копии одинаково.</summary>
    public static void Prepare(Progress p)
    {
        Normalize(p);
        Migrate(p);
    }

    /// <summary>
    /// Чужой или старый файл может содержать null вместо коллекций и испорченные записи — они роняли десятки экранов.
    /// null → пустая коллекция, битые записи выбрасываются, числа ограничиваются разумными пределами.
    /// </summary>
    public static void Normalize(Progress p)
    {
        p.Cards ??= new(); p.KanaScore ??= new(); p.SpeechBest ??= new(); p.GrammarDone ??= new(); p.History ??= new();
        p.GameBest ??= new(); p.Mistakes ??= new(); p.Achievements ??= new(); p.Flags ??= new(); p.DictHistory ??= new();
        p.CustomWords ??= new(); p.Journal ??= new(); p.Notes ??= new(); p.DayCounts ??= new(); p.DayLog ??= new();
        p.Badges ??= new(); p.StudyDone ??= new(); p.StudySkipped ??= new();
        foreach (var k in p.Cards.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList()) p.Cards.Remove(k);
        foreach (var c in p.Cards.Values)
        {
            if (double.IsNaN(c.Ease) || c.Ease is < 1 or > 10) c.Ease = 2.5;
            if (double.IsNaN(c.Interval) || c.Interval < 0) c.Interval = 0;
            if (double.IsNaN(c.Stability) || c.Stability < 0) c.Stability = 0;
            c.Difficulty = double.IsNaN(c.Difficulty) ? 0 : Math.Clamp(c.Difficulty, 0, 10);
            c.Reps = Math.Max(0, c.Reps); c.Lapses = Math.Max(0, c.Lapses);
        }
        p.CustomWords.RemoveAll(w => w == null || w.Length < 3 || w.Take(3).Any(string.IsNullOrEmpty));
        p.DictHistory.RemoveAll(string.IsNullOrEmpty);
        p.Flags.RemoveWhere(string.IsNullOrEmpty);
        p.Badges.RemoveAll(string.IsNullOrEmpty);
        p.StudyDone.RemoveAll(string.IsNullOrEmpty); p.StudySkipped.RemoveAll(string.IsNullOrEmpty);
        foreach (var k in p.Journal.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList()) p.Journal.Remove(k);
        foreach (var k in p.Notes.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList()) p.Notes.Remove(k);
        foreach (var k in p.DayLog.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList()) p.DayLog.Remove(k);
        foreach (var k in p.SpeechBest.Keys.ToList()) p.SpeechBest[k] = Math.Clamp(p.SpeechBest[k], 0, 100);
        foreach (var k in p.KanaScore.Keys.ToList()) p.KanaScore[k] = Math.Clamp(p.KanaScore[k], 0, 5);
        p.Xp = Math.Max(0, p.Xp); p.Streak = Math.Max(0, p.Streak); p.Shields = Math.Clamp(p.Shields, 0, 10);
        p.DailyGoal = Math.Clamp(p.DailyGoal, 10, 1000); p.NewPerSession = Math.Clamp(p.NewPerSession, 1, 100);
        p.UiScale = double.IsNaN(p.UiScale) ? 1 : Math.Clamp(p.UiScale, 0.7, 2); p.JpScale = double.IsNaN(p.JpScale) ? 1 : Math.Clamp(p.JpScale, 0.7, 2.5);
        p.MicPause = double.IsNaN(p.MicPause) ? 1.5 : Math.Clamp(p.MicPause, 0.5, 5);
        p.JpFontName ??= ""; p.NotifyTone ??= "bell"; p.TtsEngine ??= "offline"; p.SpeechMode ??= "easy"; p.MicMode ??= "auto";
        p.AccentColor ??= "sakura"; p.Goal ??= ""; p.WordOfDayTime ??= "09:00"; p.LastPlace ??= ""; p.LastPlaceTitle ??= "";
        p.AsrModel ??= ""; p.Level ??= "N5"; p.DayDate ??= ""; p.StudyDay ??= "";
    }

    /// <summary>
    /// Пронумерованные миграции схемы прогресса (А11): каждая применяется один раз, номер хранится в Progress.Schema.
    /// Новую миграцию дописывай в конец — порядок и номера не меняются. Старые флаги mig-* читаются ради совместимости.
    /// </summary>
    static readonly Action<Progress>[] Migrations =
    {
        // 1 — v0.3: декор по умолчанию выключен (лепестки заметно нагружали CPU)
        p => { if (p.Flags.Add("mig-v3")) p.Petals = false; },
        // 2 — голоса 女声/男声 звучали с придыханием: чёткий голос по умолчанию
        p => { if (p.Flags.Add("mig-vv2") && p.OfflineStyleId >= 10000) p.OfflineStyleId = -1; },
        // 3 — 4.1: онлайн-голоса убраны, «Авто» и «Онлайн» становятся офлайн-режимом
        p => p.TtsEngine = "offline",
    };

    public static int SchemaVersion => Migrations.Length;

    /// <summary>Миграции старых версий прогресса и ежедневная уборка.</summary>
    static void Migrate(Progress p)
    {
        for (int i = Math.Max(0, p.Schema); i < Migrations.Length; i++) Migrations[i](p);
        p.Schema = Math.Max(p.Schema, Migrations.Length);
        if (p.Xp > 0) p.Flags.Add("onboarded"); // у кого уже есть прогресс — помощник первого запуска не нужен
        // 2.0: число сессий — счётчиком; дневные флаги старше двух недель больше не нужны (файл не растёт)
        if (p.StudySessions == 0) p.StudySessions = p.Flags.Count(f => f.StartsWith("study:"));
        DropOldDayFlags(p, DateTime.Today.AddDays(-14));
        ApplyIdAliases(p, Db.IdAliases);
    }

    /// <summary>
    /// Переименованные карточки (исправлена опечатка в слове): прогресс переезжает со старого ID на новый —
    /// карточки, лучшие оценки речи, заметки и ошибки (Data/id_aliases.json, А8). Если новый ID уже есть, берётся
    /// более продвинутая карточка.
    /// </summary>
    public static void ApplyIdAliases(Progress p, IReadOnlyDictionary<string, string> aliases)
    {
        foreach (var (from, to) in aliases)
        {
            if (from == to) continue;
            if (p.Cards.Remove(from, out var c) && (!p.Cards.TryGetValue(to, out var have) || c.Reps > have.Reps)) p.Cards[to] = c;
            if (p.SpeechBest.Remove(from, out var b)) p.SpeechBest[to] = Math.Max(b, p.SpeechBest.GetValueOrDefault(to));
            if (p.Notes.Remove(from, out var n) && !p.Notes.ContainsKey(to)) p.Notes[to] = n;
            if (p.Mistakes.Remove(from, out var m)) p.Mistakes[to] = m + p.Mistakes.GetValueOrDefault(to);
        }
    }

    /// <summary>Убрать дневные флаги («study:2026-01-01», «ch:…») старше даты; флаги без даты и короткие не трогаются.</summary>
    public static void DropOldDayFlags(Progress p, DateTime before, params string[] prefixes)
    {
        if (prefixes.Length == 0) prefixes = new[] { "study:", "perfect:", "ch:", "plan:", "week:" };
        var cutoff = before.ToString("yyyy-MM-dd");
        p.Flags.RemoveWhere(f => prefixes.Any(f.StartsWith) && f.IndexOf(':') is var c && f.Length >= c + 11
            && string.CompareOrdinal(f, c + 1, cutoff, 0, 10) < 0);
    }

    /// <summary>Прогресс заменён целиком (импорт, восстановление из копии): страницы и кэши данных надо построить заново.</summary>
    public static event Action? Replaced;

    static void Replace(Progress p)
    {
        Prepare(p);
        P = p;
        Save();
        Touch();
        Replaced?.Invoke();
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
        Replace(p);
        return true;
    }

    /// <summary>Озвучивать автоматически: включено и не «тихий режим».</summary>
    public static bool AutoSpeakOn => P.AutoSpeak && !P.QuietMode;

    /// <summary>Текущий прогресс в JSON (для экспорта).</summary>
    public static byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(P, ProgressJson.Default.Progress);

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
    /// <summary>
    /// Сколько карточек ждут повторения — только тех, что можно повторить (слова и кандзи из материалов).
    /// «Осиротевшие» карточки (слово переименовано без алиаса) не попадают в значок, трей и напоминания (А8).
    /// </summary>
    public static int DueCount()
    {
        var now = DateTime.Now;
        var ok = Db.ReviewableIds;
        return P.Cards.Count(kv => kv.Value.Due <= now && ok.Contains(kv.Key));
    }

    public static bool IsDue(string id) => P.Cards.TryGetValue(id, out var c) && c.Due <= DateTime.Now;
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
            if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".before-import", true);
            Replace(np);
            return true;
        }
        catch (Exception e) { CrashReport.Warn("Импорт прогресса", e); return false; }
    }

    /// <summary>Полный сброс: удалить автокопии и все служебные копии прогресса (в них есть и дневник) — А6.</summary>
    public static void DeleteAllCopies()
    {
        try { if (Directory.Exists(BackupDir)) Directory.Delete(BackupDir, true); } catch (Exception e) { CrashReport.Warn("Сброс: автокопии", e); }
        foreach (var f in Directory.Exists(Folder) ? Directory.GetFiles(Folder, "progress*") : Array.Empty<string>())
            if (!f.EndsWith("progress.json")) try { File.Delete(f); } catch { }
    }

    public static void Reset() { P = new Progress(); Touch(); Replaced?.Invoke(); }
    public static void Notify() => Touch();
}
