using Matsubakira.Services;

namespace Kotodama.Tests;

/// <summary>Версия 2.5: числа, время, даты, цены.</summary>
public class V25Tests
{
    [Theory]
    [InlineData(1, "いち")]
    [InlineData(11, "じゅういち")]
    [InlineData(300, "さんびゃく")]
    [InlineData(600, "ろっぴゃく")]
    [InlineData(800, "はっぴゃく")]
    [InlineData(3000, "さんぜん")]
    [InlineData(8000, "はっせん")]
    [InlineData(3450, "さんぜんよんひゃくごじゅう")]
    [InlineData(10000, "いちまん")]
    [InlineData(10_000_000, "いっせんまん")]
    [InlineData(25_000_000, "にせんごひゃくまん")]
    [InlineData(100_000_000, "いちおく")]
    public void ReadsNumbers(long n, string kana) => Assert.Equal(kana, Numbers.Read(n));

    [Fact]
    public void ReadsTimeDatesPrices()
    {
        Assert.Equal("よじ", Numbers.Time(4, 0));
        Assert.Equal("くじじゅっぷん", Numbers.Time(9, 10));
        Assert.Equal("しちじはん", Numbers.Time(7, 30, true));
        Assert.Equal("にじゅうごふん", Numbers.Minutes(25));
        Assert.Equal("さんじゅっぷん", Numbers.Minutes(30));
        Assert.Equal("しがつついたち", Numbers.Date(4, 1));
        Assert.Equal("はつか", Numbers.Day(20));
        Assert.Equal("にじゅういちにち", Numbers.Day(21));
        Assert.Equal("じゅうよえん", Numbers.Yen(14));
        Assert.Equal("さんびゃくえん", Numbers.Yen(300));
        Assert.Equal("三千四百五十", Numbers.Kanji(3450));
        Assert.Equal("3450", Numbers.DigitsOnly("３,450 円"));
    }
}

public class EdgeTokenTests
{
    // эталон — edge-tts 7.x (drm.generate_sec_ms_gec_v2)
    [Theory]
    [InlineData(1700000000, "2CD23A2CA0B2595A-F3C846C370E05F9CC83ACC5F995C5F5D87A4D0F14CE8936A0F902E64099E38A2")]
    [InlineData(1763030000, "C0C0E439525AD052-71FF575FD29D9D560D0D4A8A23F688CCFB64515DB6A875E49A93FAD8E7F35B6D")]
    [InlineData(1800000123.5, "99E54519B490BE5C-E162DB0977FBDCB9B86DD8148BF1BCDC3C0303176CE39852A42B415935C20654")]
    public void MatchesReference(double t, string expected) => Assert.Equal(expected, EdgeToken.Gec(t));
}
