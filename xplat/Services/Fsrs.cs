namespace Matsubakira.Services;

/// <summary>
/// FSRS-4.5 (Free Spaced Repetition Scheduler, как в современном Anki).
/// У карточки две величины: стабильность S (через сколько дней вероятность вспомнить падает до 90%)
/// и сложность D (1..10). Интервал = S при целевой удерживаемости 90%.
/// </summary>
public static class Fsrs
{
    // Параметры по умолчанию FSRS-4.5 (обучены на миллионах повторений)
    static readonly double[] W = { 0.4872, 1.4003, 3.7145, 13.8206, 5.1618, 1.2298, 0.8975, 0.031, 1.6474, 0.1367, 1.0461, 2.1072, 0.0793, 0.3246, 1.587, 0.2272, 2.8755 };
    const double Decay = -0.5, Factor = 19.0 / 81.0, Retention = 0.9;

    static double InitS(int g) => Math.Max(0.1, W[g - 1]);
    static double InitD(int g) => Math.Clamp(W[4] - (g - 3) * W[5], 1, 10);
    public static double Retrievability(double elapsedDays, double s) => Math.Pow(1 + Factor * elapsedDays / Math.Max(0.01, s), Decay);
    static double Interval(double s) => Math.Max(1, Math.Round(s / Factor * (Math.Pow(Retention, 1 / Decay) - 1)));

    /// <summary>Перенос карточки из старого SM-2: стабильность ≈ прежний интервал.</summary>
    static void Migrate(SrsCard c)
    {
        if (c.Stability > 0 || c.Reps == 0) return;
        c.Stability = Math.Max(0.5, c.Interval);
        c.Difficulty = Math.Clamp(11 - c.Ease * 2.4, 1, 10);
        c.LastReview = c.Due.AddDays(-Math.Max(1, c.Interval));
    }

    /// <summary>Результат оценки без изменения карточки: (новая S, новая D, интервал в днях; 0 — повтор через несколько минут).</summary>
    public static (double s, double d, double days) Next(SrsCard? card, Rating r)
    {
        int g = (int)r;
        if (card == null || card.Reps == 0 && card.Stability <= 0)
        {
            double s0 = InitS(g), d0 = InitD(g);
            return (s0, d0, g == 1 ? 0 : g == 2 ? Math.Max(1, Math.Round(s0)) : Interval(s0));
        }
        var c = new SrsCard { Ease = card.Ease, Interval = card.Interval, Reps = card.Reps, Lapses = card.Lapses, Due = card.Due, Stability = card.Stability, Difficulty = card.Difficulty, LastReview = card.LastReview };
        Migrate(c);
        double elapsed = Math.Max(0, (DateTime.Now - c.LastReview).TotalDays);
        double R = Retrievability(elapsed, c.Stability);
        double d = c.Difficulty - W[6] * (g - 3);
        d = Math.Clamp(W[7] * InitD(4) + (1 - W[7]) * d, 1, 10);
        double s;
        if (g == 1)
            s = Math.Min(c.Stability, W[11] * Math.Pow(d, -W[12]) * (Math.Pow(c.Stability + 1, W[13]) - 1) * Math.Exp(W[14] * (1 - R)));
        else if (elapsed < 0.8)
            s = c.Stability * (g == 2 ? 1.0 : g == 3 ? 1.15 : 1.4);   // повтор в тот же день почти не растит стабильность
        else
            s = c.Stability * (Math.Exp(W[8]) * (11 - d) * Math.Pow(c.Stability, -W[9]) * (Math.Exp(W[10] * (1 - R)) - 1) * (g == 2 ? W[15] : 1) * (g == 4 ? W[16] : 1) + 1);
        s = Math.Max(0.1, s);
        double days = g == 1 ? 0 : Interval(s);
        if (g == 2) days = Math.Max(1, Math.Min(days, Interval(c.Stability)));       // «трудно» не дальше прежнего
        return (s, d, days);
    }

    public static void Apply(SrsCard c, Rating r)
    {
        Migrate(c);
        var (s, d, days) = Next(c, r);
        c.Stability = s; c.Difficulty = d; c.LastReview = DateTime.Now;
        c.Interval = days;
        if (r == Rating.Again) { c.Lapses++; c.Reps = 0; } else c.Reps++;
        c.Due = days <= 0 ? DateTime.Now.AddMinutes(c.Lapses > 0 ? 5 : 1) : DateTime.Today.AddDays(days).AddHours(4);
    }

    /// <summary>Прогноз: сколько карточек придёт на повторение в каждый из следующих n дней.</summary>
    public static int[] Forecast(IEnumerable<SrsCard> cards, int n = 7)
    {
        var res = new int[n];
        foreach (var c in cards)
        {
            int day = (int)Math.Floor((c.Due.Date - DateTime.Today).TotalDays);
            res[Math.Clamp(day, 0, n - 1)] += day < n ? 1 : 0;
        }
        return res;
    }
}
