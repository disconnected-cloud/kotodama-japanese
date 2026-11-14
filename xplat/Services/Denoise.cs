namespace Matsubakira.Services;

/// <summary>
/// Шумоподавление перед распознаванием: спектральное вычитание с профилем шума из самых тихих кадров записи
/// (паузы до и после фразы). Включается только на шумной записи — чистый голос не трогаем, чтобы не добавить артефактов.
/// </summary>
public static class Denoise
{
    const int N = 512, Hop = 256;   // 32 мс окно, 50 % перекрытие (Ханн при 50 % в сумме даёт 1)

    /// <summary>Отношение «речь/шум» в дБ по энергии кадров (90-й и 15-й перцентили).</summary>
    public static double SnrDb(float[] x)
    {
        int frames = (x.Length - N) / Hop + 1;
        if (frames < 8) return 99;
        var e = new double[frames];
        for (int f = 0; f < frames; f++)
        {
            double s = 0;
            for (int i = 0; i < N; i++) { double v = x[f * Hop + i]; s += v * v; }
            e[f] = s / N + 1e-12;
        }
        Array.Sort(e);
        return 10 * Math.Log10(e[(int)(frames * 0.9)] / e[(int)(frames * 0.15)]);
    }

    /// <summary>Чистит сигнал, если он шумный (SNR ниже порога). Возвращает true, если фильтр применён.</summary>
    public static bool Apply(float[] src, double maxSnrDb = 28)
    {
        if ((src.Length - N) / Hop + 1 < 8 || SnrDb(src) > maxSnrDb) return false;
        // Края дополняем нулями: первые 16 мс иначе приглушены окном без пары, а хвост за последним окном
        // обнулялся — страдали конечные «су» и «цу» (Г4)
        var x = new float[Hop + src.Length + N];
        Array.Copy(src, 0, x, Hop, src.Length);
        int frames = (x.Length - N) / Hop + 1;

        var win = new double[N];
        for (int i = 0; i < N; i++) win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N);
        const int bins = N / 2 + 1;
        var re = new double[frames][]; var im = new double[frames][];
        var energy = new double[frames];
        for (int f = 0; f < frames; f++)
        {
            var r = new double[N]; var m = new double[N];
            for (int i = 0; i < N; i++) r[i] = x[f * Hop + i] * win[i];
            Fft(r, m, false);
            re[f] = r; im[f] = m;
            for (int k = 0; k < bins; k++) energy[f] += r[k] * r[k] + m[k] * m[k];
        }

        // Профиль шума: средний спектр 15 % самых тихих кадров
        var quiet = Enumerable.Range(0, frames).OrderBy(f => energy[f]).Take(Math.Max(4, frames * 15 / 100)).ToArray();
        var noise = new double[bins];
        foreach (int f in quiet)
            for (int k = 0; k < bins; k++) noise[k] += Math.Sqrt(re[f][k] * re[f][k] + im[f][k] * im[f][k]);
        for (int k = 0; k < bins; k++) noise[k] /= quiet.Length;

        // Усиление по бинам: вычитание с запасом 1.6, пол 0.12 (без «музыкального шума»), сглаживание во времени
        var prev = new double[bins];
        Array.Fill(prev, 1.0);
        var y = new double[x.Length];
        for (int f = 0; f < frames; f++)
        {
            var r = re[f]; var m = im[f];
            for (int k = 0; k < bins; k++)
            {
                double mag = Math.Sqrt(r[k] * r[k] + m[k] * m[k]) + 1e-9;
                double g = Math.Max(0.12, 1 - 1.6 * noise[k] / mag);
                g = Math.Max(g, prev[k] * 0.55); // быстрое нарастание, плавное затухание — не обрезаем начало слогов
                prev[k] = g;
                r[k] *= g; m[k] *= g;
                if (k > 0 && k < N / 2) { r[N - k] = r[k]; m[N - k] = -m[k]; }
            }
            Fft(r, m, true);
            for (int i = 0; i < N; i++) y[f * Hop + i] += r[i];
        }
        for (int i = 0; i < src.Length; i++) src[i] = (float)y[Hop + i];
        return true;
    }

    /// <summary>Комплексное БПФ на месте (радикс-2). inverse — с делением на N.</summary>
    static void Fft(double[] re, double[] im, bool inverse)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = 2 * Math.PI / len * (inverse ? 1 : -1);
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    double nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                }
            }
        }
        if (inverse) for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
    }
}
