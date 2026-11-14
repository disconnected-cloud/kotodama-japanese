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
