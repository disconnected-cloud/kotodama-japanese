using Matsubakira.Services;
using Matsubakira.Views;

namespace Kotodama.Tests;

/// <summary>4.0: порядок «Пути» с вставленными юнитами, новые уроки, приключения, частицы, «Живой язык».</summary>
public class V40Tests
{
    [Fact]
    public void CourseOrderKeepsIdsAndInsertsUnitsInsideLevels()
    {
        var c = Db.Course;
        Assert.Equal(Enumerable.Range(1, c.Length), c.Select(u => u.Num));               // номера на «Пути» подряд
        Assert.Equal(Enumerable.Range(1, c.Length), c.Select(u => u.Id).OrderBy(i => i)); // ключи прогресса не потерялись
        Assert.Equal(1, c[0].Id);
        // старые юниты идут в прежнем порядке относительно друг друга
        var old = c.Where(u => u.Id <= 41).Select(u => u.Id).ToList();
        Assert.Equal(old.OrderBy(i => i), old);
        // новые юниты N5 стоят сразу после юнита 14, а не в конце после N1
        int at14 = Array.FindIndex(c, u => u.Id == 14);
        Assert.Equal(42, c[at14 + 1].Id);
        Assert.Equal(43, c[at14 + 2].Id);
        Assert.Equal(15, c[at14 + 3].Id);
        Assert.Equal("N1", c[^1].Level);
    }

    [Fact]
    public void NeighboursFollowPathOrder()
    {
        var u42 = Db.Course.First(u => u.Id == 42);
        Assert.Equal(14, Course.Prev(u42)!.Id);
        Assert.Equal(43, Course.Next(u42)!.Id);
        Assert.Null(Course.Prev(Db.Course[0]));
        Assert.Null(Course.Next(Db.Course[^1]));
    }

    [Fact]
    public void AllGrammarLessonsAreOnThePathAndQuizzesAreAnswerable()
    {
        Assert.True(Db.Grammar.Length >= 88);
        var onPath = Db.Course.SelectMany(u => u.Steps).Where(s => s.T == "grammar").Select(s => s.Id).ToHashSet();
        foreach (var g in Db.Grammar.Where(g => g.Id > 75))
        {
            Assert.Contains(g.Id, onPath);
            foreach (var q in g.Quiz)
            {
                Assert.Contains(q.Answer, q.Options);
                Assert.Contains("＿", q.Question);
            }
        }
        foreach (var u in Db.Course)
            foreach (var s in u.Steps)
            {
                if (s.T == "grammar") Assert.InRange(s.Id, 1, Db.Grammar.Length);
                if (s.T == "dialog") Assert.InRange(s.Id, 1, Db.Dialogs.Length);
                if (s.T == "words") Assert.True(Db.Words.Count(w => w.Topic == s.Topic) >= 4, s.Topic);
            }
    }

    [Fact]
    public void QuestsHaveExactlyOneRightAnswerPerScene()
    {
        Assert.True(Db.Quests.Length >= 4);
        foreach (var q in Db.Quests)
        {
            Assert.True(q.Scenes.Length >= 5, q.Id);
            foreach (var s in q.Scenes)
            {
                Assert.Equal(1, s.Choices.Count(c => c.Ok));
                Assert.Equal(3, s.Choices.Length);
                Assert.All(s.Choices, c => Assert.False(string.IsNullOrWhiteSpace(c.Why)));
                Assert.DoesNotContain(s.Romaji, ch => ch >= 'ぁ' && ch <= 'ヶ');
                // верный ответ можно проверить голосом — для него есть чтение
                Assert.NotNull(Db.KanaFor(s.Choices.First(c => c.Ok).Jp));
            }
        }
    }

    [Fact]
    public void ParticleQuizHasOneValidAnswerAndNoSynonyms()
    {
        var ps = ParticlesPage.Rules.Select(r => r.P).ToHashSet();
        Assert.All(ParticlesPage.Items, i => Assert.Contains(i.A, ps));
        Assert.All(ParticlesPage.Items, i => Assert.Contains(ParticlesPage.Rules, r => r.Key == i.Rule && r.P == i.A));
        for (int run = 0; run < 20; run++)
            foreach (var q in ParticlesPage.Questions(Array.Empty<string>(), 100))
            {
                Assert.Equal(4, q.Options.Distinct().Count());
                Assert.Contains(q.Answer, q.Options);
                if (q.Answer == "へ") Assert.DoesNotContain("に", q.Options);
                if (q.Answer == "と") Assert.DoesNotContain("や", q.Options);
            }
    }

    [Fact]
    public void LiveLanguageLevelIsLoadedWithExamples()
    {
        var live = Db.WordsOf("Живой").ToList();
        Assert.True(live.Count >= 100);
        Assert.All(live, w => Assert.False(string.IsNullOrEmpty(w.ExJp)));
        Assert.Contains(Db.Words, w => w.Topic == "Ещё слова N5" && w.Level == "N5");
        Assert.Contains(Db.Words, w => w.Topic == "Ещё слова N4" && w.Level == "N4");
        // ни одно слово не задвоилось с другим уровнем
        Assert.Equal(Db.Words.Length, Db.Words.Select(w => w.Id).Distinct().Count());
    }
}
