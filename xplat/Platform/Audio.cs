using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Matsubakira.Platform;

/// <summary>Декодирование и обработка звука без зависимостей от ОС: MP3 (NLayer), WAV PCM16, ресемплинг, замедление.</summary>
public static class Pcm
{
    /// <summary>Файл → моно float + частота. MP3 декодируется управляемым кодом, поэтому работает и в Linux.</summary>
    public static (float[] x, int rate) Decode(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Decode(bytes, path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase));
    }

    public static (float[] x, int rate) Decode(byte[] data, bool? isWav = null)
    {
        bool wav = isWav ?? (data.Length > 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F');
        return wav ? DecodeWav(data) : DecodeMp3(data);
    }

    static (float[], int) DecodeMp3(byte[] data)
    {
        using var mpeg = new NLayer.MpegFile(new MemoryStream(data));
        int ch = Math.Max(1, mpeg.Channels), rate = mpeg.SampleRate;
        var all = new List<float>(rate * 4);
        var buf = new float[4096 * ch];
        int n;
        while ((n = mpeg.ReadSamples(buf, 0, buf.Length)) > 0)
            for (int i = 0; i + ch - 1 < n; i += ch)
            {
                float s = 0;
                for (int c = 0; c < ch; c++) s += buf[i + c];
                all.Add(s / ch);
            }
        return (all.ToArray(), rate);
    }

    /// <summary>WAV PCM16 (любое число каналов) — ищем чанки fmt и data, а не полагаемся на 44-байтный заголовок.</summary>
    static (float[], int) DecodeWav(byte[] b)
    {
        int rate = 16000, ch = 1, bits = 16, pos = 12, dataOff = 44, dataLen = Math.Max(0, b.Length - 44);
        while (pos + 8 <= b.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
            int len = BitConverter.ToInt32(b, pos + 4);
            if (id == "fmt " && pos + 24 <= b.Length)
            {
                ch = Math.Max(1, (int)BitConverter.ToInt16(b, pos + 10));
                rate = BitConverter.ToInt32(b, pos + 12);
                bits = BitConverter.ToInt16(b, pos + 22);
            }
            else if (id == "data") { dataOff = pos + 8; dataLen = Math.Min(len < 0 ? int.MaxValue : len, b.Length - dataOff); break; }
            pos += 8 + Math.Max(0, len) + (len & 1);
        }
        if (bits != 16) return (Array.Empty<float>(), rate);
        int frames = dataLen / (2 * ch);
        var x = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            float s = 0;
            for (int c = 0; c < ch; c++) s += BitConverter.ToInt16(b, dataOff + (i * ch + c) * 2);
            x[i] = s / ch / 32768f;
        }
        return (x, rate);
    }

    /// <summary>Линейный ресемплинг с простым сглаживанием при понижении частоты (для анализа и Whisper хватает).</summary>
    public static float[] Resample(float[] x, int from, int to)
    {
        if (from == to || x.Length == 0) return x;
        if (to < from)
        {
            // скользящее среднее как дешёвый фильтр от наложения спектра
            int k = Math.Max(1, (int)Math.Round((double)from / to));
            if (k > 1)
            {
                var y = new float[x.Length]; double acc = 0;
                for (int i = 0; i < x.Length; i++) { acc += x[i]; if (i >= k) acc -= x[i - k]; y[i] = (float)(acc / Math.Min(i + 1, k)); }
                x = y;
            }
        }
        int n = (int)((long)x.Length * to / from);
        var r = new float[n];
        double step = (double)from / to;
        for (int i = 0; i < n; i++)
        {
            double p = i * step; int a = (int)p; double f = p - a;
            float v0 = x[Math.Min(a, x.Length - 1)], v1 = x[Math.Min(a + 1, x.Length - 1)];
            r[i] = (float)(v0 + (v1 - v0) * f);
        }
        return r;
    }

    public static byte[] ToPcm16(float[] x)
    {
        var b = new byte[x.Length * 2];
        for (int i = 0; i < x.Length; i++)
        {
            int v = (int)Math.Round(Math.Clamp(x[i], -1f, 1f) * 32767);
            b[i * 2] = (byte)(v & 0xFF); b[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return b;
    }

    public static byte[] ToWav(float[] x, int rate)
    {
        var pcm = ToPcm16(x);
        using var ms = new MemoryStream(44 + pcm.Length);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + pcm.Length); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(pcm.Length); w.Write(pcm);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Замедление без изменения высоты голоса (WSOLA): окна по 40 мс накладываются с поиском
    /// наилучшего совпадения формы волны, поэтому речь не «плывёт» и не звучит роботом.
    /// speed &lt; 1 — медленнее.
    /// </summary>
    public static float[] TimeStretch(float[] x, int rate, double speed)
    {
        if (Math.Abs(speed - 1) < 0.01 || x.Length < rate / 10) return x;
        int win = rate * 40 / 1000, hop = win / 2, search = rate * 10 / 1000;
        double inHop = hop * speed;
        int outLen = (int)(x.Length / speed) + win;
        var y = new float[outLen];
        var norm = new float[outLen];
        var w = new float[win];
        for (int i = 0; i < win; i++) w[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (win - 1)));
        int outPos = 0, prevIn = 0;
        for (double inPos = 0; (int)inPos + win + search < x.Length; inPos += inHop, outPos += hop)
        {
            int best = (int)inPos;
            if (outPos > 0)
            {
                // ищем сдвиг, при котором новое окно лучше всего продолжает предыдущее
                int natural = prevIn + hop; double bestC = double.MinValue;
                for (int d = -search; d <= search; d += 2)
                {
                    int c = (int)inPos + d;
                    if (c < 0 || c + win >= x.Length || natural + win >= x.Length) continue;
                    double s = 0;
                    for (int i = 0; i < win; i += 4) s += x[c + i] * x[natural + i];
                    if (s > bestC) { bestC = s; best = c; }
                }
            }
            for (int i = 0; i < win && outPos + i < outLen; i++) { y[outPos + i] += x[best + i] * w[i]; norm[outPos + i] += w[i]; }
            prevIn = best;
        }
        int len = Math.Min(outLen, outPos + win);
        var r = new float[len];
        for (int i = 0; i < len; i++) r[i] = norm[i] > 1e-3f ? y[i] / norm[i] : 0;
        return r;
    }
}

/// <summary>Один канал воспроизведения (озвучка и звуки интерфейса — разные экземпляры, не перебивают друг друга).</summary>
public sealed class AudioPlayer
{
    /// <summary>Только для отладочной проверки: играть без звука.</summary>
    public static bool Muted;
    readonly object _lock = new();
    IPlayback? _cur;

    public Task<bool> PlayAsync(float[] x, int rate)
    {
        Stop();
        var pb = OperatingSystem.IsWindows() ? (IPlayback)new WinPlayback() : new LinuxPlayback();
        lock (_lock) _cur = pb;
        return pb.PlayAsync(x, rate);
    }

    public void Stop()
    {
        IPlayback? p;
        lock (_lock) { p = _cur; _cur = null; }
        try { p?.Stop(); } catch { }
    }

    interface IPlayback { Task<bool> PlayAsync(float[] x, int rate); void Stop(); }

    /// <summary>Windows: NAudio WaveOut (winmm) — без WinRT, работает на любом .NET.</summary>
    sealed class WinPlayback : IPlayback
    {
        NAudio.Wave.WaveOutEvent? _out;
        readonly TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool _stopped;

        public Task<bool> PlayAsync(float[] x, int rate)
        {
            try
            {
                var pcm = Pcm.ToPcm16(x);
                var src = new NAudio.Wave.RawSourceWaveStream(new MemoryStream(pcm), new NAudio.Wave.WaveFormat(rate, 16, 1));
                _out = new NAudio.Wave.WaveOutEvent { DesiredLatency = 120 };
                if (Muted) _out.Volume = 0;
                _out.PlaybackStopped += (_, _) => { _tcs.TrySetResult(!_stopped); try { _out?.Dispose(); } catch { } };
                _out.Init(src);
                _out.Play();
            }
            catch { _tcs.TrySetResult(false); }
            return _tcs.Task;
        }

        public void Stop() { _stopped = true; try { _out?.Stop(); } catch { } _tcs.TrySetResult(false); }
    }

    /// <summary>Linux: PulseAudio/PipeWire (paplay, pw-play) или ALSA (aplay); звук подаётся в stdin.</summary>
    sealed class LinuxPlayback : IPlayback
    {
        Process? _p;
        bool _stopped;

        public async Task<bool> PlayAsync(float[] x, int rate)
        {
            var cmd = Tools.Player(rate);
            if (cmd == null) return false;
            try
            {
                _p = Process.Start(new ProcessStartInfo(cmd.Value.exe, cmd.Value.args) { RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true });
                if (_p == null) return false;
                var pcm = Pcm.ToPcm16(x);
                await _p.StandardInput.BaseStream.WriteAsync(pcm);
                _p.StandardInput.Close();
                await _p.WaitForExitAsync();
                return !_stopped && _p.ExitCode == 0;
            }
            catch { return false; }
        }

        public void Stop() { _stopped = true; try { if (_p is { HasExited: false }) _p.Kill(); } catch { } }
    }
}

/// <summary>Источник звука с микрофона: 16 кГц, 16 бит, моно, кусками по ~40 мс.</summary>
public interface IMic : IDisposable
{
    event Action<byte[], int>? Data;
    event Action<Exception?>? Stopped;
    void Start();
    void Stop();
}

public static class Mic
{
    public static IMic Create(int device) => OperatingSystem.IsWindows() ? new WinMic(device) : new LinuxMic();

    public static bool Available => OperatingSystem.IsWindows() ? SafeCount() > 0 : Tools.Recorder() != null;

    public static List<string> Devices()
    {
        var list = new List<string>();
        if (!OperatingSystem.IsWindows()) return list;
        try { for (int i = 0; i < NAudio.Wave.WaveInEvent.DeviceCount; i++) list.Add(NAudio.Wave.WaveInEvent.GetCapabilities(i).ProductName); } catch { }
        return list;
    }

    static int SafeCount() { try { return NAudio.Wave.WaveInEvent.DeviceCount; } catch { return 0; } }

    sealed class WinMic : IMic
    {
        readonly NAudio.Wave.WaveInEvent _in;
        public event Action<byte[], int>? Data;
        public event Action<Exception?>? Stopped;

        public WinMic(int dev)
        {
            _in = new NAudio.Wave.WaveInEvent
            {
                WaveFormat = new NAudio.Wave.WaveFormat(16000, 16, 1), BufferMilliseconds = 40, NumberOfBuffers = 4,
                // -1 = микрофон, выбранный по умолчанию в Windows
                DeviceNumber = dev >= 0 && dev < SafeCount() ? dev : -1,
            };
            _in.DataAvailable += (_, e) => Data?.Invoke(e.Buffer, e.BytesRecorded);
            _in.RecordingStopped += (_, e) => Stopped?.Invoke(e.Exception);
        }

        public void Start() => _in.StartRecording();
        public void Stop() { try { _in.StopRecording(); } catch { Stopped?.Invoke(null); } }
        public void Dispose() { try { _in.Dispose(); } catch { } }
    }

    /// <summary>Linux: parecord / pw-record / arecord пишут сырой PCM в stdout, мы читаем его кусками.</summary>
    sealed class LinuxMic : IMic
    {
        Process? _p;
        volatile bool _stop;
        public event Action<byte[], int>? Data;
        public event Action<Exception?>? Stopped;

        public void Start()
        {
            var cmd = Tools.Recorder() ?? throw new InvalidOperationException("Не найдена программа записи звука (parecord, pw-record или arecord). Установи пакет pulseaudio-utils, pipewire или alsa-utils.");
            _p = Process.Start(new ProcessStartInfo(cmd.exe, cmd.args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })
                 ?? throw new InvalidOperationException("Не удалось запустить запись звука");
            var stream = _p.StandardOutput.BaseStream;
            new Thread(() =>
            {
                Exception? err = null;
                try
                {
                    var buf = new byte[1280]; // 40 мс
                    while (!_stop)
                    {
                        int got = 0;
                        while (got < buf.Length && !_stop)
                        {
                            int n = stream.Read(buf, got, buf.Length - got);
                            if (n <= 0) { _stop = true; break; }
                            got += n;
                        }
                        if (got > 0) Data?.Invoke(buf, got - (got & 1));
                    }
                }
                catch (Exception ex) { err = _stop ? null : ex; }
                try { if (_p is { HasExited: false }) _p.Kill(); } catch { }
                Stopped?.Invoke(err);
            }) { IsBackground = true, Name = "mic" }.Start();
        }

        public void Stop() { _stop = true; try { if (_p is { HasExited: false }) _p.Kill(); } catch { } }
        public void Dispose() => Stop();
    }
}

/// <summary>Поиск консольных аудиоутилит Linux в PATH.</summary>
static class Tools
{
    static readonly Dictionary<string, string?> Found = new();

    static string? Which(string name)
    {
        lock (Found)
        {
            if (Found.TryGetValue(name, out var v)) return v;
            v = (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':')
                .Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);
            Found[name] = v;
            return v;
        }
    }

    public static (string exe, string args)? Player(int rate)
    {
        if (Which("paplay") is { } pa) return (pa, $"--raw --rate={rate} --channels=1 --format=s16le --latency-msec=60");
        if (Which("pw-play") is { } pw) return (pw, $"--rate {rate} --channels 1 --format s16 -");
        if (Which("aplay") is { } ap) return (ap, $"-q -f S16_LE -r {rate} -c 1 -t raw -");
        return null;
    }

    public static (string exe, string args)? Recorder()
    {
        if (Which("parecord") is { } pa) return (pa, "--raw --rate=16000 --channels=1 --format=s16le --latency-msec=40");
        if (Which("pw-record") is { } pw) return (pw, "--rate 16000 --channels 1 --format s16 -");
        if (Which("arecord") is { } ar) return (ar, "-q -f S16_LE -r 16000 -c 1 -t raw");
        return null;
    }
}
