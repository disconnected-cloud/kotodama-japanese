using System.IO;
using Matsubakira.Platform;

namespace Matsubakira.Services;

/// <summary>Результат записи: WAV 16 кГц моно (формат, который ждёт Whisper) + пики для формы волны.</summary>
public sealed record Recording(byte[] Wav, bool Heard, float[] Peaks, double Seconds, float PeakLevel = 0, float NoiseLevel = 0);

/// <summary>
/// Запись с микрофона через NAudio.
/// • Авто-режим: адаптивный порог шума (скользящий минимум, а не первые 300 мс), гистерезис,
///   остановка после 1,5 с тишины — не обрывает фразу на паузах и не «глохнет», если начать говорить сразу.
/// • Ручной режим: пишет, пока пользователь сам не остановит (до 20 с).
/// • Края обрезаются с запасом 250 мс, тихий голос автоматически усиливается.
/// </summary>
public sealed class Recorder : IDisposable
{
    IMic? _in;
    readonly MemoryStream _pcm = new();
    TaskCompletionSource<Recording>? _tcs;
    DateTime _start, _lastLoud, _speechStart;
    bool _heard, _stopping, _started, _noiseInit;
    int _loudRun;                            // сколько буферов подряд громче порога (щелчок ≠ речь)
    float _noise = 0.004f;

    /// <summary>Микрофон реально начал отдавать звук (Bluetooth-гарнитура может включаться до секунды) — можно говорить.</summary>
    public event Action? Started;
    readonly bool _manual;
    readonly double _maxSeconds;
    readonly double _endSilence;

    /// <summary>Текущий уровень 0..1 (читается UI-таймером).</summary>
    public volatile float Level;

    public static bool HasMicrophone
    {
        get { try { return Mic.Available; } catch { return false; } }
    }

    public static List<string> Devices() => Mic.Devices();

    public Recorder(bool? manual = null, double maxSeconds = 0)
    {
        _manual = manual ?? ProgressStore.P.MicMode == "manual";
        _maxSeconds = maxSeconds > 0 ? maxSeconds : _manual ? 20 : 15;
        _endSilence = ProgressStore.P.MicPause;
    }

    public Task<Recording> RecordAsync(CancellationToken ct)
    {
        _tcs = new TaskCompletionSource<Recording>(TaskCreationOptions.RunContinuationsAsynchronously);
        _in = Mic.Create(ProgressStore.P.MicDevice);
        _in.Data += OnData;
        _in.Stopped += ex =>
        {
            if (ex != null) _tcs.TrySetException(ex);
            else _tcs.TrySetResult(Build());
        };
        ct.Register(Stop);
        _start = _lastLoud = DateTime.UtcNow;
        _in.Start();
        return _tcs.Task;
    }

    public void Stop()
    {
        if (_stopping) return;
        _stopping = true;
        try { _in?.Stop(); } catch { _tcs?.TrySetResult(Build()); }
    }

    void OnData(byte[] buffer, int bytes)
    {
        lock (_pcm) _pcm.Write(buffer, 0, bytes);
        double sum = 0;
        int n = bytes / 2;
        for (int i = 0; i + 1 < bytes; i += 2)
        {
            double v = BitConverter.ToInt16(buffer, i) / 32768.0;
            sum += v * v;
        }
        float rms = n > 0 ? (float)Math.Sqrt(sum / n) : 0;
        Level = Math.Min(1f, (float)Math.Sqrt(rms) * 2.2f); // корень — тихий микрофон тоже заметно «шевелит» индикатор

        var now = DateTime.UtcNow;
        if (!_started)
        {
            _started = true; _start = _lastLoud = now; // отсчёт времени — с первого реального звука, а не с нажатия
            Started?.Invoke();
        }

        // Шум: быстро опускается к тишине и медленно растёт только на «не-речи».
        // Раньше брался перцентиль за 1,5 с — на длинной фразе он доходил до уровня голоса, и запись обрывалась.
        if (!_noiseInit) { _noise = Math.Clamp(rms, 0.001f, 0.01f); _noiseInit = true; }
        float speechThr = Math.Max(0.006f, _noise * 3f);
        if (rms < _noise) _noise = _noise * 0.6f + rms * 0.4f;
        else if (rms < speechThr) _noise = _noise * 0.97f + rms * 0.03f;
        _noise = Math.Clamp(_noise, 0.0008f, 0.05f);
        float silenceThr = speechThr * 0.6f; // гистерезис: тише порога речи ещё не значит «тишина»

        if (rms > speechThr)
        {
            if (++_loudRun == 1 && !_heard) _speechStart = now;
            if (_loudRun >= 2) _heard = true; // ≥ 80 мс подряд — это голос, а не щелчок
            _lastLoud = now;
        }
        else
        {
            if (!_heard) _loudRun = 0;
            if (rms > silenceThr && _heard) _lastLoud = now;
        }

        double elapsed = (now - _start).TotalSeconds;
        if (elapsed > _maxSeconds) { Stop(); return; }
        if (_manual) return;
        bool longEnough = _heard && (now - _speechStart).TotalSeconds > 0.5;
        if ((longEnough && (now - _lastLoud).TotalSeconds > _endSilence) || (!_heard && elapsed > 10))
            Stop();
    }

    Recording Build()
    {
        byte[] pcm; lock (_pcm) pcm = _pcm.ToArray();
        int samples = pcm.Length / 2;
        const int frame = 320; // 20 мс
        int frames = samples / frame;
        float thr = Math.Max(0.005f, _noise * 2.5f);
        int first = -1, last = -1;
        float peak = 0;
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            for (int i = 0; i < frame; i++)
            {
                float v = BitConverter.ToInt16(pcm, (f * frame + i) * 2) / 32768f;
                sum += v * v;
                peak = Math.Max(peak, Math.Abs(v));
            }
            if (Math.Sqrt(sum / frame) > thr) { if (first < 0) first = f; last = f; }
        }
        int startS = 0, endS = samples;
        if (first >= 0)
        {
            startS = Math.Max(0, (first - 12) * frame);          // 240 мс запаса до речи
            endS = Math.Min(samples, (last + 14) * frame);       // 280 мс после — не съедаем «су», «цу»
        }
        var trimmed = new byte[Math.Max(0, endS - startS) * 2];
        Buffer.BlockCopy(pcm, startS * 2, trimmed, 0, trimmed.Length);

        // Автоусиление тихого микрофона (до ×8), чтобы Whisper и анализ интонации получили нормальный уровень
        if (peak > 0.002f && peak < 0.45f)
        {
            float gain = Math.Min(10f, 0.7f / peak);
            for (int i = 0; i + 1 < trimmed.Length; i += 2)
            {
                int v = (int)(BitConverter.ToInt16(trimmed, i) * gain);
                v = Math.Clamp(v, short.MinValue, short.MaxValue);
                trimmed[i] = (byte)(v & 0xFF); trimmed[i + 1] = (byte)((v >> 8) & 0xFF);
            }
        }
        return new Recording(ToWav(trimmed), _heard, Waveform.PeaksFromPcm16(trimmed, 90), (endS - startS) / 16000.0, peak, _noise);
    }

    public static byte[] ToWav(byte[] pcm16, int rate = 16000)
    {
        using var ms = new MemoryStream(44 + pcm16.Length);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + pcm16.Length); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(pcm16.Length); w.Write(pcm16);
        w.Flush();
        return ms.ToArray();
    }

    public void Dispose()
    {
        try { _in?.Dispose(); } catch { } // уже освобождён — повторное освобождение не нужно
    }
}

/// <summary>Вычисление пиков формы волны для визуального сравнения «образец ↔ моя запись».</summary>
public static class Waveform
{
    public static float[] PeaksFromPcm16(byte[] pcm, int bins)
    {
        int samples = pcm.Length / 2;
        var peaks = new float[bins];
        if (samples == 0) return peaks;
        for (int b = 0; b < bins; b++)
        {
            int from = (int)((long)samples * b / bins), to = (int)((long)samples * (b + 1) / bins);
            float max = 0;
            for (int i = from; i < to; i++) max = Math.Max(max, Math.Abs(BitConverter.ToInt16(pcm, i * 2) / 32768f));
            peaks[b] = max;
        }
        return Normalize(peaks);
    }

    /// <summary>Декодирует образец (Media Foundation, встроен в Windows) и строит пики.</summary>
    public static (float[] peaks, double seconds) PeaksFromFile(string path, int bins)
    {
        try
        {
            var (all, rate) = Pcm.Decode(path);
            int first = Array.FindIndex(all, v => Math.Abs(v) > 0.02f), last = Array.FindLastIndex(all, v => Math.Abs(v) > 0.02f);
            if (first < 0) return (new float[bins], 0);
            int len = last - first + 1;
            var peaks = new float[bins];
            for (int b = 0; b < bins; b++)
            {
                int from = first + (int)((long)len * b / bins), to = first + (int)((long)len * (b + 1) / bins);
                float max = 0;
                for (int i = from; i < to; i++) max = Math.Max(max, Math.Abs(all[i]));
                peaks[b] = max;
            }
            return (Normalize(peaks), len / (double)rate);
        }
        catch { return (new float[bins], 0); }
    }

    static float[] Normalize(float[] p)
    {
        float max = p.Length == 0 ? 0 : p.Max();
        if (max > 0.0001f) for (int i = 0; i < p.Length; i++) p[i] /= max;
        return p;
    }
}
