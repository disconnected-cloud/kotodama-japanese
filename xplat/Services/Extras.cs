using System.IO;


namespace Matsubakira.Services;

/// <summary>Тихие звуковые отклики. Синтезируются один раз в память (без файлов), играются асинхронно.</summary>
public static class Sfx
{
    static readonly Platform.AudioPlayer Fx = new();
    static readonly Lazy<byte[]> _ok = new(() => Tones((880, 70), (1320, 110)));
    static readonly Lazy<byte[]> _bad = new(() => Tones((300, 90), (220, 140)));
    static readonly Lazy<byte[]> _done = new(() => Tones((660, 80), (880, 80), (1100, 80), (1320, 180)));
    static readonly Lazy<byte[]> _level = new(() => Tones((523, 90), (659, 90), (784, 90), (1047, 260)));

    public static void Ok() => Play(_ok);
    public static void Bad() => Play(_bad);
    public static void Done() => Play(_done);
    public static void LevelUp() => Play(_level);

    // Уведомления: мягкий «колокольчик» (основной тон + негармонические обертоны, как у металлофона), длинное затухание
    // Наборы: (короткий звук уведомления, напоминание). Все спокойные: мягкая атака, без резких частот.
    public static readonly (string id, string title)[] NotifyTones =
    {
        ("bell", "Колокольчик"), ("drop", "Капля"), ("koto", "Кото"), ("furin", "Фурин"),
    };
    static readonly Dictionary<string, (Lazy<byte[]> note, Lazy<byte[]> remind)> Sets = new()
    {
        ["bell"] = (new(() => Chime((1046.5, 0), (1568.0, 110))), new(() => Chime((784.0, 0), (987.8, 140), (1318.5, 280)))),
        ["drop"] = (new(() => Drop((1150, 0))), new(() => Drop((980, 0), (1310, 170)))),
        ["koto"] = (new(() => Pluck((587.3, 0), (880.0, 120))), new(() => Pluck((440.0, 0), (587.3, 150), (659.3, 300)))),   // пентатоника: ре–ля, ля–ре–ми
        ["furin"] = (new(() => Chime(1.6, (2093.0, 0), (2637.0, 230))), new(() => Chime(1.6, (2093.0, 0), (2349.3, 260), (2637.0, 520)))),
    };
    static DateTime _last;
    /// <summary>WAV звука уведомления (для проверки и предпросмотра).</summary>
    public static byte[] ToneWav(string id, bool remind) => remind ? Sets[id].remind.Value : Sets[id].note.Value;
    static (Lazy<byte[]> note, Lazy<byte[]> remind) Set => Sets.TryGetValue(ProgressStore.P.NotifyTone, out var s) ? s : Sets["bell"];

    /// <summary>Звук всплывающего уведомления (не звучит, если только что прозвучал другой звук).</summary>
    public static void Notify() { if ((DateTime.Now - _last).TotalMilliseconds > 500) Play(Set.note, notify: true); }
    /// <summary>Напоминание из трея — чуть заметнее, две-три ноты.</summary>
    public static void Reminder() => Play(Set.remind, notify: true);

    static byte[] Chime(params (double hz, int startMs)[] notes) => Chime(1.0, notes);

    /// <summary>Капля: чистый тон, который мягко «скатывается» вниз по высоте и быстро гаснет.</summary>
    static byte[] Drop(params (double hz, int startMs)[] notes)
    {
        const int rate = 22050;
        int len = rate * (notes.Max(n => n.startMs) + 600) / 1000;
        var mix = new double[len];
        foreach (var (hz, start) in notes)
        {
            int s0 = rate * start / 1000;
            double phase = 0;
            for (int i = 0; s0 + i < len; i++)
            {
                double t = (double)i / rate;
                double f = hz * (0.78 + 0.22 * Math.Exp(-t * 18)); // лёгкое падение высоты в начале
                phase += 2 * Math.PI * f / rate;
                mix[s0 + i] += Math.Sin(phase) * Math.Min(1, i / (rate * 0.003)) * Math.Exp(-t * 9);
            }
        }
        return ToWav(mix, rate);
    }

    /// <summary>Кото: щипок струны (алгоритм Карплуса — Стронга), тёплый и тихий.</summary>
    static byte[] Pluck(params (double hz, int startMs)[] notes)
    {
        const int rate = 22050;
        int len = rate * (notes.Max(n => n.startMs) + 1300) / 1000;
        var mix = new double[len];
        var rnd = new Random(3);
        foreach (var (hz, start) in notes)
        {
            int s0 = rate * start / 1000, n = (int)(rate / hz);
            var buf = new double[n];
            for (int i = 0; i < n; i++) buf[i] = rnd.NextDouble() * 2 - 1;
            // сглаживаем начальный шум — щипок мягче, без «щелчка»
            for (int pass = 0; pass < 2; pass++) for (int i = 1; i < n; i++) buf[i] = 0.5 * (buf[i] + buf[i - 1]);
            for (int i = 0, k = 0; s0 + i < len; i++, k = (k + 1) % n)
            {
                double v = buf[k];
                buf[k] = 0.996 * 0.5 * (buf[k] + buf[(k + 1) % n]);
                mix[s0 + i] += v * Math.Min(1, i / (rate * 0.004)); // мягкая атака 4 мс
            }
        }
        return ToWav(mix, rate);
    }

    static byte[] ToWav(double[] mix, int rate)
    {
        double peak = Math.Max(1e-6, mix.Max(Math.Abs));
        int fade = rate / 50; // 20 мс — тихий конец без обрыва
        var bytes = new byte[mix.Length * 2];
        for (int i = 0; i < mix.Length; i++)
        {
            double g = i > mix.Length - fade ? (mix.Length - i) / (double)fade : 1;
            short v = (short)(mix[i] / peak * 0.22 * g * short.MaxValue);
            bytes[i * 2] = (byte)(v & 0xFF); bytes[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return Recorder.ToWav(bytes, rate);
    }

    /// <summary>Колокольчик: основной тон + негармонические обертоны (как у металлофона). sustain > 1 — дольше звенит (фурин).</summary>
    static byte[] Chime(double sustain, params (double hz, int startMs)[] notes)
    {
        const int rate = 22050;
        int len = rate * (notes.Max(n => n.startMs) + (int)(1100 * sustain)) / 1000;
        var mix = new double[len];
        foreach (var (hz, start) in notes)
        {
            int s0 = rate * start / 1000;
            for (int i = 0; s0 + i < len; i++)
            {
                double t = (double)i / rate;
                double env = Math.Min(1, i / (rate * 0.004)) * Math.Exp(-t * 4.2 / sustain) * (sustain > 1 ? 0.7 : 1);
                // обертоны 2.76 и 5.40 — «металлический», но мягкий тембр; верхние гаснут быстрее
                double v = Math.Sin(2 * Math.PI * hz * t) + 0.32 * Math.Sin(2 * Math.PI * hz * 2.76 * t) * Math.Exp(-t * 6) + 0.12 * Math.Sin(2 * Math.PI * hz * 5.40 * t) * Math.Exp(-t * 10);
                mix[s0 + i] += v * env;
            }
        }
        return ToWav(mix, rate);
    }

    static void Play(Lazy<byte[]> s, bool notify = false)
    {
        if (notify ? !ProgressStore.P.NotifySound : !ProgressStore.P.Sounds) return;
        if (Platform.AudioPlayer.Muted) return;
        _last = DateTime.Now;
        // Отдельный канал: звук интерфейса не обрывает озвучку
        Task.Run(async () => { try { var (x, r) = Platform.Pcm.Decode(s.Value, true); await Fx.PlayAsync(x, r); } catch (Exception e) { CrashReport.Warn("Звуковой эффект", e); } });
    }

    /// <summary>Мягкие синусоидальные ноты с огибающей (без щелчков).</summary>
    static byte[] Tones(params (double hz, int ms)[] notes)
    {
        const int rate = 22050;
        var pcm = new List<short>();
        foreach (var (hz, ms) in notes)
        {
            int n = rate * ms / 1000;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / rate, env = Math.Min(1, i / (rate * 0.008)) * Math.Exp(-3.0 * i / n);
                pcm.Add((short)(Math.Sin(2 * Math.PI * hz * t) * env * 0.16 * short.MaxValue));
            }
        }
        var bytes = new byte[pcm.Count * 2];
        Buffer.BlockCopy(pcm.ToArray(), 0, bytes, 0, bytes.Length);
        return Recorder.ToWav(bytes, rate);
    }
}

public sealed record Achievement(string Id, string Jp, string Title, string Desc, Func<Progress, bool> Check);

/// <summary>Достижения: проверяются при изменении прогресса, новые показываются тостом.</summary>
public static class Achievements
{
    public static readonly Achievement[] All =
    {
        new("first", "初", "Первый шаг", "Набери первые XP", p => p.Xp > 0),
        new("streak3", "三", "Три дня подряд", "Серия 3 дня", p => p.Streak >= 3),
        new("streak7", "週", "Неделя", "Серия 7 дней", p => p.Streak >= 7),
        new("streak30", "月", "Месяц самурая", "Серия 30 дней", p => p.Streak >= 30),
        new("kana", "仮", "Каллиграф каны", "Освой 46 знаков хираганы", p => Kana.All.Count(k => k.Group == Kana.Basic && p.KanaScore.GetValueOrDefault("h:" + k.Hira) >= 3) >= 46),
        new("w50", "語", "50 слов", "Начни учить 50 слов", p => p.Cards.Keys.Count(k => k.StartsWith("w:")) >= 50),
        new("w300", "辞", "300 слов", "Начни учить 300 слов", p => p.Cards.Keys.Count(k => k.StartsWith("w:")) >= 300),
        new("w1000", "千", "Тысяча слов", "Начни учить 1000 слов", p => p.Cards.Keys.Count(k => k.StartsWith("w:")) >= 1000),
        new("kanji50", "漢", "50 кандзи", "Начни учить 50 кандзи", p => p.Cards.Keys.Count(k => k.StartsWith("k:")) >= 50),
        new("grammar5", "文", "Грамматист", "Пройди 5 уроков грамматики", p => p.GrammarDone.Count >= 5),
        new("voice90", "声", "Голос носителя", "Получи 90+ за произношение", p => p.SpeechBest.Values.Any(v => v >= 90)),
        new("dialog", "対", "Собеседник", "Сыграй роль в диалоге", p => p.Flags.Any(f => f.StartsWith("dialog:"))),
        new("units5", "道", "Путник", "Пройди 5 юнитов пути", p => p.Flags.Count(f => f.StartsWith("theory:")) >= 5),
        new("xp1000", "星", "1000 XP", "Набери 1000 опыта", p => p.Xp >= 1000),
        new("perfect1", "挑", "Идеальный день", "Выполни все испытания дня", p => p.PerfectDays >= 1),
        new("perfect10", "極", "Десять идеальных дней", "10 раз выполни все испытания дня", p => p.PerfectDays >= 10),
        new("week1", "週", "Неделя взята", "Выполни недельную цель", p => p.Badges.Count >= 1),
        new("week4", "暦", "Месяц целей", "Выполни недельную цель 4 раза", p => p.Badges.Count >= 4),
        new("shield", "盾", "Спасённая серия", "Щит сохранил серию в пропущенный день", p => p.ShieldsUsed >= 1),
        new("study10", "学", "Ученик", "Пройди 10 сессий «Учиться»", p => p.StudySessions >= 10),
    };

    public static void CheckNew()
    {
        var p = ProgressStore.P;
        foreach (var a in All)
            if (!p.Achievements.Contains(a.Id) && a.Check(p))
            {
                p.Achievements.Add(a.Id);
                Shell.Toast("Достижение: " + a.Title, a.Jp);
                Sfx.LevelUp();
            }
    }
}

/// <summary>Тетрадь ошибок: ключ — что именно перепутано (слово, кандзи, знак, вопрос грамматики, фраза).</summary>
public static class Mistakes
{
    public static void Add(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        var m = ProgressStore.P.Mistakes;
        m[key] = Math.Min(5, m.GetValueOrDefault(key) + 2);
        ProgressStore.Notify();
    }

    /// <summary>Верный ответ уменьшает счётчик; ошибка исчезает после двух верных ответов подряд.</summary>
    public static void Correct(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        var m = ProgressStore.P.Mistakes;
        if (!m.TryGetValue(key, out var c)) return;
        if (c <= 1) m.Remove(key); else m[key] = c - 1;
    }

    public static int Count => ProgressStore.P.Mistakes.Count;
}
