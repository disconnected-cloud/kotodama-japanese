using Matsubakira.Services;
using Matsubakira.Views;

namespace Kotodama.Tests;

/// <summary>Аудит 2.6: целостность связей между данными и логика тренажёров.</summary>
public class AuditTests
{
    [Fact]
    public void CourseStepsPointToExistingContent()
    {
        var topics = Db.Words.Select(w => w.Topic).ToHashSet();
        foreach (var u in Db.Course)
            foreach (var s in u.Steps)
            {
                switch (s.T)
                {
                    case "words": Assert.True(topics.Contains(s.Topic ?? ""), $"юнит {u.Id}: нет темы «{s.Topic}»"); break;
                    case "kanji": Assert.True(Db.KanjiOf(s.Level ?? "N5").Count() >= s.Count, $"юнит {u.Id}: кандзи {s.Level} меньше {s.Count}"); break;
                    case "grammar": Assert.InRange(s.Id, 1, Db.Grammar.Length); break;
                    case "speech": Assert.InRange(s.Id, 1, Db.SpeechLessons.Length); break;
                    case "dialog": Assert.InRange(s.Id, 1, Db.Dialogs.Length); break;
                }
            }
    }

    [Fact]
    public void GrammarQuizAnswersAreAmongOptions() =>
        Assert.All(Db.Grammar.SelectMany(g => g.Quiz.Select(q => (g, q))), x =>
        {
            Assert.Contains(x.q.Answer, x.q.Options);
            Assert.Contains("＿", x.q.Question);
        });

    [Fact]
    public void DialogAndPhraseRomajiHasNoKana()
    {
        foreach (var l in Db.Dialogs.SelectMany(d => d.Lines))
            Assert.DoesNotContain(l.Romaji, c => c >= 'ぁ' && c <= 'ヶ');
        foreach (var p in Db.SpeechLessons.SelectMany(l => l.Items))
            Assert.DoesNotContain(p.Romaji, c => c >= 'ぁ' && c <= 'ヶ');
    }

    [Fact]
    public void JapanQuizAnswersAreAmongOptions() =>
        Assert.All(JapanPage.All, a => Assert.All(a.Quiz, q => Assert.Contains(q.Item2, q.Item3)));

    [Theory]
    [InlineData("kyou", "きょう", "")]
    [InlineData("kitte", "きって", "")]
    [InlineData("shinbun", "しんぶ", "n")]
    [InlineData("shinbunn", "しんぶん", "")]
    [InlineData("ko-hi-", "こーひー", "")]
    [InlineData("tak", "た", "k")]
    [InlineData("konnichiha", "こんにちは", "")]
    public void TypingConvertsRomaji(string raw, string kana, string pending)
    {
        var (k, p) = TypingPage.Convert(raw);
        Assert.Equal(kana, k);
        Assert.Equal(pending, p);
    }

    [Theory]
    [InlineData("kinnen", "きんえん")]
    [InlineData("kin'en", "きんえん")]
    [InlineData("konnnichiha", "こんにちは")]
    [InlineData("konnichiha", "こんにちは")]
    [InlineData("hannnou", "はんのう")]
    public void TypingAcceptsImeAndHepburn(string raw, string target)
    {
        var (kana, pending) = TypingPage.Best(raw, target);
        Assert.True(kana == target && pending == "" || pending == "n" && kana + "ん" == target, $"{kana}+{pending}");
    }

    [Fact]
    public void AnkiExportHasThreeColumnsPerLine()
    {
        var (text, _) = AnkiExport.Build(AnkiExport.Set.Level);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith('#')).ToList();
        Assert.True(lines.Count > 100);
        Assert.All(lines, l => Assert.Equal(3, l.TrimEnd('\r').Split('\t').Length));
    }

    [Fact]
    public void NumbersReadEveryValueWithoutLatin()
    {
        for (long n = 1; n < 20000; n += 37) Assert.All(Numbers.Read(n), c => Assert.True(Kana.IsKana(c), $"{n}: {Numbers.Read(n)}"));
        for (int h = 1; h <= 12; h++) for (int m = 0; m < 60; m++) Assert.All(Numbers.Time(h, m), c => Assert.True(Kana.IsKana(c)));
        for (int d = 1; d <= 31; d++) Assert.All(Numbers.Day(d), c => Assert.True(Kana.IsKana(c)));
    }

    [Fact]
    public void EveryUnitHasAnExam()
    {
        bool was = ProgressStore.P.QuietMode;
        foreach (var quiet in new[] { false, true })
        {
            ProgressStore.P.QuietMode = quiet;
            foreach (var u in Db.Course)
            {
            var qs = Course.ExamQuestions(u, new Random(u.Id));
            Assert.True(qs.Count >= 6, $"юнит {u.Id} «{u.Title}»: {qs.Count} вопросов");
            Assert.All(qs, q => { Assert.Contains(q.Answer, q.Options); Assert.Equal(q.Options.Length, q.Options.Distinct().Count()); Assert.True(q.Options.Length >= 2, $"юнит {u.Id}: {q.Prompt}"); });
            }
        }
        ProgressStore.P.QuietMode = was;
    }

    [Theory]
    [InlineData("は", "ハ")]
    [InlineData("へ", "ヘ")]
    [InlineData("しゃ", "シャ")]
    [InlineData("はい", "はい")]
    [InlineData("ハ", "ハ")]
    public void LoneKanaIsSpokenAsSound(string text, string spoken) => Assert.Equal(spoken, Speech.Spoken(text));

    [Fact]
    public void WordLevelsAreKnown() =>
        Assert.All(Db.Words, w => Assert.Contains(w.Level, new[] { "N5", "N4", "N3", "N2", "N1", "1000", "Мои" }));
}
