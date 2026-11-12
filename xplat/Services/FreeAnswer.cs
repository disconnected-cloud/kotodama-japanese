namespace Matsubakira.Services;

/// <summary>
/// Свободный ответ в диалоге: засчитывается, если в сказанном есть ключевые слова реплики
/// (существительные, глаголы, прилагательные — по словарю), а не дословное совпадение.
/// «コーヒーをください» → ключ «コーヒー»: подойдёт и «コーヒーお願いします».
/// </summary>
public static class FreeAnswer
{
    // служебные слова и вежливые хвосты — по ним смысл не проверяем
    static readonly HashSet<string> Function = new()
    {
        "は", "が", "を", "に", "で", "へ", "と", "も", "の", "か", "ね", "よ", "な", "や", "から", "まで", "より",
        "です", "ます", "でした", "ました", "ません", "ください", "します", "して", "ある", "いる", "あります", "います",
        "これ", "それ", "あれ", "この", "その", "あの", "はい", "いいえ", "ええ", "あの", "えっと", "じゃ", "では", "でも",
        "おねがい", "おねがいします", "どうも", "すみません", "ちょっと",
    };

    /// <summary>Ключевые слова реплики (каной, в хирагане).</summary>
    public static List<string> Keywords(string jp)
    {
        var keys = new List<string>();
        int i = 0;
        var s = jp.Trim().TrimEnd('。', '？', '！', '?', '!');
        while (i < s.Length)
        {
            var (len, e) = JDict.Longest(s, i);
            var surf = s.Substring(i, len);
            i += len;
            if (e == null) continue;
            var kana = Kana.ToHiragana(e.Kana.Split('、')[0]);
            if (kana.Length < 2 || Function.Contains(kana) || Function.Contains(surf)) continue;
            // глагол или прилагательное — достаточно основы (たべ из たべます / たべた)
            if (kana.Length >= 3 && (kana.EndsWith("る") || kana.EndsWith("い") || kana.EndsWith("う"))) kana = kana[..^1];
            if (!keys.Contains(kana)) keys.Add(kana);
        }
        return keys;
    }

    /// <summary>Балл 0–100 и какие ключевые слова прозвучали / не прозвучали.</summary>
    public static (int score, List<string> said, List<string> missed) Check(string targetJp, string heardKana)
    {
        var keys = Keywords(targetJp);
        var heard = Kana.ToHiragana(heardKana).Replace(" ", "");
        if (keys.Count == 0) return (heard.Length > 0 ? 80 : 0, new(), new());
        var said = keys.Where(k => heard.Contains(k, StringComparison.Ordinal)).ToList();
        var missed = keys.Except(said).ToList();
        int score = heard.Length == 0 ? 0 : 40 + 60 * said.Count / keys.Count;
        if (said.Count == 0) score = Math.Min(score, 35);
        return (score, said, missed);
    }
}
