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

    static void Play(Lazy<byte[]> s)
    {
        if (!ProgressStore.P.Sounds) return;
        // Отдельный канал: звук интерфейса не обрывает озвучку
        Task.Run(async () => { try { var (x, r) = Platform.Pcm.Decode(s.Value, true); await Fx.PlayAsync(x, r); } catch { } });
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
