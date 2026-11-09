using System.IO;
using NAudio.Utils;
using NAudio.Wave;

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
    static readonly WaveFormat Fmt = new(16000, 16, 1);
    WaveInEvent? _in;
    readonly MemoryStream _pcm = new();
    TaskCompletionSource<Recording>? _tcs;
    DateTime _start, _lastLoud, _speechStart;
    bool _heard, _stopping;
    readonly Queue<float> _recent = new();   // уровни последних ~1,5 с для оценки шума
    float _noise = 0.004f;
    readonly bool _manual;
    readonly double _maxSeconds;
    readonly double _endSilence;

    /// <summary>Текущий уровень 0..1 (читается UI-таймером).</summary>
    public volatile float Level;
    public bool HeardSpeech => _heard;

    public static bool HasMicrophone
    {
        get { try { return WaveInEvent.DeviceCount > 0; } catch { return false; } }
    }

    public static List<string> Devices()
    {
        var list = new List<string>();
        try { for (int i = 0; i < WaveInEvent.DeviceCount; i++) list.Add(WaveInEvent.GetCapabilities(i).ProductName); } catch { }
        return list;
    }

    public Recorder(bool? manual = null, double maxSeconds = 0)
    {
        _manual = manual ?? ProgressStore.P.MicMode == "manual";
        _maxSeconds = maxSeconds > 0 ? maxSeconds : _manual ? 20 : 15;
        _endSilence = ProgressStore.P.MicPause;
    }

    public Task<Recording> RecordAsync(CancellationToken ct)
    {
        _tcs = new TaskCompletionSource<Recording>(TaskCreationOptions.RunContinuationsAsynchronously);
        int dev = ProgressStore.P.MicDevice;
        _in = new WaveInEvent
        {
            WaveFormat = Fmt, BufferMilliseconds = 40, NumberOfBuffers = 4,
            DeviceNumber = dev >= 0 && dev < WaveInEvent.DeviceCount ? dev : 0,
        };
        _in.DataAvailable += OnData;
        _in.RecordingStopped += (_, e) =>
        {
            if (e.Exception != null) _tcs.TrySetException(e.Exception);
            else _tcs.TrySetResult(Build());
        };
        ct.Register(Stop);
        _start = _lastLoud = DateTime.UtcNow;
        _in.StartRecording();
        return _tcs.Task;
    }

    public void Stop()
    {
        if (_stopping) return;
        _stopping = true;
        try { _in?.StopRecording(); } catch { _tcs?.TrySetResult(Build()); }
    }

    void OnData(object? s, WaveInEventArgs e)
    {
        _pcm.Write(e.Buffer, 0, e.BytesRecorded);
        double sum = 0;
        int n = e.BytesRecorded / 2;
        for (int i = 0; i < e.BytesRecorded; i += 2)
        {
            double v = BitConverter.ToInt16(e.Buffer, i) / 32768.0;
            sum += v * v;
        }
        float rms = n > 0 ? (float)Math.Sqrt(sum / n) : 0;
        Level = Math.Min(1f, rms * 7f);

        // Шум = 15-й перцентиль уровней за последние ~1,5 с (устойчиво к речи с первой секунды)
        _recent.Enqueue(rms);
        if (_recent.Count > 38) _recent.Dequeue();
        var sorted = _recent.OrderBy(x => x).ToArray();
        _noise = Math.Clamp(sorted[sorted.Length * 15 / 100], 0.0015f, 0.05f);
        float speechThr = Math.Max(0.012f, _noise * 3.2f);
        float silenceThr = speechThr * 0.65f; // гистерезис: тише порога речи ещё не значит «тишина»

        var now = DateTime.UtcNow;
        if (rms > speechThr)
        {
            if (!_heard) _speechStart = now;
            _heard = true;
            _lastLoud = now;
        }
        else if (rms > silenceThr && _heard) _lastLoud = now;

        double elapsed = (now - _start).TotalSeconds;
        if (elapsed > _maxSeconds) { Stop(); return; }
        if (_manual) return;
        bool longEnough = _heard && (now - _speechStart).TotalSeconds > 0.6;
        if ((longEnough && (now - _lastLoud).TotalSeconds > _endSilence) || (!_heard && elapsed > 8))
            Stop();
    }

    Recording Build()
    {
        var pcm = _pcm.ToArray();
        int samples = pcm.Length / 2;
        const int frame = 320; // 20 мс
        int frames = samples / frame;
        float thr = Math.Max(0.01f, _noise * 2.8f);
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
            float gain = Math.Min(8f, 0.7f / peak);
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
        using (var w = new WaveFileWriter(new IgnoreDisposeStream(ms), new WaveFormat(rate, 16, 1)))
            w.Write(pcm16, 0, pcm16.Length);
        return ms.ToArray();
    }

    public void Dispose()
    {
        try { _in?.Dispose(); } catch { }
        _pcm.Dispose();
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
            using var reader = new MediaFoundationReader(path);
            var sp = reader.ToSampleProvider();
            var all = new List<float>(reader.WaveFormat.SampleRate * 4);
            var buf = new float[8192];
            int ch = reader.WaveFormat.Channels, n;
            while ((n = sp.Read(buf, 0, buf.Length)) > 0)
                for (int i = 0; i < n; i += ch) all.Add(Math.Abs(buf[i]));
            int first = all.FindIndex(v => v > 0.02f), last = all.FindLastIndex(v => v > 0.02f);
            if (first < 0) return (new float[bins], 0);
            var span = all.GetRange(first, last - first + 1);
            var peaks = new float[bins];
            for (int b = 0; b < bins; b++)
            {
                int from = span.Count * b / bins, to = span.Count * (b + 1) / bins;
                float max = 0;
                for (int i = from; i < to; i++) max = Math.Max(max, span[i]);
                peaks[b] = max;
            }
            return (Normalize(peaks), span.Count / (double)reader.WaveFormat.SampleRate);
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
