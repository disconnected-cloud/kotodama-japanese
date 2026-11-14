using Matsubakira.Platform;
using Matsubakira.Services;

namespace Kotodama.Tests;

/// <summary>Офлайн-озвучка: системный голос через COM (Windows) и отсутствие онлайн-сервисов в коде.</summary>
public class VoiceTests
{
    [Fact]
    public void SystemRussianVoiceSynthesizesWav()
    {
        if (!SystemVoice.HasRu) return; // на машине без русского голоса проверять нечего
        var wav = SystemVoice.SpeakRu("Проверка русского голоса");
        Assert.NotNull(wav);
        Assert.True(wav!.Length > 1000);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
    }

    [Fact]
    public void NoOnlineSpeechServicesInCode()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "xplat"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        foreach (var f in Directory.EnumerateFiles(Path.Combine(dir!, "xplat"), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("obj")))
        {
            var s = File.ReadAllText(f);
            foreach (var bad in new[] { "speech.platform.bing.com", "translate_tts", "TrustedClientToken" })
                Assert.False(s.Contains(bad), $"{Path.GetFileName(f)}: {bad}");
        }
    }

    [Fact]
    public void OldOnlineEngineSettingMigratesToOffline()
    {
        var p = new Progress { TtsEngine = "online" };
        ProgressStore.Prepare(p);
        Assert.Equal("offline", p.TtsEngine);
    }
}

public class HonestScoringTests
{
    static float[] Syllables(int seed, double seconds = 2)
    {
        var r = new Random(seed);
        var x = new float[(int)(16000 * seconds)];
        int i = 0;
        while (i < x.Length)
        {
            int len = r.Next(1600, 4800), gap = r.Next(800, 3200);
            float amp = (float)(0.2 + r.NextDouble() * 0.6);
            for (int j = 0; j < len && i < x.Length; j++, i++) x[i] = amp * (float)Math.Sin(i * 0.07) * (float)Math.Sin(Math.PI * j / len);
            i += gap;
        }
        return x;
    }

    [Fact]
    public void SpeakerEchoIsDetected()
    {
        var sample = Syllables(1);
        var echo = new float[sample.Length + 3200]; // задержка 0,2 с, тише и с шумом
        var r = new Random(5);
        for (int i = 0; i < sample.Length; i++) echo[i + 3200] = 0.3f * sample[i] + 0.002f * (float)(r.NextDouble() - 0.5);
        Assert.True(Echo.Similarity(echo, sample) >= Echo.EchoThreshold);
    }

    [Fact]
    public void DifferentSpeechIsNotEcho() => Assert.True(Echo.Similarity(Syllables(2), Syllables(1)) < Echo.EchoThreshold);
}
