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

public class WordFormsTests
{
    [Fact]
    public void VerbAndAdjectiveForms()
    {
        var v = Matsubakira.Views.WordPage.Forms("食べる", "たべる", "есть");
        Assert.Contains(v, f => f.form == "食べます");
        Assert.Equal("たべられる", Matsubakira.Views.WordPage.FormKana("食べる", "たべる", "食べられる"));
        Assert.Equal("きます", Matsubakira.Views.WordPage.FormKana("来る", "くる", "来ます"));
        var a = Matsubakira.Views.WordPage.Forms("高い", "たかい", "высокий");
        Assert.Contains(a, f => f.form == "高くない");
        Assert.Contains(Matsubakira.Views.WordPage.Forms("いい", "いい", "хороший"), f => f.form == "よかった");
        Assert.Empty(Matsubakira.Views.WordPage.Forms("机", "つくえ", "стол"));
        Assert.Empty(Matsubakira.Views.WordPage.Forms("きれい", "きれい", "красивый"));
    }
}
