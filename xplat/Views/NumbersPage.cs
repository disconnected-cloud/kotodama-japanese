using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using RadioButton = Matsubakira.Compat.RadioButton;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>Тренажёр «Числа, время, даты, цены»: на слух с вводом цифр и чтение со звуковыми изменениями.</summary>
public sealed class NumbersPage : UserControl, IPage
{
    public string Title => "Числа и время";
    static int _tier = 1; // 0 — до 100, 1 — до 10 000, 2 — до 100 000 000

    public NumbersPage() => Build();

    void Build()
    {
        var root = new StackPanel { MaxWidth = 1080 };
        var tiers = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (i, name) in new[] { (0, "до 100"), (1, "до 10 000"), (2, "до 100 млн") })
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = name, GroupName = "ntier", IsChecked = _tier == i };
            rb.Checked += (_, _) => _tier = i;
            tiers.Children.Add(rb);
        }
        root.Children.Add(U.Header("数", "Числа и время", "Цифры на слух, цены, время и даты — с особыми чтениями: さんびゃく, はっせん, よじ, ついたち.", tiers));

        var grid = new UniformGrid { Columns = U.Columns(3), Margin = new Thickness(-6, 0, -6, 0) };
        grid.Children.Add(Mode("headphone", "Числа на слух", "Слушай и записывай цифрами", () => Shell.Open(ListenNumbers())));
        grid.Children.Add(Mode("input_numbers", "Прочитай число", "Выбери правильное чтение", () => Shell.Open(ReadNumbers())));
        grid.Children.Add(Mode("money_bag", "Цены на слух", "Сколько стоит? Пиши сумму в иенах", () => Shell.Open(Prices())));
        grid.Children.Add(Mode("alarm_clock", "Который час", "Время: よじ, くじ, じゅっぷん, はん", () => Shell.Open(Times())));
        grid.Children.Add(Mode("spiral_calendar", "Даты", "Месяцы и дни: ついたち, はつか…", () => Shell.Open(Dates())));
        grid.Children.Add(Mode("stopwatch", "Смешанный марафон", "Всё вперемешку — 20 вопросов", () => Shell.Open(Marathon())));
        root.Children.Add(grid);

        root.Children.Add(U.T("Шпаргалка: звуковые изменения", "H2").M(0, 26, 0, 4));
        root.Children.Add(U.T("Нажми на чтение, чтобы послушать. Красным — то, что меняется.", "Muted").M(0, 0, 0, 12));
        root.Children.Add(Table("Сотни · 百", Enumerable.Range(1, 9).Select(i => ($"{i}00", Numbers.Read(i * 100))), "びゃく|ぴゃく|ろっ|はっ"));
        root.Children.Add(Table("Тысячи · 千", Enumerable.Range(1, 9).Select(i => ($"{i}000", Numbers.Read(i * 1000))), "ぜん|はっ"));
        root.Children.Add(Table("Десятки тысяч · 万", new[] { 1, 10, 100, 1000 }.Select(i => ($"{i * 10000:N0}".Replace(',', ' '), Numbers.Read(i * 10000L))), "いっ"));
        root.Children.Add(Table("Часы · 時", Enumerable.Range(1, 12).Select(i => ($"{i}時", Numbers.Time(i, 0))), "よじ|しちじ|くじ"));
        root.Children.Add(Table("Минуты · 分", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 15, 30 }.Select(i => ($"{i}分", Numbers.Minutes(i))), "っぷん|ぷん"));
        root.Children.Add(Table("Месяцы · 月", Enumerable.Range(1, 12).Select(i => ($"{i}月", Numbers.Month(i))), "しがつ|しちがつ|くがつ"));
        root.Children.Add(Table("Дни месяца · 日", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 14, 20, 24 }.Select(i => ($"{i}日", Numbers.Day(i))), "ついたち|ふつか|みっか|よっか|いつか|むいか|なのか|ようか|ここのか|とおか|はつか"));
        Anim.Stagger(grid, 35, 60);
        Content = U.Page(root);
    }

    static Border Mode(string emoji, string title, string sub, Action go)
    {
        var sp = U.Stack(Emoji.Img(emoji, 44).Also(i => i.HorizontalAlignment = HorizontalAlignment.Left), U.T(title, "H3", 15).M(0, 10, 0, 3), U.T(sub, "Muted").Also(t => t.MinHeight = 34));
        var card = new Border { Classes = { "Card" }, Child = sp, Margin = new Thickness(6), Cursor = Cursors.Hand };
        HomePage.Hover(card);
        card.PointerReleased += (_, _) => { Shell.Main.PendingExpand = card; go(); };
        return card;
    }

    static Control Table(string title, IEnumerable<(string label, string reading)> items, string highlight)
    {
        var marks = highlight.Split('|');
        var wrap = new WrapPanel();
        foreach (var (label, reading) in items)
        {
            var r = reading;
            bool special = marks.Any(r.Contains);
            var b = new Button
            {
                Content = U.Stack(U.T(label, "Muted", 11).Center(), U.Jp(r, 15, special ? U.B("Accent") : null).Center()),
                Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 6, 6),
            };
            b.Click += (_, _) => Speech.Say(r);
            wrap.Children.Add(b);
        }
        var g = U.Cols("170", "*");
        g.Children.Add(U.T(title, "H3", 14).M(0, 8, 12, 0));
        g.Children.Add(wrap.Col(1));
        return U.Card(g, new Thickness(14)).M(0, 0, 0, 8);
    }

    // ─────────── генераторы вопросов ───────────
    static long RandomNumber()
    {
        var r = U.Rng;
        return _tier switch
        {
            0 => r.Next(1, 101),
            1 => r.Next(3) == 0 ? r.Next(100, 1000) : r.Next(1000, 10000),
            // круглые суммы встречаются чаще, чем 73 815 492
            _ => r.Next(3) switch { 0 => r.Next(10, 1000) * 1000L, 1 => r.Next(1, 10000) * 10000L, _ => r.Next(10000, 100_000_000) },
        };
    }

    static string Digits(long n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");

    static QuizQ Listen(long n, string reading, string sub, string suffix = "") => new()
    {
        Prompt = "🔊", PromptJp = false, PromptSize = 64, Sub = sub, Typing = true, SpeakOnShow = true, Speak = reading,
        Answer = n.ToString(), Norm = Numbers.DigitsOnly,
        Explain = $"{Digits(n)}{suffix} — {reading} ({Kana.ToRomaji(reading)}) · {Numbers.Kanji(n)}{suffix}",
    };

    static string[] Options(string correct, Func<string> other)
    {
        var opts = new List<string>(Numbers.Mistakes(correct).Where(m => m != correct).Distinct().Shuffled().Take(2));
        for (int guard = 0; opts.Count < 3 && guard < 30; guard++) { var o = other(); if (o != correct && !opts.Contains(o)) opts.Add(o); }
        return opts.Append(correct).Shuffled().ToArray();
    }

    static QuizQ ReadQ(long n)
    {
        var reading = Numbers.Read(n);
        return new QuizQ
        {
            Prompt = Digits(n), PromptJp = false, PromptSize = n > 99999 ? 54 : 72, Sub = "Как читается это число?", Answer = reading, OptionsJp = true,
            Options = Options(reading, () => Numbers.Read(Math.Max(1, n + U.Rng.Next(-3, 4) * (long)Math.Pow(10, U.Rng.Next(0, (int)Math.Log10(n) + 1))))),
            Speak = reading, Explain = $"{Digits(n)} — {reading} · {Numbers.Kanji(n)}",
        };
    }

    static QuizQ TimeQ()
    {
        int h = U.Rng.Next(1, 13), m = U.Rng.Next(4) == 0 ? 0 : U.Rng.Next(12) * 5 + (U.Rng.Next(3) == 0 ? U.Rng.Next(1, 5) : 0);
        var reading = Numbers.Time(h, m);
        var label = m == 0 ? $"{h}時" : $"{h}時{m}分";
        if (U.Rng.Next(2) == 0)
            return new QuizQ
            {
                Prompt = "🔊", PromptJp = false, PromptSize = 64, Sub = "Который час? Напиши как 7:30", Typing = true, SpeakOnShow = true, Speak = reading,
                Answer = $"{h}:{m:00}|{h}{m:00}", Norm = Numbers.DigitsOnly, Explain = $"{label} — {reading} ({Kana.ToRomaji(reading)})",
            };
        return new QuizQ
        {
            Prompt = label, PromptSize = 64, Sub = "Как сказать это время?", Answer = reading, OptionsJp = true,
            Options = Options(reading, () => Numbers.Time(U.Rng.Next(1, 13), m)), Speak = reading, Explain = $"{label} — {reading}",
        };
    }

    static QuizQ DateQ()
    {
        int mo = U.Rng.Next(1, 13), d = U.Rng.Next(3) == 0 ? new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 14, 20, 24 }[U.Rng.Next(13)] : U.Rng.Next(1, 29);
        var reading = Numbers.Date(mo, d);
        var label = $"{mo}月{d}日";
        if (U.Rng.Next(3) == 0)
            return new QuizQ
            {
                Prompt = "🔊", PromptJp = false, PromptSize = 64, Sub = "Какое число? Напиши день месяца цифрами", Typing = true, SpeakOnShow = true, Speak = Numbers.Day(d),
                Answer = d.ToString(), Norm = Numbers.DigitsOnly, Explain = $"{d}日 — {Numbers.Day(d)}",
            };
        return new QuizQ
        {
            Prompt = label, PromptSize = 60, Sub = "Как прочитать дату?", Answer = reading, OptionsJp = true,
            Options = Options(reading, () => Numbers.Date(mo, Math.Clamp(d + U.Rng.Next(-2, 3), 1, 31))), Speak = reading, Explain = $"{label} — {reading}",
        };
    }

    static QuizQ PriceQ()
    {
        long n = _tier == 0 ? U.Rng.Next(1, 100) * 10 : U.Rng.Next(2) == 0 ? U.Rng.Next(1, 100) * 10 : U.Rng.Next(1, 300) * 100 + (U.Rng.Next(3) == 0 ? U.Rng.Next(1, 10) * 10 : 0);
        if (U.Rng.Next(8) == 0) n = new[] { 4, 14, 104, 444, 1004 }[U.Rng.Next(5)];
        var item = new[] { "コーヒー", "ラーメン", "本", "傘", "切符", "お弁当", "シャツ", "電車", "ケーキ", "映画" }[U.Rng.Next(10)];
        var reading = Numbers.Yen(n);
        return Listen(n, $"{item}は {reading}です", $"Сколько стоит {item}? Напиши сумму в иенах", " 円").Also(q => q.Explain = $"{Digits(n)} 円 — {reading}");
    }

    static QuizQ ListenQ() { var n = RandomNumber(); return Listen(n, Numbers.Read(n), "Напиши услышанное число цифрами"); }
    public static QuizSession ListenNumbers() => new("Числа на слух", Enumerable.Range(0, 12).Select(_ => ListenQ()));
    public static QuizSession ReadNumbers() => new("Прочитай число", Enumerable.Range(0, 12).Select(_ => ReadQ(RandomNumber())));
    public static QuizSession Prices() => new("Цены на слух", Enumerable.Range(0, 12).Select(_ => PriceQ()));
    public static QuizSession Times() => new("Который час", Enumerable.Range(0, 12).Select(_ => TimeQ()));
    public static QuizSession Dates() => new("Даты", Enumerable.Range(0, 12).Select(_ => DateQ()));
    public static QuizSession Marathon() => new("Числа: марафон", Enumerable.Range(0, 20).Select(i => (i % 5) switch
    {
        0 => ReadQ(RandomNumber()), 1 => TimeQ(), 2 => DateQ(), 3 => PriceQ(),
        _ => ListenQ(),
    }));
}
