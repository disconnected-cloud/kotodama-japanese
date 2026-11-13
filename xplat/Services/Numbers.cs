namespace Matsubakira.Services;

/// <summary>
/// Японские числа каной: со всеми звуковыми изменениями (さんびゃく, はっせん, ろっぴゃく…),
/// время (よじ, くじ, じゅっぷん), даты (ついたち, はつか), цены (よえん).
/// </summary>
public static class Numbers
{
    static readonly string[] Digit = { "", "いち", "に", "さん", "よん", "ご", "ろく", "なな", "はち", "きゅう" };
    static readonly string[] Hyaku = { "", "ひゃく", "にひゃく", "さんびゃく", "よんひゃく", "ごひゃく", "ろっぴゃく", "ななひゃく", "はっぴゃく", "きゅうひゃく" };
    static readonly string[] Sen = { "", "せん", "にせん", "さんぜん", "よんせん", "ごせん", "ろくせん", "ななせん", "はっせん", "きゅうせん" };

    /// <summary>Число от 1 до 9999 (без 万).</summary>
    static string Below10k(int n)
    {
        int th = n / 1000, h = n / 100 % 10, t = n / 10 % 10, u = n % 10;
        var s = Sen[th] + Hyaku[h];
        if (t > 0) s += (t == 1 ? "" : Digit[t]) + "じゅう";
        return s + Digit[u];
    }

    /// <summary>Чтение числа каной (0 — ゼロ). До 9 9999 9999.</summary>
    public static string Read(long n)
    {
        if (n == 0) return "ゼロ";
        if (n < 0) return "マイナス" + Read(-n);
        var s = "";
        int oku = (int)(n / 100_000_000), man = (int)(n / 10_000 % 10_000), rest = (int)(n % 10_000);
        if (oku > 0) s += Below10k(oku) + "おく";
        // 1000万 — いっせんまん (а не せんまん): тысяча перед 万 читается с いっ
        if (man > 0) s += (man == 1 ? "いち" : man >= 1000 && man / 1000 == 1 ? "いっ" + Below10k(man) : Below10k(man)) + "まん";
        if (rest > 0) s += Below10k(rest);
        return s;
    }

    /// <summary>Число иероглифами: 3450 → 三千四百五十 (для подсказки).</summary>
    public static string Kanji(long n)
    {
        const string d = "〇一二三四五六七八九";
        if (n == 0) return "〇";
        string Small(int x)
        {
            var r = "";
            foreach (var (v, k) in new[] { (1000, "千"), (100, "百"), (10, "十") })
                if (x >= v) { int q = x / v; r += (q > 1 ? d[q].ToString() : "") + k; x %= v; }
            return x > 0 ? r + d[x] : r;
        }
        var s = "";
        if (n >= 100_000_000) { s += Small((int)(n / 100_000_000)) + "億"; n %= 100_000_000; }
        if (n >= 10_000) { s += Small((int)(n / 10_000)) + "万"; n %= 10_000; }
        return n > 0 ? s + Small((int)n) : s;
    }

    static readonly string[] Hours = { "れいじ", "いちじ", "にじ", "さんじ", "よじ", "ごじ", "ろくじ", "しちじ", "はちじ", "くじ", "じゅうじ", "じゅういちじ", "じゅうにじ" };
    static readonly string[] MinUnit = { "", "いっぷん", "にふん", "さんぷん", "よんぷん", "ごふん", "ろっぷん", "ななふん", "はっぷん", "きゅうふん" };

    /// <summary>Минуты: 10 じゅっぷん, 25 にじゅうごふん, 30 さんじゅっぷん.</summary>
    public static string Minutes(int m)
    {
        if (m <= 0) return "";
        int t = m / 10, u = m % 10;
        if (u == 0) return (t == 1 ? "" : Digit[t]) + "じゅっぷん";
        return (t == 0 ? "" : (t == 1 ? "" : Digit[t]) + "じゅう") + MinUnit[u];
    }

    /// <summary>Время по-японски: 7:30 → しちじ さんじゅっぷん (или しちじはん).</summary>
    public static string Time(int h, int m, bool han = false)
    {
        var hh = Hours[h % 13];
        if (m == 0) return hh;
        if (m == 30 && han) return hh + "はん";
        return hh + Minutes(m);
    }

    static readonly string[] Months = { "", "いちがつ", "にがつ", "さんがつ", "しがつ", "ごがつ", "ろくがつ", "しちがつ", "はちがつ", "くがつ", "じゅうがつ", "じゅういちがつ", "じゅうにがつ" };
    static readonly Dictionary<int, string> SpecialDays = new()
    {
        [1] = "ついたち", [2] = "ふつか", [3] = "みっか", [4] = "よっか", [5] = "いつか", [6] = "むいか", [7] = "なのか", [8] = "ようか", [9] = "ここのか", [10] = "とおか",
        [14] = "じゅうよっか", [17] = "じゅうしちにち", [19] = "じゅうくにち", [20] = "はつか", [24] = "にじゅうよっか", [27] = "にじゅうしちにち", [29] = "にじゅうくにち",
    };

    public static string Month(int m) => Months[m];
    public static string Day(int d) => SpecialDays.TryGetValue(d, out var s) ? s : Below10k(d) + "にち";
    public static string Date(int m, int d) => Months[m] + Day(d);

    /// <summary>Цена: 4 円 — よえん, 14 円 — じゅうよえん.</summary>
    public static string Yen(long n)
    {
        var r = Read(n);
        return (r.EndsWith("よん") ? r[..^2] + "よ" : r) + "えん";
    }

    /// <summary>Правдоподобные ошибки для вариантов ответа: без звуковых изменений, соседние числа.</summary>
    public static IEnumerable<string> Mistakes(string reading)
    {
        var swaps = new (string a, string b)[]
        {
            ("さんびゃく", "さんひゃく"), ("ろっぴゃく", "ろくひゃく"), ("はっぴゃく", "はちひゃく"), ("さんぜん", "さんせん"), ("はっせん", "はちせん"),
            ("じゅっぷん", "じゅうふん"), ("いっぷん", "いちふん"), ("さんぷん", "さんふん"), ("ろっぷん", "ろくふん"), ("はっぷん", "はちふん"), ("よんぷん", "よんふん"),
            ("よじ", "よんじ"), ("しちじ", "ななじ"), ("くじ", "きゅうじ"), ("しがつ", "よんがつ"), ("くがつ", "きゅうがつ"), ("しちがつ", "なながつ"),
            ("ついたち", "いちにち"), ("ふつか", "ににち"), ("みっか", "さんにち"), ("よっか", "よんにち"), ("はつか", "にじゅうにち"), ("ようか", "はちにち"), ("なのか", "なのにち"),
            ("よえん", "よんえん"), ("いっせんまん", "せんまん"),
        };
        foreach (var (a, b) in swaps)
            if (reading.Contains(a)) yield return reading.Replace(a, b);
    }

    /// <summary>Убрать из введённого ответа всё, кроме цифр и двоеточия (3,450 円 → 3450).</summary>
    public static string DigitsOnly(string s) =>
        new(s.Normalize(System.Text.NormalizationForm.FormKC).Where(c => char.IsDigit(c) || c == ':').ToArray());
}
