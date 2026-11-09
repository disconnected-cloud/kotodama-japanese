using Matsubakira.Platform;

namespace Kotodama.Tests;

/// <summary>Кроссплатформенный звук: WAV туда-обратно, ресемплинг, замедление без изменения высоты.</summary>
public class AudioTests
{
    static float[] Sine(double hz, int rate, double sec) =>
        Enumerable.Range(0, (int)(rate * sec)).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * hz * i / rate))).ToArray();

    [Fact]
    public void WavRoundTrip()
    {
        var x = Sine(440, 16000, 0.5);
        var (y, rate) = Pcm.Decode(Pcm.ToWav(x, 16000), true);
        Assert.Equal(16000, rate);
        Assert.Equal(x.Length, y.Length);
        Assert.True(Math.Abs(x[100] - y[100]) < 0.001);
    }

    [Fact]
    public void ResampleKeepsDuration()
    {
        var x = Sine(300, 24000, 1.0);
        var y = Pcm.Resample(x, 24000, 16000);
        Assert.InRange(y.Length, 15990, 16010);
    }

    [Fact]
    public void TimeStretchSlowsDownWithoutChangingPitch()
    {
        var x = Sine(220, 16000, 1.0);
        var y = Pcm.TimeStretch(x, 16000, 0.72);
        Assert.InRange(y.Length / 16000.0, 1.3, 1.5);               // длиннее в ~1/0,72 раза
        // высота тона: число пересечений нуля в секунду почти не меняется
        double Zc(float[] s) { int c = 0; for (int i = 1000; i < s.Length - 1000; i++) if (s[i - 1] < 0 && s[i] >= 0) c++; return c / ((s.Length - 2000) / 16000.0); }
        Assert.InRange(Zc(y), 205, 235);
    }

    [Fact]
    public void RecorderWavHeaderIsValid()
    {
        var wav = Matsubakira.Services.Recorder.ToWav(new byte[3200]);
        Assert.Equal(44 + 3200, wav.Length);
        Assert.Equal((byte)'R', wav[0]);
        var (y, rate) = Pcm.Decode(wav, true);
        Assert.Equal(16000, rate);
        Assert.Equal(1600, y.Length);
    }
}
