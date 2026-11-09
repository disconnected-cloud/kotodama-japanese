using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>Разбор произношения по морам и проверка питч-акцента отдельных слов.</summary>
public static class SpeechDetail
{
    static bool Same(string a, string b) => a == b || Reading.Phon(a) == Reading.Phon(b);

    /// <summary>Уверенность Whisper для каждой распознанной моры (null, если сопоставить фрагменты не удалось).</summary>
    static float[]? MoraProbs(List<string> heard, (string text, float p)[]? tokens, Dictionary<string, string>? map)
    {
        if (tokens is not { Length: > 0 }) return null;
        var chars = new List<float>();
        var sb = new System.Text.StringBuilder();
        foreach (var (text, p) in tokens)
        {
            var r = Kana.ToHiragana(Reading.Of(text, map));
            sb.Append(r);
            for (int i = 0; i < r.Length; i++) chars.Add(p);
        }
        if (sb.ToString() != string.Concat(heard)) return null;
        var res = new float[heard.Count];
        int pos = 0;
        for (int i = 0; i < heard.Count; i++)
        {
            float m = 1;
            for (int k = 0; k < heard[i].Length; k++) m = Math.Min(m, chars[pos + k]);
            res[i] = m; pos += heard[i].Length;
        }
        return res;
    }

    /// <summary>
    /// Плашки мор: зелёная — верно, красная — пропущено или заменено («слышно: …»), жёлтая зачёркнутая — лишнее.
    /// Оранжевая рамка — мора распознана, но модель была не уверена (звучало нечётко).
    /// </summary>
    public static Control MoraChips(string targetJp, string targetKana, string heardKana, (string text, float p)[]? tokens)
    {
        var t = Pitch.Moras(Kana.ToHiragana(Speech.Normalize(targetKana)));
        var h = Pitch.Moras(Kana.ToHiragana(Speech.Normalize(heardKana)));
        var probs = MoraProbs(h, tokens, Reading.Align(targetJp, targetKana));
        int n = t.Count, m = h.Count;
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                dp[i, j] = Same(t[i], h[j]) ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
        var ops = new List<(char op, string t, string h, float p)>();
        int x = 0, y = 0;
        while (x < n || y < m)
        {
            if (x < n && y < m && Same(t[x], h[y])) { ops.Add(('=', t[x], h[y], probs?[y] ?? 1)); x++; y++; }
            else if (y < m && (x >= n || dp[x, y + 1] >= dp[x + 1, y])) { ops.Add(('+', "", h[y], 1)); y++; }
            else { ops.Add(('-', t[x], "", 1)); x++; }
        }
        // пропуск + лишнее рядом = замена одной моры на другую
        for (int i = 0; i + 1 < ops.Count; i++)
            if ((ops[i].op, ops[i + 1].op) is ('-', '+') or ('+', '-'))
            {
                var (tt, hh) = ops[i].op == '-' ? (ops[i].t, ops[i + 1].h) : (ops[i + 1].t, ops[i].h);
                ops[i] = ('~', tt, hh, 1); ops.RemoveAt(i + 1);
            }

        var wrap = new WrapPanel();
        foreach (var (op, tm, hm, p) in ops)
        {
            string kana = op == '+' ? hm : tm;
            bool unclear = op == '=' && p < 0.45f;
            var (bg, fg) = op switch { '=' => ("GoodSoft", "Good"), '+' => ("Surface2", "Gold"), _ => ("BadSoft", "Bad") };
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = kana, FontFamily = U.R<FontFamily>("JpFont"), FontSize = 19, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = U.B(op == '=' ? "Text" : fg), TextDecorations = op == '+' ? TextDecorations.Strikethrough : null,
            });
            sp.Children.Add(new TextBlock { Text = Kana.ToRomaji(kana), FontSize = 10.5, Foreground = U.B("Muted"), HorizontalAlignment = HorizontalAlignment.Center });
            if (op == '~') sp.Children.Add(new TextBlock { Text = "слышно: " + hm, FontSize = 10, Foreground = U.B("Bad"), HorizontalAlignment = HorizontalAlignment.Center });
            var chip = new Border
            {
                Child = sp, Background = U.B(bg), CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 4), Margin = new Thickness(0, 0, 5, 5), MinWidth = 38,
                BorderBrush = unclear ? U.B("Gold") : U.B(fg), BorderThickness = new Thickness(unclear ? 2 : 1),
            };
            chip.Tip(op switch
            {
                '=' when unclear => $"Распознано, но нечётко (уверенность {p:P0}) — произнеси эту мору яснее",
                '=' => "Верно",
                '+' => "Лишний звук",
                '~' => $"Нужно «{tm}», а прозвучало «{hm}»",
                _ => "Пропущено",
            });
            wrap.Children.Add(chip);
        }
        return U.Stack(wrap, U.T("зелёный — верно · красный — ошибка · жёлтый — лишнее · оранжевая рамка — нечётко", "Muted", 11).M(0, 2, 0, 0));
    }

    /// <summary>Сколько мор подряд прозвучали нечётко — для подсказки «говори яснее».</summary>
    public static int UnclearCount((string text, float p)[]? tokens) => tokens?.Count(t => t.p < 0.45f && Speech.Normalize(t.text).Length > 0) ?? 0;

    /// <summary>
    /// Питч-акцент отдельного слова: тон голоса делится на моры, в каждой берётся медиана высоты,
    /// и рисунок «высоко/низко» сравнивается со словарным (токийская норма).
    /// </summary>
    public static (List<(string kana, double pitch, bool accentEnd)> user, List<(string kana, double pitch, bool accentEnd)> target, int ok, int total)? AccentCheck(string jp, string kana, byte[] wav)
    {
        var pat = Pitch.Pattern(jp, kana);
        if (pat == null) return null;
        var word = pat.Where(p => !p.kana.StartsWith("(")).ToList();
        if (word.Count < 2) return null;
        var (pitch, _) = Prosody.Analyze(Prosody.FromWav(wav));
        int first = Array.FindIndex(pitch, v => !double.IsNaN(v)), last = Array.FindLastIndex(pitch, v => !double.IsNaN(v));
        if (first < 0 || last - first < word.Count * 4) return null;
        var med = new double[word.Count];
        for (int i = 0; i < word.Count; i++)
        {
            int a = first + (last - first) * i / word.Count, b = first + (last - first) * (i + 1) / word.Count;
            int pad = (b - a) / 5; // середина моры: края смазаны переходами между слогами
            var v = pitch[(a + pad)..Math.Max(a + pad + 1, b - pad)].Where(z => !double.IsNaN(z)).OrderBy(z => z).ToArray();
            med[i] = v.Length > 0 ? v[v.Length / 2] : double.NaN;
        }
        var known = med.Where(z => !double.IsNaN(z)).ToArray();
        if (known.Length < 2) return null;
        double lo = known.Min(), hi = known.Max(), mid = (lo + hi) / 2;
        bool flat = hi - lo < 1.0; // меньше полутона — тон почти не меняется
        var user = new List<(string, double, bool)>();
        int ok = 0;
        for (int i = 0; i < word.Count; i++)
        {
            double u = double.IsNaN(med[i]) ? (i > 0 ? user[i - 1].Item2 : 0) : flat ? 0.5 : (med[i] >= mid ? 1 : 0);
            user.Add((word[i].kana, u, false));
            if (!flat && (u >= 0.5) == (word[i].pitch >= 0.5)) ok++;
        }
        return (user, word, ok, word.Count);
    }

    public static Control? AccentPanel(string jp, string kana, byte[] wav)
    {
        if (Speech.Normalize(jp).Length > 8 || jp.Contains(' ')) return null; // только отдельные слова
        var r = AccentCheck(jp, kana, wav);
        if (r == null) return null;
        var (user, target, ok, total) = r.Value;
        bool good = ok == total;
        string verdict = good ? "Питч-акцент верный — тон поднимается и падает как у носителя."
            : ok == 0 && user.All(u => Math.Abs(u.pitch - 0.5) < 0.01) ? "Голос звучит ровно — у японского слова есть рисунок тона: выдели высокие моры голосом."
            : $"Тон совпал на {ok} из {total} мор. Сравни линии и послушай образец ещё раз.";
        var grid = U.Cols("auto", "*");
        grid.RowDefinitions = new RowDefinitions("auto,auto");
        grid.Children.Add(U.T("Образец", "Muted", 12).CenterV());
        grid.Children.Add(new Controls.AccentView(target).Col(1).M(10, 0, 0, 0));
        grid.Children.Add(U.T("Ты", "Muted", 12).CenterV().RowAt(1));
        grid.Children.Add(new Controls.AccentView(user).Col(1).RowAt(1).M(10, 0, 0, 0));
        return U.Stack(
            U.T("ПИТЧ-АКЦЕНТ", "Overline"),
            U.T(verdict, "Body", 13, U.B(good ? "Good" : "Gold")).M(0, 4, 0, 6),
            grid);
    }
}
