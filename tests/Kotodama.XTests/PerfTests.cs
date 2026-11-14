using System.Diagnostics;
using Matsubakira.Services;
using Xunit.Abstractions;

namespace Kotodama.Tests;

/// <summary>Замеры (раздел 6): печатаются в вывод теста, проверяется только разумная граница.</summary>
public class PerfTests(ITestOutputHelper output)
{
    [Fact]
    public async Task DictionarySearchIsFast()
    {
        await JDict.LoadAsync();
        foreach (var q in new[] { "たべ", "食", "taberu", "кошка" }) JDict.Search(q); // прогрев
        var sw = Stopwatch.StartNew();
        int n = 0;
        foreach (var q in new[] { "たべ", "食べる", "taberu", "кошка", "日本", "がっこう", "zzz", "見" }) { JDict.Search(q); n++; }
        double ms = sw.Elapsed.TotalMilliseconds / n;
        output.WriteLine($"JDict.Search: {ms:0.0} мс на запрос");
        Assert.True(ms < 500);
    }
}
