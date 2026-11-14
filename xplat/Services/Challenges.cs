using Avalonia.Threading;

namespace Matsubakira.Services;

/// <summary>Испытание дня: что сделать, сколько и сколько уже сделано сегодня.</summary>
public sealed record Challenge(string Id, string Icon, string Title, int Target, int Done)
{
    public bool Complete => Done >= Target;
}

/// <summary>
/// Испытания дня (3 штуки, меняются каждый день), недельная цель по опыту и награды:
/// щит серии (спасает серию при пропущенном дне, до 3 штук) и значки за недели и «идеальные дни».
/// </summary>
public static class Challenges
{
    public const int MaxShields = 3;

    // ─────────── Счётчики дня ───────────
    /// <summary>Счётчики сегодняшнего дня. Чтение ничего не меняет: вчерашний день сегодня — это просто нули.</summary>
    static IReadOnlyDictionary<string, int> Day => ProgressStore.P.DayDate == DateTime.Today.ToString("yyyy-MM-dd") ? ProgressStore.P.DayCounts : Empty;
    static readonly Dictionary<string, int> Empty = new();

    /// <summary>
    /// Смена дня: вчерашние счётчики — в архив (итоги недели, храним два месяца), сегодняшние с нуля.
    /// Вызывается при запуске, перед изменением счётчиков и по минутному таймеру (полночь) — А13.
    /// </summary>
    public static bool RollDay(Progress p)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (p.DayDate == today) return false;
        if (p.DayDate.Length > 0 && p.DayCounts.Count > 0) p.DayLog[p.DayDate] = new Dictionary<string, int>(p.DayCounts);
        var cutoff = DateTime.Today.AddDays(-60).ToString("yyyy-MM-dd");
        foreach (var old in p.DayLog.Keys.Where(k => string.CompareOrdinal(k, cutoff) < 0).ToList()) p.DayLog.Remove(old);
        p.DayDate = today; p.DayCounts.Clear();
        return true;
    }

    /// <summary>Отметить действие (повторение, верный ответ, фраза вслух…). Вызывать из потока интерфейса.</summary>
    public static void Bump(string kind, int n = 1)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Bump(kind, n)); return; }
        RollDay(ProgressStore.P);
        ProgressStore.P.DayCounts[kind] = ProgressStore.P.DayCounts.GetValueOrDefault(kind) + n;
        ProgressStore.Notify();
    }

    /// <summary>Счётчик за день: сегодня — живой, прошлые дни — из архива.</summary>
    public static int CountOn(DateTime day, string kind)
    {
        var key = day.ToString("yyyy-MM-dd");
        if (day.Date == DateTime.Today) return Day.GetValueOrDefault(kind);
        return ProgressStore.P.DayLog.TryGetValue(key, out var d) ? d.GetValueOrDefault(kind) : 0;
    }

    static int Count(string kind) => kind == "xp" ? ProgressStore.TodayXp : Day.GetValueOrDefault(kind);

    static readonly (string id, string icon, string title, Func<int, int> target)[] Pool =
    {
        ("review", "review", "Повтори {0} карточек", g => Math.Max(10, g / 3)),
        ("correct", "target", "Ответь верно {0} раз", g => Math.Max(15, g / 2)),
        ("speak", "speech", "Скажи вслух {0} фраз на зачёт", g => g >= 80 ? 8 : 5),
        ("xp", "star", "Набери {0} XP", g => g + g / 2),
        ("new", "words", "Выучи {0} новых слов или кандзи", g => g >= 80 ? 8 : 5),
        ("study", "flag", "Пройди сессию «Учиться»", _ => 1),
        ("listen", "voice", "Узнай на слух {0} слов", g => g >= 80 ? 12 : 8),
    };

    /// <summary>Три испытания на сегодня: один и тот же набор весь день, разный в разные дни.</summary>
    public static List<Challenge> Today()
    {
        int goal = Math.Max(20, ProgressStore.P.DailyGoal);
        var rnd = new Random(DateTime.Today.DayOfYear * 31 + DateTime.Today.Year);
        var picks = Pool.OrderBy(_ => rnd.Next()).Take(3).ToList();
        // «Учиться» — в каждом третьем дне обязательно, остальное как выпало
        if (DateTime.Today.DayOfYear % 3 == 0 && picks.All(x => x.id != "study")) picks[2] = Pool.First(x => x.id == "study");
        return picks.Select(x =>
        {
            int t = x.target(goal);
            return new Challenge(x.id, x.icon, string.Format(x.title, t), t, Math.Min(t, Count(x.id)));
        }).ToList();
    }

    // ─────────── Неделя ───────────
    public static DateTime WeekStart => DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7));
    public static string WeekKey => $"week:{WeekStart:yyyy-MM-dd}";
    public static int WeekTarget => Math.Max(100, ProgressStore.P.DailyGoal * 5);
    public static int WeekXp => Enumerable.Range(0, 7).Sum(i => ProgressStore.P.History.GetValueOrDefault(WeekStart.AddDays(i).ToString("yyyy-MM-dd")));

    /// <summary>Проверить выполнение и выдать награды (один раз за день/неделю). Вызывается при изменении прогресса.</summary>
    public static void Check()
    {
        var p = ProgressStore.P;
        var dayKey = "perfect:" + DateTime.Today.ToString("yyyy-MM-dd");
        var list = Today();
        foreach (var c in list)
        {
            var key = $"ch:{DateTime.Today:yyyy-MM-dd}:{c.Id}";
            if (c.Complete && p.Flags.Add(key))
            {
                ProgressStore.AddXp(10);
                Shell.Toast($"Испытание выполнено: {c.Title} · +10 XP", "挑");
                Sfx.Ok();
            }
        }
        if (list.All(c => c.Complete) && p.Flags.Add(dayKey))
        {
            p.PerfectDays++;
            Shell.Toast("Все испытания дня выполнены! 完璧", "完");
            Sfx.LevelUp();
        }
        if (WeekXp >= WeekTarget && p.Flags.Add(WeekKey))
        {
            p.Badges.Add(WeekStart.ToString("yyyy-MM-dd"));
            bool shield = p.Shields < MaxShields;
            if (shield) p.Shields++;
            Shell.Toast(shield ? "Недельная цель взята! Награда: щит серии и значок недели" : "Недельная цель взята! Новый значок недели", "週");
            Sfx.LevelUp();
        }
        // старые флаги испытаний не копим: оставляем последние 2 недели
        if (DateTime.Today.Day == 1 || p.Flags.Count > 4000)
            ProgressStore.DropOldDayFlags(p, DateTime.Today.AddDays(-14), "ch:", "plan:");
    }

    /// <summary>
    /// Щит серии: если пропущены дни, а щиты есть — они тратятся (по одному на день), и серия не сгорает.
    /// Вызывается при загрузке прогресса. Возвращает число потраченных щитов.
    /// </summary>
    public static int UseShields(Progress p)
    {
        if (p.Streak <= 0) return 0;
        int missed = (DateTime.Today.AddDays(-1) - p.LastStudy.Date).Days;
        if (missed <= 0 || missed > p.Shields) return 0;
        p.Shields -= missed;
        p.LastStudy = DateTime.Today.AddDays(-1).AddHours(12); // как будто занимались вчера — серия продолжится
        p.ShieldsUsed += missed;
        return missed;
    }
}
