using Matsubakira.Services;

namespace Kotodama.Tests;

/// <summary>Целостность учебных данных по всем уровням N5–N2: ничего пустого, битого или ссылающегося в никуда.</summary>
public class DataQualityTests
{
    static bool IsKana(char c) => c is >= 'ぁ' and <= 'ゖ' or >= 'ァ' and <= 'ヺ' or 'ー' or 'ゝ' or 'ゞ' or 'ヽ' or 'ヾ' or '・' or '〜' or ' ' or '、' or '。' or '？' or '！';

    [Theory]
    [InlineData("N5")] [InlineData("N4")] [InlineData("N3")] [InlineData("N2")] [InlineData("N1")]
    public void EveryLevelHasWordsAndKanji(string level)
    {
        Assert.True(Db.Words.Count(w => w.Level == level) >= 100, level);
        Assert.True(Db.Kanji.Count(k => k.Level == level) >= 80, level);
    }

    [Fact]
    public void WordsHaveKanaReadingAndRomaji() =>
        Assert.All(Db.Words, w =>
        {
            Assert.False(string.IsNullOrWhiteSpace(w.Jp), w.Id);
            Assert.True(w.Kana.Length > 0 && w.Kana.All(IsKana), $"{w.Jp}: чтение «{w.Kana}» не каной");
            Assert.False(string.IsNullOrWhiteSpace(w.Romaji), w.Jp);
        });

    [Fact]
    public void WordExamplesHaveTranslation() =>
        Assert.All(Db.Words.Where(w => w.ExJp.Length > 0), w => Assert.False(string.IsNullOrWhiteSpace(w.ExRu), w.Jp));

    [Fact]
    public void NoDuplicateWordsWithinLevel()
    {
        var dups = Db.Words.GroupBy(w => (w.Level, w.Jp, w.Kana)).Where(g => g.Count() > 1).Select(g => $"{g.Key.Level} {g.Key.Jp}").ToList();
        Assert.True(dups.Count == 0, string.Join(", ", dups.Take(10)));
    }

    [Fact]
    public void KanjiHaveMeaningReadingAndStrokes() =>
        Assert.All(Db.Kanji, k =>
        {
            Assert.False(string.IsNullOrWhiteSpace(k.Ru), k.Char);
            Assert.True(k.On.Length + k.Kun.Length > 0, $"{k.Char}: нет ни одного чтения");
            Assert.True(k.Strokes > 0, k.Char);
            Assert.NotNull(Db.Strokes(k.Char));
        });

    [Fact]
    public void KanjiStrokeCountMatchesStrokeOrder()
    {
        // число черт из KANJIDIC и из KanjiVG могут расходиться на одну (разные школы написания) — но не больше
        var bad = Db.Kanji.Where(k => Db.Strokes(k.Char) is { } s && Math.Abs(s.Length - k.Strokes) > 1).Select(k => $"{k.Char} {k.Strokes}≠{Db.Strokes(k.Char)!.Length}").ToList();
        Assert.True(bad.Count == 0, string.Join(", ", bad));
    }

    [Fact]
    public void KanjiExamplesAreComplete() =>
        Assert.All(Db.Kanji.SelectMany(k => k.Examples.Select(e => (k, e))), x =>
        {
            Assert.False(string.IsNullOrWhiteSpace(x.e.Kana), $"{x.k.Char}: {x.e.Jp}");
            Assert.False(string.IsNullOrWhiteSpace(x.e.Ru), $"{x.k.Char}: {x.e.Jp}");
        });

    [Fact]
    public void GrammarLessonsAreUniqueAndFull()
    {
        Assert.Equal(Db.Grammar.Length, Db.Grammar.Select(g => g.Id).Distinct().Count());
        Assert.Equal(Db.Grammar.Length, Db.Grammar.Select(g => g.Title).Distinct().Count());
        Assert.All(Db.Grammar, g =>
        {
            Assert.NotEmpty(g.Points);
            Assert.True(g.Quiz.Length >= 3, $"урок {g.Id}: мало упражнений");
            Assert.All(g.Points.SelectMany(p => p.Examples), e => Assert.False(string.IsNullOrWhiteSpace(e.Ru), e.Jp));
            Assert.All(g.Quiz, q => Assert.Equal(q.Options.Length, q.Options.Distinct().Count()));
        });
    }

    [Fact]
    public void DialogLinesUseExistingRoles() =>
        Assert.All(Db.Dialogs, d =>
        {
            Assert.Equal(d.Roles.Length, d.Male.Length);
            Assert.All(d.Lines, l =>
            {
                Assert.InRange(l.Speaker, 0, d.Roles.Length - 1);
                Assert.False(string.IsNullOrWhiteSpace(l.Ru), l.Jp);
                Assert.False(string.IsNullOrWhiteSpace(l.Kana), l.Jp);
            });
        });

    [Fact]
    public void CourseUnitsAreNumberedAndHaveTheory()
    {
        // Id — ключ прогресса (позиция в файле), Num — порядок на «Пути» (с 4.0 новые юниты вставлены внутрь уровней)
        Assert.Equal(Enumerable.Range(1, Db.Course.Length), Db.Course.Select(u => u.Id).OrderBy(i => i));
        Assert.Equal(Enumerable.Range(1, Db.Course.Length), Db.Course.Select(u => u.Num));
        Assert.All(Db.Course, u =>
        {
            Assert.NotEmpty(u.Theory);
            Assert.NotEmpty(u.Steps);
        });
        // курс доходит до N2
        Assert.Contains(Db.Course, u => u.Level == "N2");
    }
}

public class MistakeExplainTests
{
    [Fact]
    public void ParticleMixupIsExplained()
    {
        var why = Matsubakira.Services.MistakeExplain.For("が", "は");
        Assert.NotNull(why);
        Assert.Contains("«は»", why);
        Assert.Contains("«が»", why);
    }

    [Fact]
    public void RightAnswerNeedsNoExplanation() => Assert.Null(Matsubakira.Services.MistakeExplain.For("に", "に"));
}

public class ThemeDeckTests
{
    [Fact]
    public void EveryThemeHasEnoughWords() =>
        Assert.All(Matsubakira.Services.Themes.All, t => Assert.True(Matsubakira.Services.Themes.Words(t.name).Count() >= 15, $"{t.name}: {Matsubakira.Services.Themes.Words(t.name).Count()}"));
}

public class ArticleImportTests
{
    [Fact]
    public void ExtractsJapaneseWithoutFuriganaAndMenus()
    {
        var html = "<html><nav>メニュー ホーム ニュース</nav><script>var x='日本語';</script><p><ruby>今日<rt>きょう</rt></ruby>は<ruby>雨<rt>あめ</rt></ruby>が<ruby>降<rt>ふ</rt></ruby>りました。</p><p>Share on Twitter</p></html>";
        var text = Matsubakira.Views.ReaderPage.ExtractJapanese(html);
        Assert.Equal("今日は雨が降りました。", text);
    }
}

public class FreeAnswerTests
{
    public FreeAnswerTests() => Matsubakira.Services.JDict.LoadAsync().Wait();

    [Fact]
    public void ParaphraseWithKeyWordPasses()
    {
        var (score, said, _) = Matsubakira.Services.FreeAnswer.Check("コーヒーをください。", "こーひーおねがいします");
        Assert.True(score >= 60, $"{score}");
        Assert.Contains("こーひー", said);
    }

    [Fact]
    public void UnrelatedAnswerFails() => Assert.True(Matsubakira.Services.FreeAnswer.Check("コーヒーをください。", "おみずをください").score < 60);
}

/// <summary>9.13: новые уроки (с 4.2) — по правилам раздела 9.3.</summary>
public class NewLessonRulesTests
{
    [Fact]
    public void NewGrammarLessonsFollowTheFormat()
    {
        var onPath = Db.Course.SelectMany(u => u.Steps).Where(s => s.T == "grammar").Select(s => s.Id).ToHashSet();
        foreach (var g in Db.Grammar.Where(g => g.Id >= 89))
        {
            Assert.True(g.Points.Length >= 3, $"{g.Title}: мало пунктов");
            Assert.Contains(g.Points, p => p.Pattern.StartsWith("Частые ошибки"));
            Assert.All(g.Points, p => Assert.True(p.Examples.Length >= 3, $"{g.Title}/{p.Pattern}: меньше 3 примеров"));
            Assert.All(g.Points.SelectMany(p => p.Examples), e => Assert.True(e.Jp.Length > 0 && e.Kana.Length > 0 && e.Ru.Length > 0, $"{g.Title}: пустой пример"));
            Assert.True(g.Quiz.Length >= 8, $"{g.Title}: меньше 8 вопросов");
            Assert.Contains(g.Id, onPath);
            Assert.Contains(g.Level, new[] { "N5", "N4", "N3", "N2", "N1" });
        }
    }
}
