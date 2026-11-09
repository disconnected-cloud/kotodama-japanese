using Android.Media;
using Android.Speech.Tts;
using Matsubakira.Platform;
using Matsubakira.Services;

namespace Matsubakira.Droid;

/// <summary>Воспроизведение через AudioTrack: PCM 16 бит моно, весь звук сразу в буфер (фразы короткие).</summary>
sealed class AndroidPlayback : IPlayback
{
    AudioTrack? _t;
    volatile bool _stop;

    public async Task<bool> PlayAsync(float[] x, int rate)
    {
        var pcm = Pcm.ToPcm16(x);
        if (pcm.Length == 0) return false;
        try
        {
            _t = new AudioTrack.Builder()
                .SetAudioAttributes(new AudioAttributes.Builder().SetUsage(AudioUsageKind.Media)!.SetContentType(AudioContentType.Speech)!.Build()!)
                .SetAudioFormat(new AudioFormat.Builder().SetEncoding(Encoding.Pcm16bit)!.SetSampleRate(rate)!.SetChannelMask(ChannelOut.Mono)!.Build()!)
                .SetBufferSizeInBytes(pcm.Length)
                .SetTransferMode(AudioTrackMode.Static)
                .Build();
            _t.Write(pcm, 0, pcm.Length);
            _t.Play();
            int frames = pcm.Length / 2;
            var deadline = DateTime.UtcNow.AddSeconds(frames / (double)rate + 2);
            while (!_stop && _t.PlaybackHeadPosition < frames && DateTime.UtcNow < deadline) await Task.Delay(25);
            return !_stop;
        }
        catch { return false; }
        finally { try { _t?.Stop(); _t?.Release(); } catch { } _t = null; }
    }

    public void Stop() { _stop = true; try { _t?.Pause(); } catch { } }
}

/// <summary>Микрофон через AudioRecord: 16 кГц моно, источник «распознавание речи» (без лишней обработки звука).</summary>
sealed class AndroidMic : IMic
{
    AudioRecord? _rec;
    volatile bool _stop;
    public event Action<byte[], int>? Data;
    public event Action<Exception?>? Stopped;

    public void Start()
    {
        int min = AudioRecord.GetMinBufferSize(16000, ChannelIn.Mono, Encoding.Pcm16bit);
        _rec = new AudioRecord(AudioSource.VoiceRecognition, 16000, ChannelIn.Mono, Encoding.Pcm16bit, Math.Max(min, 16000));
        if (_rec.State != State.Initialized)
            throw new InvalidOperationException("Нет доступа к микрофону. Разреши его: Настройки телефона → Приложения → Kotodama → Разрешения.");
        _rec.StartRecording();
        new Thread(() =>
        {
            Exception? err = null;
            try
            {
                var buf = new byte[1280]; // 40 мс
                while (!_stop)
                {
                    int n = _rec.Read(buf, 0, buf.Length);
                    if (n > 0) Data?.Invoke(buf, n);
                    else if (n < 0) break;
                }
            }
            catch (Exception ex) { err = ex; }
            try { _rec.Stop(); _rec.Release(); } catch { }
            Stopped?.Invoke(err);
        }) { IsBackground = true, Name = "mic" }.Start();
    }

    public void Stop() => _stop = true;
    public void Dispose() => Stop();
}

/// <summary>Системный голос Android (Google, Samsung…) — когда онлайн-голос недоступен. Синтез в WAV-файл.</summary>
static class AndroidTts
{
    static TextToSpeech? _tts;
    static TaskCompletionSource<bool>? _ready;
    static readonly SemaphoreSlim Gate = new(1, 1);

    sealed class InitListener : Java.Lang.Object, TextToSpeech.IOnInitListener
    {
        public void OnInit(OperationResult status) => _ready?.TrySetResult(status == OperationResult.Success);
    }

    sealed class DoneListener : UtteranceProgressListener
    {
        readonly TaskCompletionSource<bool> _tcs;
        public DoneListener(TaskCompletionSource<bool> tcs) => _tcs = tcs;
        public override void OnStart(string? utteranceId) { }
        public override void OnDone(string? utteranceId) => _tcs.TrySetResult(true);
        [Obsolete] public override void OnError(string? utteranceId) => _tcs.TrySetResult(false);
    }

    public static async Task<string?> SynthesizeAsync(string text, bool slow)
    {
        await Gate.WaitAsync();
        try
        {
            if (_tts == null)
            {
                _ready = new TaskCompletionSource<bool>();
                _tts = new TextToSpeech(Android.App.Application.Context, new InitListener());
                if (!await _ready.Task.WaitAsync(TimeSpan.FromSeconds(8))) return null;
                var r = _tts.SetLanguage(Java.Util.Locale.Japan);
                if (r is LanguageAvailableResult.MissingData or LanguageAvailableResult.NotSupported) return null;
            }
            Directory.CreateDirectory(Tts.CacheDir);
            var path = Path.Combine(Tts.CacheDir, $"sys-{(slow ? "s" : "n")}-{(uint)text.GetHashCode():x8}.wav");
            if (File.Exists(path) && new FileInfo(path).Length > 1000) return path;
            _tts.SetSpeechRate(slow ? 0.7f : 1.0f);
            var tcs = new TaskCompletionSource<bool>();
            _tts.SetOnUtteranceProgressListener(new DoneListener(tcs));
            _tts.SynthesizeToFile(text, null, new Java.IO.File(path), "kotodama");
            bool ok = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
            return ok && File.Exists(path) ? path : null;
        }
        catch { return null; }
        finally { Gate.Release(); }
    }
}
