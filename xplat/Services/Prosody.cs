using Matsubakira.Platform;

namespace Matsubakira.Services;

public sealed record ProsodyResult(int Intonation, int Rhythm, double[] RefPitch, double[] UserPitch, double DurationRatio, List<string> Tips);

/// <summary>
/// Акустический анализ произношения (полностью офлайн):
/// • интонация — контур основного тона (YIN), нормализованный к медиане голоса говорящего, выравнивание DTW;
/// • ритм — темп (отношение длительностей) и совпадение огибающей громкости (долгие гласные, паузы っ).
/// </summary>
public static class Prosody
{
    const int Rate = 16000, Hop = 160, Win = 640; // шаг 10 мс, окно 40 мс

    public static float[] DecodeFile(string path)
    {
        var (x, rate) = Pcm.Decode(path);
        return Pcm.Resample(x, rate, Rate);
    }

    public static float[] FromWav(byte[] wav) => Pcm16ToFloat(wav, 44);

    static float[] Pcm16ToFloat(byte[] b, int offset)
    {
        int n = (b.Length - offset) / 2;
        var f = new float[Math.Max(0, n)];
        for (int i = 0; i < n; i++) f[i] = BitConverter.ToInt16(b, offset + i * 2) / 32768f;
        return f;
    }

    /// <summary>Основной тон по кадрам (полутона относительно 100 Гц), NaN — глухой кадр. Плюс энергия кадров.</summary>
    public static (double[] pitch, double[] energy) Analyze(float[] x)
    {
        int frames = Math.Max(0, (x.Length - Win) / Hop);
        var pitch = new double[frames];
        var energy = new double[frames];
        const int tauMin = Rate / 450, tauMax = Rate / 70;
        var d = new double[tauMax + 1];
        double maxE = 1e-9;
        for (int f = 0; f < frames; f++)
        {
            int o = f * Hop;
            double e = 0;
            for (int i = 0; i < Win; i++) e += x[o + i] * x[o + i];
            energy[f] = Math.Sqrt(e / Win);
            maxE = Math.Max(maxE, energy[f]);
        }
        for (int f = 0; f < frames; f++)
        {
            pitch[f] = double.NaN;
            if (energy[f] < maxE * 0.08) continue;
            int o = f * Hop;
            int w = Win - tauMax;
            // YIN: разностная функция + кумулятивная нормализация
            for (int tau = 1; tau <= tauMax; tau++)
            {
                double s = 0;
                for (int i = 0; i < w; i++) { double v = x[o + i] - x[o + i + tau]; s += v * v; }
                d[tau] = s;
            }
            double run = 0; d[0] = 1;
            for (int tau = 1; tau <= tauMax; tau++) { run += d[tau]; d[tau] = d[tau] * tau / Math.Max(run, 1e-12); }
            int best = -1;
            for (int tau = tauMin; tau < tauMax; tau++)
                if (d[tau] < 0.2) { while (tau + 1 < tauMax && d[tau + 1] < d[tau]) tau++; best = tau; break; }
            if (best < 0) continue;
            // Параболическая интерполяция для точности
            double a = d[best - 1], b = d[best], c = d[best + 1];
            double shift = (a - c) / (2 * (a - 2 * b + c) + 1e-12);
            double hz = Rate / (best + Math.Clamp(shift, -1, 1));
            pitch[f] = 12 * Math.Log2(hz / 100.0);
        }
        // Медианный фильтр убирает октавные скачки
        var sm = (double[])pitch.Clone();
        for (int f = 2; f < frames - 2; f++)
        {
            var win = new[] { pitch[f - 2], pitch[f - 1], pitch[f], pitch[f + 1], pitch[f + 2] }.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToArray();
            if (!double.IsNaN(pitch[f]) && win.Length >= 3) sm[f] = win[win.Length / 2];
        }
        return (sm, energy);
    }

    /// <summary>Обрезка по голосовым кадрам, нормализация к медиане, заполнение пробелов, ресемплинг до n точек.</summary>
    static double[] Contour(double[] pitch, int n)
    {
        int first = Array.FindIndex(pitch, v => !double.IsNaN(v)), last = Array.FindLastIndex(pitch, v => !double.IsNaN(v));
        if (first < 0 || last - first < 5) return Array.Empty<double>();
        var span = pitch[first..(last + 1)];
        var voiced = span.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToArray();
        double med = voiced[voiced.Length / 2];
        // Линейная интерполяция глухих участков
        var filled = new double[span.Length];
        int prev = -1;
        for (int i = 0; i < span.Length; i++)
        {
            if (double.IsNaN(span[i])) continue;
            if (prev >= 0 && i - prev > 1)
                for (int k = prev + 1; k < i; k++) filled[k] = span[prev] + (span[i] - span[prev]) * (k - prev) / (i - prev) - med;
            filled[i] = span[i] - med;
            prev = i;
        }
        var outp = new double[n];
        for (int i = 0; i < n; i++)
        {
            double pos = (double)i * (filled.Length - 1) / (n - 1);
            int i0 = (int)pos; int i1 = Math.Min(filled.Length - 1, i0 + 1);
            outp[i] = filled[i0] + (filled[i1] - filled[i0]) * (pos - i0);
        }
        return outp;
    }

    static double[] EnvelopeShape(double[] energy, int n)
    {
        double max = energy.Length == 0 ? 0 : energy.Max();
        if (max <= 0) return new double[n];
        int first = Array.FindIndex(energy, v => v > max * 0.1), last = Array.FindLastIndex(energy, v => v > max * 0.1);
        if (first < 0 || last <= first) return new double[n];
        var outp = new double[n];
        for (int i = 0; i < n; i++)
        {
            int idx = first + (int)((double)i * (last - first) / (n - 1));
            outp[i] = energy[idx] / max;
        }
        return outp;
    }

    static double SpeechSeconds(double[] energy)
    {
        double max = energy.Length == 0 ? 0 : energy.Max();
        int first = Array.FindIndex(energy, v => v > max * 0.1), last = Array.FindLastIndex(energy, v => v > max * 0.1);
        return first < 0 ? 0 : (last - first) * Hop / (double)Rate;
    }

    /// <summary>DTW-расстояние со штрафом за перекос (окно Сакоэ-Чибы 20%).</summary>
    static double Dtw(double[] a, double[] b)
    {
        int n = a.Length, m = b.Length, band = Math.Max(4, (int)(Math.Max(n, m) * 0.2));
        var D = new double[n + 1, m + 1];
        for (int i = 0; i <= n; i++) for (int j = 0; j <= m; j++) D[i, j] = double.PositiveInfinity;
        D[0, 0] = 0;
        for (int i = 1; i <= n; i++)
            for (int j = Math.Max(1, i - band); j <= Math.Min(m, i + band); j++)
                D[i, j] = Math.Abs(a[i - 1] - b[j - 1]) + Math.Min(D[i - 1, j - 1], Math.Min(D[i - 1, j] + 0.3, D[i, j - 1] + 0.3));
        return D[n, m] / (n + m) * 2;
    }

    static double Std(double[] v) { if (v.Length == 0) return 0; double m = v.Average(); return Math.Sqrt(v.Average(x => (x - m) * (x - m))); }

    public static ProsodyResult? Compare(float[] reference, float[] user)
    {
        try
        {
            const int N = 80;
            var (rp, re) = Analyze(reference);
            var (up, ue) = Analyze(user);
            var rc = Contour(rp, N); var uc = Contour(up, N);
            if (rc.Length == 0 || uc.Length == 0) return null;

            // Интонация: средняя ошибка в полутонах после DTW
            double err = Dtw(rc, uc);
            int inton = (int)Math.Round(Math.Clamp(100 - (err - 0.8) * 18, 0, 100));

            // Ритм: темп + форма огибающей
            double rs = SpeechSeconds(re), us = SpeechSeconds(ue);
            double ratio = rs > 0 ? us / rs : 1;
            double tempo = Math.Clamp(100 - Math.Abs(Math.Log(ratio)) * 140, 0, 100);
            double envErr = Dtw(EnvelopeShape(re, N), EnvelopeShape(ue, N));
            double env = Math.Clamp(100 - envErr * 260, 0, 100);
            int rhythm = (int)Math.Round(tempo * 0.55 + env * 0.45);

            var tips = new List<string>();
            double rRange = Std(rc), uRange = Std(uc);
            if (inton < 70 && uRange < rRange * 0.6) tips.Add("Говоришь слишком ровно — у носителя мелодика ярче: следи за повышением и понижением тона.");
            else if (inton < 70 && uRange > rRange * 1.6) tips.Add("Тон слишком «скачет» — японская интонация спокойнее, перепады мягкие.");
            else if (inton < 70) tips.Add("Мелодия отличается от образца: посмотри на график и повтори, повторяя подъёмы и спады.");
            double rEnd = rc[^8..].Average() - rc[^20..^8].Average(), uEnd = uc[^8..].Average() - uc[^20..^8].Average();
            if (rEnd > 1.5 && uEnd < 0.3) tips.Add("В конце образца тон поднимается (вопрос) — подними голос на последнем слоге.");
            if (rEnd < -1.5 && uEnd > 0.5) tips.Add("В конце фразы тон должен опускаться, а у тебя поднимается.");
            if (ratio > 1.35) tips.Add("Говоришь заметно медленнее образца — попробуй в естественном темпе.");
            else if (ratio < 0.75) tips.Add("Слишком быстро: долгие гласные и паузы っ нужно выдерживать полностью.");
            if (env < 60 && tempo > 70) tips.Add("Ритм слогов отличается — каждая мора должна звучать примерно одинаково по длине.");
            return new ProsodyResult(inton, rhythm, rc, uc, ratio, tips);
        }
        catch { return null; }
    }
}
