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
