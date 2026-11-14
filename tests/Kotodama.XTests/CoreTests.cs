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

public class SpeechBySoundTests
{
    public SpeechBySoundTests() => JDict.LoadAsync().Wait();

    // Правильно сказанное, но записанное Whisper иначе, должно засчитываться
    [Theory]
    [InlineData("お願いします", "おねがいします", "おねがいします")]
    [InlineData("こんにちわ", "こんにちは", "こんにちは")]
    [InlineData("3時です", "三時です", "さんじです")]
    [InlineData("にほんごをべんきょうしています", "日本語を勉強しています", "にほんごをべんきょうしています")]
    [InlineData("日本語を勉強しています", "にほんごをべんきょうしています", "にほんごをべんきょうしています")]
    [InlineData("スシを食べます", "すしをたべます", "すしをたべます")]
    [InlineData("東京に行きます", "とうきょうにいきます", "とうきょうにいきます")]
    [InlineData("おはよございます", "おはようございます", "おはようございます")]
    [InlineData("ここは", "ここ", "ここ")]
    [InlineData("じゃね", "じゃあね", "じゃあね")]
    [InlineData("真っ直ぐですね。", "まっすぐですね。", "まっすぐですね。")]
    public void SameSoundIsAccepted(string heard, string jp, string kana) =>
        Assert.True(Speech.Score(heard, 0.6f, jp, kana).Score >= 75, Speech.Score(heard, 0.6f, jp, kana).ToString());

    [Fact]
    public void AlignMapsKanjiRuns()
    {
        var m = Reading.Align("日本語を勉強しています", "にほんごをべんきょうしています");
        Assert.Equal("にほんご", m["日本語"]);
        Assert.Equal("べんきょう", m["勉強"]);
    }

    [Fact]
    public void OkuriganaUsesDictionaryForm() => Assert.Equal("たべます", Reading.Of("食べます"));

    [Theory]
    [InlineData("切って", "きって")]
    [InlineData("話して", "はなして")]
    [InlineData("書いた", "かいた")]
    [InlineData("飲んで", "のんで")]
    public void ConjugatedVerbsReadCorrectly(string text, string reading) => Assert.Equal(reading, Reading.Of(text));

    [Fact]
    public void NumbersBecomeKanji() => Assert.Equal("二十五", Reading.NumberKanji(25));

    [Fact]
    public void SingleKanjiReadingFollowsTarget() => Assert.StartsWith("ま", Reading.Of("真っ", null, "まっすぐ"));

    [Fact]
    public void LongVowelHint() => Assert.Contains(Reading.Hints("おばあさん", "おばさん"), t => t.Contains("долг"));

    [Fact]
    public void SmallTsuHint() => Assert.Contains(Reading.Hints("きって", "きて"), t => t.Contains("っ"));

    [Fact]
    public void WrongWordStillLow() => Assert.True(Speech.Score("さようなら", 0.9f, "ありがとう", "ありがとう").Score < 40);

    [Fact]
    public void HallucinationIsDropped() => Assert.Equal(0, Speech.Score("ご視聴ありがとうございました", 0.9f, "はい", "はい").Score);

    [Fact]
    public void PaddingMakesWhisperInputLongEnough()
    {
        var wav = Recorder.ToWav(new byte[16000]); // 0,5 с
        Assert.True((Asr.Pad(wav).Length - 44) / 32000.0 >= 1.6);
    }
}

public class SpeechScoringTests
{
    [Fact]
    public void ExactMatchScoresHigh() => Assert.True(Speech.Score("おはようございます", 0.9f, "おはようございます", "おはようございます").Score >= 92);

    [Fact]
    public void KanaVersionOfKanjiPhraseMatches() => Assert.True(Speech.Score("にほんごをべんきょうしています", 0.8f, "日本語を勉強しています", "にほんごをべんきょうしています").Score >= 90);

    [Fact]
    public void DifferentPhraseScoresLow()
    {
        // в любом режиме чужая фраза не засчитывается; в обычном — балл совсем низкий
        var r = Speech.Score("さようなら", 0.9f, "ありがとうございます", "ありがとうございます");
        Assert.False(r.Ok);
        Assert.True(r.Score < 45);
        var mode = ProgressStore.P.SpeechMode;
        try { ProgressStore.P.SpeechMode = "normal"; Assert.True(Speech.Score("さようなら", 0.9f, "ありがとうございます", "ありがとうございます").Score < 35); }
        finally { ProgressStore.P.SpeechMode = mode; }
    }

    [Fact]
    public void EasyModeForgivesSmallSlip() =>
        // одна неточная мора («ございまう») — в лёгком режиме это зачёт, как в Duolingo
        Assert.True(Speech.Score("ありがとうございまう", 0.8f, "ありがとうございます", "ありがとうございます").Ok);

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
        Assert.True(Db.Grammar.Length >= 103); // 4.2: +15 уроков N5
        Assert.NotEmpty(Db.Dialogs);
        Assert.NotEmpty(Db.Course);
    }

    [Fact]
    public void EveryGrammarLessonIsOnThePath()
    {
        var ids = Db.Course.SelectMany(u => u.Steps).Where(s => s.T == "grammar").Select(s => s.Id).ToList();
        Assert.All(ids, id => Assert.InRange(id, 1, Db.Grammar.Length));
        Assert.Equal(Enumerable.Range(1, Db.Grammar.Length), ids.Distinct().OrderBy(x => x));
        // N5 раньше N4, N4 раньше N3 (по первому появлению урока в пути)
        var order = ids.Distinct().Select(id => Db.Grammar[id - 1].Level).ToList();
        Assert.True(order.LastIndexOf("N5") < order.IndexOf("N3"));
        // шаги «кандзи» достижимы: столько знаков уровня действительно есть
        foreach (var s in Db.Course.SelectMany(u => u.Steps).Where(s => s.T == "kanji"))
            Assert.True(Db.KanjiOf(s.Level ?? "N5").Count() >= s.Count, $"{s.Level}: {s.Count}");
        // слова для шагов «слова» существуют
        foreach (var s in Db.Course.SelectMany(u => u.Steps).Where(s => s.T == "words"))
            Assert.True(Db.Words.Any(w => w.Topic == s.Topic), s.Topic);
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

public class PitchAndMnemonicsTests
{
    [Theory]
    [InlineData("箸", "はし", 1)]   // палочки: атамадака
    [InlineData("橋", "はし", 2)]   // мост: одака
    [InlineData("端", "はし", 0)]   // край: хэйбан
    public void HashiMinimalPair(string word, string reading, int accent) => Assert.Equal(accent, Pitch.Accent(word, reading));

    [Fact]
    public void MorasJoinSmallKana() => Assert.Equal(new[] { "きょ", "う" }, Pitch.Moras("きょう"));

    [Fact]
    public void AtamadakaPatternHighThenLow()
    {
        var p = Pitch.Pattern("箸", "はし")!;
        Assert.Equal(1, p[0].pitch);
        Assert.Equal(0, p[1].pitch);
        Assert.True(p[0].accentEnd);
    }

    [Fact]
    public void KanjiHaveParts()
    {
        var m = Mnemonics.For("休")!;
        Assert.Contains(m.Parts, x => x[0] == "亻");
        Assert.False(string.IsNullOrEmpty(Mnemonics.Hint("休", "отдыхать")));
    }

    [Fact]
    public void MostKanjiHaveMnemonicHint() =>
        Assert.True(Db.Kanji.Count(k => Mnemonics.Hint(k.Char, k.Ru).Length > 0) > Db.Kanji.Length * 0.6);
}

public class GrammarRulesTests
{
    [Theory]
    [InlineData("きのう学校に行きます。", "きのう学校に行きました。")]
    [InlineData("あした映画を見ました。", "あした映画を見ます。")]
    [InlineData("日本語を勉強しますでした。", "日本語を勉強しませんでした。")]
    [InlineData("これはペンじゃありますか？", "これはペンですか？")]
    public void FixesCommonMistakes(string wrong, string right) => Assert.Equal(right, GrammarRules.Check(wrong)?.fixedText);

    [Theory]
    [InlineData("コーヒーをください。")]
    [InlineData("きのう学校に行きました。")]
    [InlineData("あした映画を見ます。")]
    public void CorrectSentencesPass(string ok) => Assert.Null(GrammarRules.Check(ok));

    [Fact]
    public void RomajiBySeparateWords()
    {
        JDict.LoadAsync().Wait();
        Assert.Equal("konnichiwa, kyou wa nani o shimasu ka?", Reading.RomajiWords("こんにちは、今日は何をしますか？"));
        Assert.Equal("koohii o kudasai.", Reading.RomajiWords("コーヒーをください。"));
        Assert.Equal("watashi wa gakusei desu.", Reading.RomajiWords("私は学生です。"));
        Assert.Equal("sushi o tabemasu ka?", Reading.RomajiWords("寿司を食べますか？"));
    }
}

public class AlgorithmFixTests
{
    [Theory]
    [InlineData("きのうはきれいです", "きのうはきれいでした。")]
    [InlineData("きのうはゆうめいです。", "きのうはゆうめいでした。")]
    [InlineData("昨日は嫌いです", "昨日は嫌いでした。")]
    [InlineData("きのうはいいです", "きのうはよかったです。")]
    [InlineData("きのうはさむいです", "きのうはさむかったです。")]
    [InlineData("  きのうたべます！", "きのうたべました！")]
    public void GrammarRulesHandleNaAdjectivesAndIi(string wrong, string right) => Assert.Equal(right, GrammarRules.Check(wrong)?.fixedText);

    [Theory]
    [InlineData("tukue", "つくえ")]
    [InlineData("tikara", "ちから")]
    [InlineData("xtsu", "っ")]
    [InlineData("ltu", "っ")]
    [InlineData("kyoukai", "きょうかい")]
    public void RomajiKunreiAndSmallKana(string r, string k) => Assert.Equal(k, Kana.FromRomaji(r));

    [Fact]
    public void NumbersReadCorrectly()
    {
        Assert.Equal("いっせんおく", Numbers.Read(100_000_000_000));
        Assert.Equal("いっちょう", Numbers.Read(1_000_000_000_000));
        Assert.Equal("三点五", Reading.DigitsToKanji("3.5"));
        Assert.Equal("三時三十分", Reading.DigitsToKanji("3:30"));
        Assert.Equal("一二三四五六七八九〇一二三四五六七", Reading.DigitsToKanji("12345678901234567")); // 17 цифр — по одной
    }

    [Fact]
    public void DenoiseKeepsRecordingTail()
    {
        var r = new Random(1);
        var x = new float[16000];
        for (int i = 0; i < x.Length; i++) x[i] = 300 * (float)(r.NextDouble() - 0.5) + (i > 15000 ? 6000 * (float)Math.Sin(i * 0.3) : 0);
        Matsubakira.Services.Denoise.Apply(x, 99);
        double tail = 0; for (int i = x.Length - 200; i < x.Length; i++) tail += Math.Abs(x[i]);
        Assert.True(tail / 200 > 500, $"хвост обнулён: {tail / 200}");
    }

    [Fact]
    public void QuizOptionsSkipSynonyms()
    {
        for (int i = 0; i < 20; i++)
        {
            var o = Matsubakira.Controls.QuizSession.MakeOptions("кошка", new[] { "кошка, кот", "собака", "кот (животное)", "птица", "рыба" });
            Assert.DoesNotContain("кошка, кот", o);
            Assert.Equal(4, o.Length);
        }
    }

    [Theory]
    [InlineData("トウキョウ", "とうきょう")]
    [InlineData("tōkyō", "toukyou")]
    [InlineData("tookyoo", "toukyou")]
    [InlineData("ｔｏｋｙｏ", "tokyo")]
    public void TypedAnswersNormalize(string a, string b) => Assert.Equal(Matsubakira.Controls.QuizSession.StdAnswer(b), Matsubakira.Controls.QuizSession.StdAnswer(a));

    [Fact]
    public void ThemesAvoidFalsePrefixes()
    {
        Assert.False(Themes.Match(new Word("w:x|x", "x", "x", "радио", "t"), "Чувства и характер"));
        Assert.False(Themes.Match(new Word("w:x|x", "x", "x", "руководитель", "t"), "Тело и здоровье"));
        Assert.True(Themes.Match(new Word("w:x|x", "x", "x", "рука", "t"), "Тело и здоровье"));
        Assert.False(Themes.Match(new Word("w:x|x", "x", "x", "рука", "t"), "нет такой темы"));
    }

    [Fact]
    public void LevelsCompareByDifficulty()
    {
        Assert.True(Db.LevelRank("N5") < Db.LevelRank("N3"));
        Assert.True(Db.LevelRank("1000") < Db.LevelRank("N2"));
    }

    [Fact]
    public void ShiftJisPageDecodes()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var sjis = System.Text.Encoding.GetEncoding("shift_jis").GetBytes("<meta charset=\"Shift_JIS\"><p>吾輩は猫である。名前はまだ無い。</p>");
        Assert.Contains("吾輩は猫である", Matsubakira.Views.ReaderPage.Decode(sjis, null));
    }
}

public class ReaderImportTests
{
    [Fact]
    public void EpubTextIsExtracted()
    {
        var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            using var w = new StreamWriter(zip.CreateEntry("OEBPS/ch1.xhtml").Open());
            w.Write("<html><body><p>吾輩は<ruby>猫<rt>ねこ</rt></ruby>である。名前はまだ無い。</p></body></html>");
        }
        var text = Matsubakira.Views.ReaderPage.FromEpub(ms.ToArray());
        Assert.Contains("吾輩は猫である", text);
        Assert.DoesNotContain("ねこ", text); // фуригана не попадает в текст
    }
}

public class PitchTrainerTests
{
    [Fact]
    public void PitchQuizHasQuestionsWithCorrectOption()
    {
        var qs = Matsubakira.Views.TrainersPage.PitchQuestions();
        Assert.True(qs.Count >= 8);
        Assert.All(qs, x => Assert.Contains(x.Answer, x.Options!));
    }
}
