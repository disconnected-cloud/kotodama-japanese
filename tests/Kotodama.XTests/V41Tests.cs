using Matsubakira.Services;
using Matsubakira.Views;

namespace Kotodama.Tests;

/// <summary>4.1: распознавание прощает акцент, но подсказка Whisper не даёт зачёт за чужую фразу.</summary>
public class V41Tests
{
    static T InMode<T>(string mode, Func<T> f)
    {
        var old = ProgressStore.P.SpeechMode;
        try { ProgressStore.P.SpeechMode = mode; return f(); }
        finally { ProgressStore.P.SpeechMode = old; }
    }

    [Theory]
    [InlineData("ありがとごじゃいます", "ありがとうございます")] // じゃ вместо ざ, нет долготы
    [InlineData("こんにちわ", "こんにちは")]
    [InlineData("おはよございまず", "おはようございます")]      // звонкая вместо глухой
    [InlineData("たべます", "たべまず")]
    [InlineData("すみめせん", "すみません")]                    // え/い
    public void AccentedSpeechPassesInNormalMode(string heard, string target) =>
        Assert.True(InMode("normal", () => Speech.Score(heard, 0.7f, target, target).Ok), heard);

    [Fact]
    public void DifferentPhraseStillFails()
    {
        Assert.False(InMode("normal", () => Speech.Score("さようなら", 0.9f, "ありがとうございます", "ありがとうございます").Ok));
        Assert.False(InMode("easy", () => Speech.Score("いぬ", 0.9f, "ねこ", "ねこ").Ok));
    }

    [Fact]
    public void StrictModeDoesNotForgiveVoicing() =>
        Assert.True(InMode("strict", () => Reading.SoundSimilarity("kaki", "gagi")) < InMode("normal", () => Reading.SoundSimilarity("kaki", "gagi", true)));

    [Fact]
    public void HintCannotPassUnrelatedSpeech()
    {
        foreach (var mode in new[] { "easy", "normal", "strict" })
            InMode(mode, () =>
            {
                Assert.True(SpeechUi.HintCap(false, 0.1, 0.99f) < Speech.PassScore);  // сказал другое — подсказка не спасёт
                Assert.True(SpeechUi.HintCap(true, 0, 0.2f) < Speech.PassScore);     // тишина/шум, модель не уверена
                Assert.True(SpeechUi.HintCap(true, 0, 0.8f) >= 90);                  // тихое короткое слово, но уверенно
                Assert.True(SpeechUi.HintCap(false, 0.6, 0.5f) >= 90);               // похоже, с акцентом — можно до 90
                return 0;
            });
    }

    [Fact]
    public void MaxModelIsListedFirst()
    {
        Assert.Equal("kotoba-v2-q5_0", Asr.Models[0].id);
        Assert.Equal("small-q5_1", Asr.DefaultId);
        Assert.Equal(3, Asr.Models.Length);
    }
}
