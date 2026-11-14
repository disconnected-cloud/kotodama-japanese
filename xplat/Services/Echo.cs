namespace Matsubakira.Services;

/// <summary>
/// Шэдоуинг без наушников: микрофон пишет голос синтезатора из динамиков, и оценивался он, а не ученик.
/// Сравниваем огибающие громкости записи и образца (кадры 20 мс) при сдвиге до 0,6 с: эхо из динамиков
/// повторяет образец почти точно, а живая речь вместе с образцом — только примерно.
/// </summary>
public static class Echo
{
    const int Frame = 320; // 20 мс при 16 кГц

    /// <summary>Наибольшая корреляция огибающих (0..1) по всем сдвигам записи относительно образца.</summary>
    public static double Similarity(float[] rec, float[] sample)
    {
        var a = Envelope(rec);
        var b = Envelope(sample);
        if (a.Length < 10 || b.Length < 10) return 0;
        double best = 0;
        for (int lag = 0; lag <= 30 && lag < a.Length - 5; lag++)
        {
            int n = Math.Min(a.Length - lag, b.Length);
            best = Math.Max(best, Corr(a.AsSpan(lag, n), b.AsSpan(0, n)));
        }
        return best;
    }

    /// <summary>Порог: выше — в записи звучит образец из динамиков, а не ученик.</summary>
    public const double EchoThreshold = 0.9;

    static double[] Envelope(float[] x)
    {
        var e = new double[x.Length / Frame];
        for (int i = 0; i < e.Length; i++)
        {
            double s = 0;
            for (int j = i * Frame; j < (i + 1) * Frame; j++) s += x[j] * x[j];
            e[i] = Math.Log10(1e-9 + s / Frame); // в логарифме: тихое эхо так же похоже, как громкое
        }
        return e;
    }

    static double Corr(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
    {
        double ma = 0, mb = 0;
        for (int i = 0; i < a.Length; i++) { ma += a[i]; mb += b[i]; }
        ma /= a.Length; mb /= b.Length;
        double sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < a.Length; i++) { double da = a[i] - ma, db = b[i] - mb; sab += da * db; saa += da * da; sbb += db * db; }
        return saa < 1e-9 || sbb < 1e-9 ? 0 : sab / Math.Sqrt(saa * sbb);
    }
}
