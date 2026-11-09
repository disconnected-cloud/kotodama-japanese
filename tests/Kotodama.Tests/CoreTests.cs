using Matsubakira.Services;
using Matsubakira.Views;

namespace Kotodama.Tests;

public class RomajiTests
{
    [Theory]
    [InlineData("こんにちは", "konnichiwa")]
    [InlineData("わたし は アンナ です", "watashi wa anna desu")]
    [InlineData("ごちゅうもん は？", "gochuumon wa?")]
    [InlineData("きって", "kitte")]
    [InlineData("ちょっと", "chotto")]
    [InlineData("しんぶん", "shinbun")]
    [InlineData("きんえん", "kin'en")]
    [InlineData("コーヒー", "koohii")]
    [InlineData("ぎゅうにゅう", "gyuunyuu")]
    public void KanaToRomaji(string kana, string expected) => Assert.Equal(expected, Kana.ToRomaji(kana));

    [Theory]
    [InlineData("taberu", "たべる")]
    [InlineData("gakkou", "がっこう")]
    [InlineData("konnichiwa", "こんにちわ")]
    [InlineData("shinbun", "しんぶん")]
    [InlineData("matcha", "まっちゃ")]
    [InlineData("si", "し")]
    public void RomajiToKana(string romaji, string expected) => Assert.Equal(expected, Kana.FromRomaji(romaji));

    [Fact]
    public void KatakanaConversionRoundTrip() => Assert.Equal("あいうえお", Kana.ToHiragana(Kana.ToKatakana("あいうえお")));

    [Fact]
    public void SentenceRomajiUsesPhoneticParticles()
    {
        Assert.Equal("watashi wa gakusei desu.", Db.RomajiFor("私は学生です。", "わたしはがくせいです。"));
        Assert.Contains("o", Db.RomajiFor("水を飲みます。", "みずをのみます。").Split(' '));
    }
}

public class ConjugationTests
{
    [Theory]
    [InlineData("食べる", "たべる", "te", "食べて")]
    [InlineData("食べる", "たべる", "nai", "食べない")]
    [InlineData("食べる", "たべる", "pot", "食べられる")]
    [InlineData("飲む", "のむ", "te", "飲んで")]
    [InlineData("飲む", "のむ", "vol", "飲もう")]
    [InlineData("飲む", "のむ", "ba", "飲めば")]
    [InlineData("行く", "いく", "te", "行って")]
    [InlineData("行く", "いく", "ta", "行った")]
    [InlineData("書く", "かく", "te", "書いて")]
    [InlineData("泳ぐ", "およぐ", "te", "泳いで")]
    [InlineData("話す", "はなす", "masu", "話します")]
    [InlineData("買う", "かう", "nai", "買わない")]
    [InlineData("帰る", "かえる", "te", "帰って")]        // исключение: годан на -eru
    [InlineData("勉強する", "べんきょうする", "pot", "勉強できる")]
    [InlineData("来る", "くる", "nai", "来ない")]
    [InlineData("ある", "ある", "nai", "ない")]
    public void Forms(string jp, string kana, string form, string expected) => Assert.Equal(expected, Conj.All(jp, kana)![form]);

    [Fact]
    public void AllCourseVerbsConjugate()
    {
        var verbs = Db.Words.Where(w => w.Topic.StartsWith("Глаголы") && w.Level is "N5" or "N4").ToList();
        Assert.NotEmpty(verbs);
        Assert.All(verbs, v => Assert.NotNull(Conj.All(v.Jp, v.Kana)));
    }
}

public class FsrsTests
{
    [Fact]
    public void GoodAnswersGrowIntervals()
    {
        var c = new SrsCard();
        double prev = 0;
        for (int i = 0; i < 5; i++)
        {
            var (_, _, days) = Fsrs.Next(c, Rating.Good);
            Assert.True(days > prev, $"интервал должен расти: {days} > {prev}");
            prev = days;
            Fsrs.Apply(c, Rating.Good);
            c.LastReview = DateTime.Now.AddDays(-days);
        }
    }

    [Fact]
    public void AgainResetsToMinutes()
    {
        var c = new SrsCard();
        Fsrs.Apply(c, Rating.Good);
        Assert.Equal(0, Fsrs.Next(c, Rating.Again).days);
        Fsrs.Apply(c, Rating.Again);
        Assert.Equal(1, c.Lapses);
        Assert.True(c.Due < DateTime.Now.AddHours(1));
    }

    [Fact]
    public void EasyBeatsGoodBeatsHard()
    {
        var c = new SrsCard();
        double hard = Fsrs.Next(c, Rating.Hard).days, good = Fsrs.Next(c, Rating.Good).days, easy = Fsrs.Next(c, Rating.Easy).days;
        Assert.True(easy > good && good >= hard);
    }

    [Fact]
    public void MigratesOldSm2Cards()
    {
        var old = new SrsCard { Reps = 3, Interval = 10, Ease = 2.5, Due = DateTime.Today };
        var (s, d, days) = Fsrs.Next(old, Rating.Good);
        Assert.True(s > 5 && d is >= 1 and <= 10 && days >= 10);
    }
}

public class SpeechScoringTests
{
    [Fact]
    public void ExactMatchScoresHigh() => Assert.True(Speech.Score("おはようございます", 0.9f, "おはようございます", "おはようございます").Score >= 92);

    [Fact]
    public void KanaVersionOfKanjiPhraseMatches() => Assert.True(Speech.Score("にほんごをべんきょうしています", 0.8f, "日本語を勉強しています", "にほんごをべんきょうしています").Score >= 90);

    [Fact]
    public void DifferentPhraseScoresLow() => Assert.True(Speech.Score("さようなら", 0.9f, "ありがとうございます", "ありがとうございます").Score < 35);

    [Fact]
    public void EmptyRecognitionIsZero() => Assert.Equal(0, Speech.Score("", 0, "はい", "はい").Score);

    [Theory]
    [InlineData("abc", "abc", 1.0)]
    [InlineData("abc", "abd", 0.6667)]
    [InlineData("", "", 1.0)]
    public void Similarity(string a, string b, double expected) => Assert.Equal(expected, Speech.Similarity(a, b), 3);
}

public class ProsodyTests
{
    /// <summary>Синтетический «голос»: гармонический сигнал с плавающей частотой основного тона.</summary>
    static float[] Voice(double f0Start, double f0End, double seconds)
    {
        int n = (int)(16000 * seconds);
        var x = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n, f = f0Start + (f0End - f0Start) * t;
            phase += 2 * Math.PI * f / 16000;
            double env = Math.Min(1, Math.Min(i, n - i) / 800.0);
            x[i] = (float)(env * 0.3 * (Math.Sin(phase) + 0.5 * Math.Sin(2 * phase) + 0.25 * Math.Sin(3 * phase)));
        }
        return x;
    }

    [Fact]
    public void SameContourScoresHigh()
    {
        var a = Voice(140, 240, 1.2);
        var r = Prosody.Compare(a, a)!;
        Assert.True(r.Intonation >= 95 && r.Rhythm >= 95, $"{r.Intonation}/{r.Rhythm}");
    }

    [Fact]
    public void OppositeContourScoresLower()
    {
        var r = Prosody.Compare(Voice(140, 240, 1.2), Voice(240, 140, 1.2))!;
        Assert.True(r.Intonation < 75, $"интонация {r.Intonation}");
    }

    [Fact]
    public void SameMelodyInDifferentVoiceIsFine()
    {
        // Тот же контур, но голос на октаву ниже: нормализация к медиане должна это простить
        var r = Prosody.Compare(Voice(200, 300, 1.2), Voice(100, 150, 1.2))!;
        Assert.True(r.Intonation >= 85, $"интонация {r.Intonation}");
    }

    [Fact]
    public void SlowSpeechDetected()
    {
        var r = Prosody.Compare(Voice(150, 220, 1.0), Voice(150, 220, 1.8))!;
        Assert.True(r.DurationRatio > 1.5);
        Assert.Contains(r.Tips, t => t.Contains("медленнее"));
    }
}

public class DataIntegrityTests
{
    [Fact]
    public void ContentLoads()
    {
        Assert.True(Db.Words.Length > 2000);
        Assert.True(Db.Kanji.Length >= 370);
        Assert.Equal(28, Db.Grammar.Length);
        Assert.NotEmpty(Db.Dialogs);
        Assert.NotEmpty(Db.Course);
    }

    [Fact]
    public void GrammarAnswersAreAmongOptions() =>
        Assert.All(Db.Grammar.SelectMany(g => g.Quiz.Select(q => (g.Title, q))), x => Assert.Contains(x.q.Answer, x.q.Options));

    [Fact]
    public void GrammarQuestionsHaveBlank() =>
        Assert.All(Db.Grammar.SelectMany(g => g.Quiz), q => Assert.Contains("＿", q.Question));

    [Fact]
    public void CourseStepsReferenceExistingContent()
    {
        foreach (var u in Db.Course)
            foreach (var s in u.Steps)
            {
                switch (s.T)
                {
                    case "grammar": Assert.InRange(s.Id, 1, Db.Grammar.Length); break;
                    case "speech": Assert.InRange(s.Id, 1, Db.SpeechLessons.Length); break;
                    case "dialog": Assert.InRange(s.Id, 1, Db.Dialogs.Length); break;
                    case "words": Assert.Contains(Db.Words, w => w.Topic == s.Topic); break;
                    case "kana": Assert.True(s.Rows != null || s.Group != null); break;
                }
            }
    }

    [Fact]
    public void EveryKanjiHasStrokeOrder() => Assert.All(Db.Kanji, k => Assert.NotNull(Db.Strokes(k.Char)));

    [Fact]
    public void WordIdsAreUnique() => Assert.Equal(Db.Words.Length, Db.Words.Select(w => w.Id).Distinct().Count());

    [Fact]
    public void NoEmptyTranslations() => Assert.All(Db.Words, w => Assert.False(string.IsNullOrWhiteSpace(w.Ru), w.Jp));
}

public class DictionaryTests
{
    public DictionaryTests() => JDict.LoadAsync().Wait();

    [Theory]
    [InlineData("кошка", "猫")]
    [InlineData("neko", "猫")]
    [InlineData("ねこ", "猫")]
    [InlineData("taberu", "食べる")]
    public void TopResult(string query, string expected) => Assert.Equal(expected, JDict.Search(query).First().Jp);

    [Fact]
    public void LongestMatchFindsCompound()
    {
        var (len, e) = JDict.Longest("日本語を勉強します", 0);
        Assert.Equal(3, len);
        Assert.NotNull(e);
    }
}
