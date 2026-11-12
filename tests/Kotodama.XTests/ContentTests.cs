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

    [Fact]
    public void VoiceCleanupNormalizesWithoutClicks()
    {
        // тихий тон 200 Гц с постоянным смещением и резким началом
        var x = Enumerable.Range(0, 24000).Select(i => (float)(0.2 + 0.3 * Math.Sin(i * 2 * Math.PI * 200 / 24000))).ToArray();
        var (y, rate) = Matsubakira.Platform.Pcm.Decode(Voicevox.Clean(Matsubakira.Platform.Pcm.ToWav(x, 24000)), true);
        Assert.Equal(24000, rate);
        Assert.Equal(x.Length, y.Length);
        Assert.InRange(y.Max(Math.Abs), 0.6f, 0.86f);          // громкость выровнена, без перегруза
        Assert.True(Math.Abs(y[0]) < 0.01 && Math.Abs(y[^1]) < 0.01); // плавные края — без щелчков
        Assert.True(Math.Abs(y.Skip(4000).Average()) < 0.05);  // постоянная составляющая убрана
    }

    // корень репозитория — от исходника теста (сборка может лежать вне репо)
    static string RepoRoot => FindRoot();

    static string FindRoot([System.Runtime.CompilerServices.CallerFilePath] string src = "")
    {
        {
            var d = new DirectoryInfo(Path.GetDirectoryName(src)!);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Assets", "emoji"))) d = d.Parent;
            return d!.FullName;
        }
    }

    [Fact]
    public void JapanArticlesAreComplete()
    {
        Assert.True(JapanPage.All.Length >= 12);
        foreach (var a in JapanPage.All)
        {
            Assert.NotEmpty(a.Sections);
            Assert.True(a.Words.Length >= 5, a.Id);
            Assert.True(File.Exists(Path.Combine(RepoRoot, "Assets", "emoji", a.Emoji + ".png")), a.Emoji);
            foreach (var (q, ans, opts) in a.Quiz) Assert.Contains(ans, opts);
        }
    }
}
