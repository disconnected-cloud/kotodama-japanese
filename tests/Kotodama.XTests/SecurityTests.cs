using System.Text.Json;
using Matsubakira.Services;

namespace Kotodama.Tests;

/// <summary>Обновление (разбор ответа GitHub, контрольные суммы) и обезличивание отчёта об ошибке.</summary>
public class SecurityTests
{
    const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void ReleaseParsingFindsArchiveAndSums()
    {
        var json = """
            {"tag_name":"v9.1.0","body":"notes","assets":[
              {"name":"Kotodama-win-x64.zip","browser_download_url":"https://x/win.zip","size":10},
              {"name":"Kotodama-portable-win-x64.zip","browser_download_url":"https://x/p.zip","size":11},
              {"name":"Kotodama-linux-x64.tar.gz","browser_download_url":"https://x/l.tgz","size":12},
              {"name":"SHA256SUMS","browser_download_url":"https://x/sums","size":1}]}
            """;
        var r = Updater.Parse(JsonDocument.Parse(json).RootElement);
        Assert.NotNull(r);
        Assert.Equal(new Version(9, 1, 0), r!.Version);
        Assert.Equal("https://x/sums", r.SumsUrl);
        Assert.StartsWith("https://x/", r.Url);
    }

    [Fact]
    public void ReleaseParsingToleratesBrokenAnswers()
    {
        Assert.Null(Updater.Parse(JsonDocument.Parse("{}").RootElement));
        Assert.Null(Updater.Parse(JsonDocument.Parse("""{"tag_name":"latest","assets":[]}""").RootElement));
        Assert.Null(Updater.Parse(JsonDocument.Parse("""{"tag_name":"v9.0","assets":[{"name":"other.zip"}]}""").RootElement));
    }

    [Fact]
    public void HashLookupMatchesExactFileName()
    {
        var sums = $"{Hash}  Kotodama-win-x64.zip\n{Hash.Replace('0', 'f')} *Kotodama-portable-win-x64.zip\r\n";
        Assert.Equal(Hash, Updater.HashFor(sums, "Kotodama-win-x64.zip"));
        Assert.Equal(Hash.Replace('0', 'f'), Updater.HashFor(sums, "Kotodama-portable-win-x64.zip"));
        Assert.Null(Updater.HashFor(sums, "Kotodama-linux-x64.tar.gz"));
        Assert.Null(Updater.HashFor("abc  Kotodama-win-x64.zip", "Kotodama-win-x64.zip")); // не хэш
    }

    [Fact]
    public void CrashReportHidesUserName()
    {
        var s = CrashReport.Anonymize(@"at X in C:\Users\Ivan Petrov\src\a.cs and /home/ivan/app");
        Assert.DoesNotContain("Ivan", s);
        Assert.DoesNotContain("ivan", s);
        Assert.DoesNotContain("Petrov", s);
    }
}

public class DownloaderTests
{
    [Fact]
    public void EveryModelAndVoicePackHasPinnedHash()
    {
        foreach (var m in Asr.Models) Assert.True(Downloader.Sha256.ContainsKey(m.id.StartsWith("kotoba") ? "ggml-kotoba-whisper-v2.0-q5_0.bin" : $"ggml-{m.id}.bin"), m.id);
        foreach (var p in Voicevox.Catalog) Assert.True(Downloader.Sha256.ContainsKey(p.File), p.File);
        Assert.All(Downloader.Sha256.Values, h => Assert.Matches("^[0-9a-f]{64}$", h));
    }

    [Fact]
    public async Task UnknownFileIsRejectedWithoutDownloading()
    {
        var dest = Path.Combine(Path.GetTempPath(), "kd-unknown.bin");
        await Assert.ThrowsAsync<InvalidDataException>(() => Downloader.FileAsync("https://example.invalid/unknown.bin", dest, null, CancellationToken.None));
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public async Task VerifyDetectsTamperedFile()
    {
        var f = Path.GetTempFileName();
        await File.WriteAllTextAsync(f, "abc");
        Assert.True(await Downloader.Verify(f, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
        await File.WriteAllTextAsync(f, "abd");
        Assert.False(await Downloader.Verify(f, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
        File.Delete(f);
    }
}

public class NoNetworkTests
{
    [Fact]
    public async Task NoNetworkBlocksEveryRequest()
    {
        var saved = ProgressStore.P.NoNetwork;
        try
        {
            ProgressStore.P.NoNetwork = true;
            Assert.Null(await Updater.CheckAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => Downloader.FileAsync("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base-q5_1.bin", Path.Combine(Path.GetTempPath(), "kd-x.bin"), null, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Matsubakira.Views.ReaderPage.FetchJapanese(new Uri("https://example.com")));
        }
        finally { ProgressStore.P.NoNetwork = saved; }
    }
}
