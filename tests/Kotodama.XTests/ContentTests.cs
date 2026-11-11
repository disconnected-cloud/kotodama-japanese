using Matsubakira.Services;
using Matsubakira.Views;

namespace Kotodama.Tests;

/// <summary>Новые материалы: книги, разговорник, юниты курса.</summary>
public class ContentTests
{
    [Fact]
    public void BooksHaveChaptersWithAnswerInOptions()
    {
        var books = ReaderPage.Texts.Where(t => t.Book != null).GroupBy(t => t.Book).ToList();
        Assert.True(books.Count >= 3);
        foreach (var t in ReaderPage.Texts)
        {
            Assert.NotEmpty(t.Sentences);
            foreach (var (q, a, opts) in t.Questions) Assert.Contains(a, opts);
        }
    }

    [Fact]
    public void PhrasebookLessonsAreComplete()
    {
        Assert.True(Db.SpeechLessons.Length >= 16);
        foreach (var l in Db.SpeechLessons)
            foreach (var p in l.Items)
            {
                Assert.False(string.IsNullOrWhiteSpace(p.Kana), p.Jp);
                Assert.DoesNotContain(p.Romaji, c => c >= 'ぁ' && c <= 'ヶ'); // всё переведено в латиницу
            }
    }

    [Fact]
    public void CourseStepsPointToExistingLessons()
    {
        foreach (var u in Db.Course)
            foreach (var s in u.Steps.Where(s => s.T == "speech"))
                Assert.InRange(s.Id, 1, Db.SpeechLessons.Length);
    }
}
