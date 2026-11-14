using System.Text.RegularExpressions;

namespace Matsubakira.Services;

/// <summary>
/// Простые правила для частых ошибок новичков — работают мгновенно и надёжно, даже когда
/// маленькая языковая модель ошибку не замечает: время глагола при «вчера/завтра», «ますでした», «じゃありますか».
/// </summary>
public static class GrammarRules
{
    static readonly string[] Past = { "きのう", "昨日", "先週", "せんしゅう", "去年", "きょねん", "先月", "せんげつ", "おととい", "さっき" };
    /// <summary>な-прилагательные, которые кончаются на い: прошедшее время у них «でした», а не «かったです».</summary>
    static readonly string[] NaAdjI = { "きれい", "綺麗", "奇麗", "きらい", "嫌い", "だいきらい", "大嫌い", "ゆうめい", "有名", "とくい", "得意", "しつれい", "失礼", "ていねい", "丁寧", "あいまい", "曖昧", "さいわい", "幸い" };
    static readonly string[] Future = { "あした", "明日", "あす", "来週", "らいしゅう", "来年", "らいねん", "来月", "らいげつ", "あさって" };

    /// <summary>Исправленная фраза и пояснение по-русски — или null, если правила ошибок не нашли.</summary>
    public static (string fixedText, string why)? Check(string s)
    {
        var full = s.Trim();
        var t = full.TrimEnd('。', '.', '！', '!', '？', '?');
        string end = full.Length > t.Length ? full[t.Length..] : "。";
        if (t.EndsWith("ますでした")) return (t[..^5] + "ませんでした" + end, "Прошедшее отрицание: «…ませんでした» (или «…ました», если «да»).");
        if (t.EndsWith("じゃありますか")) return (t[..^7] + "ですか" + end, "Вопрос «это …?» — «…ですか».");
        if (Past.Any(t.Contains))
        {
            if (t.EndsWith("ません")) return (t + "でした" + end, "Речь о прошлом — отрицание «…ませんでした».");
            if (t.EndsWith("ます")) return (t[..^2] + "ました" + end, "Речь о прошлом (вчера, на прошлой неделе) — глагол в форме «…ました».");
            if (t.EndsWith("です") && NaAdjI.Any(a => t.EndsWith(a + "です")))
                return (t[..^2] + "でした" + end, "Это な-прилагательное (хоть и кончается на い): прошедшее время «…でした».");
            if (t.EndsWith("いいです")) return (t[..^4] + "よかったです" + end, "いい в прошедшем времени — «よかったです» (от よい).");
            if (t.EndsWith("です") && Regex.IsMatch(t, "い(です)$")) return (t[..^3] + "かったです" + end, "Прошедшее время い-прилагательного: «…かったです».");
        }
        if (Future.Any(t.Contains))
        {
            if (t.EndsWith("ました")) return (t[..^3] + "ます" + end, "Речь о будущем (завтра, на следующей неделе) — форма «…ます».");
            if (t.EndsWith("ませんでした")) return (t[..^6] + "ません" + end, "Речь о будущем — отрицание «…ません».");
        }
        return null;
    }
}
