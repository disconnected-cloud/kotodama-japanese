using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>
/// Дневник на японском: тема дня с шаблонами, свободная запись, разбор написанного (чтение, латиница,
/// слова из словаря), озвучка своего текста. Запись дня даёт XP, записи хранятся в прогрессе.
/// </summary>
public sealed class JournalPage : UserControl, IPage
{
    public string Title => "Дневник";

    sealed record Prompt(string Ru, string Jp, string[] Templates);

    static readonly Prompt[] Prompts =
    {
        new("Что ты сегодня делал?", "今日は何をしましたか。", new[] { "今日は〜を食べました。", "〜へ行きました。", "〜時に起きました。" }),
        new("Что ты ел на завтрак?", "朝ご飯に何を食べましたか。", new[] { "朝ご飯にパンを食べました。", "〜を飲みました。", "おいしかったです。" }),
        new("Какая сегодня погода?", "今日の天気はどうですか。", new[] { "今日は晴れです。", "少し寒いです。", "雨が降っています。" }),
        new("Расскажи о своей семье", "家族について書いてください。", new[] { "私の家族は〜人です。", "父は〜です。", "姉が一人います。" }),
        new("Какое у тебя хобби?", "趣味は何ですか。", new[] { "趣味は〜ことです。", "週末に〜をします。", "〜のが好きです。" }),
        new("Что ты хочешь сделать в Японии?", "日本で何をしたいですか。", new[] { "京都へ行きたいです。", "〜を食べてみたいです。", "富士山に登りたいです。" }),
        new("Опиши свою комнату", "あなたの部屋はどんな部屋ですか。", new[] { "私の部屋は〜です。", "机の上に〜があります。", "窓から〜が見えます。" }),
        new("Что ты будешь делать на выходных?", "週末は何をしますか。", new[] { "土曜日に〜つもりです。", "友達と〜。", "家でゆっくりします。" }),
        new("Какое твоё любимое блюдо?", "好きな食べ物は何ですか。", new[] { "一番好きな食べ物は〜です。", "〜より〜のほうが好きです。", "毎週〜を作ります。" }),
        new("Как ты учишь японский?", "どうやって日本語を勉強していますか。", new[] { "毎日〜分勉強しています。", "アプリで〜を覚えます。", "漢字は難しいですが、楽しいです。" }),
        new("Что было интересного на этой неделе?", "今週、何か面白いことがありましたか。", new[] { "〜がありました。", "〜に会いました。", "とても楽しかったです。" }),
        new("Каким был твой день: устал или полон сил?", "今日はどんな一日でしたか。", new[] { "今日は忙しかったです。", "少し疲れました。", "でも、いい一日でした。" }),
        new("Опиши своего друга", "友達はどんな人ですか。", new[] { "友達は〜な人です。", "背が高くて、優しいです。", "いつも〜。" }),
        new("Какое время года ты любишь и почему?", "どの季節が好きですか。どうしてですか。", new[] { "〜が一番好きです。", "〜から。", "〜とき、〜ができます。" }),
        new("Что ты купил недавно?", "最近何を買いましたか。", new[] { "〜を買いました。", "〜円でした。", "〜し、〜し、気に入っています。" }),
        new("О чём ты мечтаешь?", "あなたの夢は何ですか。", new[] { "夢は〜ことです。", "〜になりたいです。", "そのために〜。" }),
        new("Что ты делал вчера вечером?", "昨日の夜は何をしましたか。", new[] { "昨日の夜、〜を見ました。", "〜時に寝ました。", "〜ながら〜。" }),
        new("Если бы у тебя был свободный день…", "もし一日休みがあったら、何をしますか。", new[] { "休みがあったら、〜たいです。", "〜に行ってみたいです。", "一日中〜。" }),
    };

    static string Today => DateTime.Today.ToString("yyyy-MM-dd");
    static Prompt TodayPrompt => Prompts[DateTime.Today.DayOfYear % Prompts.Length];
    static Dictionary<string, string> J => ProgressStore.P.Journal;

    readonly TextBox _box = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 140, FontSize = 20,
        Watermark = "Пиши по-японски (включи японскую раскладку: Win + Пробел) — или латиницей, kana появится в разборе",
    };
    readonly StackPanel _analysis = new();
    DispatcherTimer? _debounce;

    public void OnShown()
    {
        _box.FontFamily = U.R<FontFamily>("JpFont");
        var root = new StackPanel { MaxWidth = 900 };
        int days = J.Count(kv => kv.Value.Trim().Length > 0);
        int chars = J.Values.Sum(v => v.Count(c => c is >= '぀' and <= 'ヿ' or >= '一' and <= '鿿'));
        root.Children.Add(U.Header("記", "Дневник", days == 0
            ? "Пара фраз по-японски каждый день — лучший способ начать говорить. Тема дня и шаблоны помогут, разбор покажет чтение и слова."
            : $"Записей: {days} · японских знаков написано: {chars} · серия: {Streak()} дн."));

        // Тема дня
        var p = TodayPrompt;
        var tpl = new WrapPanel();
        foreach (var t in p.Templates)
        {
            var tt = t;
            var b = new Button { Content = U.Jp(tt, 15), Padding = new Thickness(12, 6), Margin = new Thickness(0, 0, 8, 8) };
            b.Click += (_, _) => Insert(tt);
            ToolTip.SetTip(b, "Вставить шаблон — замени 〜 своими словами");
            tpl.Children.Add(b);
        }
        root.Children.Add(U.Card(U.Stack(
            U.T("ТЕМА ДНЯ", "Overline"),
            U.T(p.Ru, "H2").M(0, 4, 0, 2),
            U.Row(8, U.Jp(p.Jp, 18, U.B("Accent")).CenterV(), U.SpeakBtn(() => p.Jp).CenterV()),
            U.T("Шаблоны (нажми, чтобы вставить):", "Muted", 12.5).M(0, 12, 0, 6),
            tpl), new Thickness(22, 18, 22, 12)).M(0, 0, 0, 14));

        // Запись
        _box.Text = J.GetValueOrDefault(Today, "");
        _box.TextChanged -= OnText;
        _box.TextChanged += OnText;
        var acts = U.Row(8,
            U.Btn("Сохранить", Save, "Primary", ""),
            U.Btn("Прочитать вслух", () => { if ((_box.Text ?? "").Trim() is { Length: > 0 } t) Speech.SayLong(Reading.DigitsToKanji(t)); }, "", ""),
            U.Btn("Медленно", () => { if ((_box.Text ?? "").Trim() is { Length: > 0 } t) Speech.SayLong(Reading.DigitsToKanji(t), true); }, "", ""));
        root.Children.Add(U.Card(U.Stack(U.T($"Запись за {DateTime.Today:d MMMM}", "H3", 15).M(0, 0, 0, 10), _box.Detach(), acts.M(0, 12, 0, 0)), new Thickness(22, 18, 22, 18)));
        root.Children.Add(_analysis.Detach().M(0, 14, 0, 0));
        Analyze();

        // Прошлые записи
        var past = J.Where(kv => kv.Key != Today && kv.Value.Trim().Length > 0).OrderByDescending(kv => kv.Key).Take(30).ToList();
        if (past.Count > 0)
        {
            root.Children.Add(U.T("Прошлые записи", "H2").M(0, 24, 0, 10));
            foreach (var (date, text) in past)
            {
                var g = U.Cols("*", "auto");
                var d = DateTime.TryParse(date, out var dt) ? dt.ToString("d MMMM yyyy") : date;
                g.Children.Add(U.Stack(U.T(d, "Overline"), U.Jp(text, 16).M(0, 4, 0, 0)));
                var tx = text;
                g.Children.Add(U.SpeakBtn(() => Reading.DigitsToKanji(tx)).Col(1).Also(b => b.VerticalAlignment = VerticalAlignment.Top));
                root.Children.Add(U.Card(g, new Thickness(18, 12, 12, 12)).M(0, 0, 0, 8));
            }
        }
        Content = U.Page(root);
    }

    void Insert(string t)
    {
        var cur = _box.Text ?? "";
        _box.Text = cur.Length == 0 || cur.EndsWith('\n') ? cur + t : cur + "\n" + t;
        _box.CaretIndex = _box.Text.Length;
        _box.Focus();
    }

    void OnText(object? s, TextChangedEventArgs e)
    {
        _debounce?.Stop();
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(450), DispatcherPriority.Background, (_, _) => { _debounce?.Stop(); Analyze(); });
        _debounce.Start();
    }

    /// <summary>Латиница в поле → кана (для тех, у кого нет японской раскладки).</summary>
    static string FromRomaji(string t) => t.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z') && !t.Any(c => c is >= '぀' and <= 'ヿ' or >= '一' and <= '鿿')
        ? Kana.FromRomaji(t.ToLowerInvariant()) : t;

    async void Analyze()
    {
        _analysis.Children.Clear();
        var text = FromRomaji((_box.Text ?? "").Trim());
        if (text.Length == 0) return;
        if (!JDict.Loaded)
        {
            _analysis.Children.Add(U.T("Загружаю словарь для разбора…", "Muted"));
            await JDict.LoadAsync();
            if (text != FromRomaji((_box.Text ?? "").Trim())) return;
            _analysis.Children.Clear();
        }
        var sp = new StackPanel();
        sp.Children.Add(U.T("РАЗБОР", "Overline").M(0, 0, 0, 8));
        if (text != (_box.Text ?? "").Trim()) sp.Children.Add(U.Jp(text, 18).M(0, 0, 0, 4));
        string hira;
        try { hira = Kana.ToHiragana(Reading.Of(Reading.DigitsToKanji(text))); } catch { hira = ""; }
        if (hira.Length > 0 && hira != text) sp.Children.Add(U.Jp(hira, 15, U.B("Muted")));
        if (hira.Length > 0) sp.Children.Add(U.T(Reading.RomajiWords(text), "Muted", 13).M(0, 2, 0, 10));

        var words = new WrapPanel();
        var seen = new HashSet<string>();
        foreach (var line in text.Split('\n'))
        {
            int i = 0;
            while (i < line.Length)
            {
                var (len, e) = JDict.Longest(line, i);
                if (len <= 0) len = 1;
                if (e != null && seen.Add(e.Jp) && e.Jp.Any(c => c is >= '一' and <= '鿿' or >= 'ァ' and <= 'ヶ' || e.Kana.Length > 2))
                {
                    var entry = e;
                    var b = new Button
                    {
                        Content = U.Stack(U.Jp(e.Jp, 15), U.T(e.Jp != e.Kana ? e.Kana : "", "Muted", 12), U.T(Short(e.Ru), "Muted", 12).Also(t => t.MaxWidth = 170)),
                        Padding = new Thickness(10, 6), Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Top,
                    };
                    b.Click += (_, _) => Shell.Open(new WordPage(entry));
                    words.Children.Add(b);
                }
                i += len;
            }
        }
        if (words.Children.Count > 0)
        {
            sp.Children.Add(U.T("Слова — нажми, чтобы открыть в словаре:", "Muted", 12.5).M(0, 0, 0, 6));
            sp.Children.Add(words);
        }
        int kanji = text.Count(c => c is >= '一' and <= '鿿');
        sp.Children.Add(U.T($"Знаков: {text.Count(c => !char.IsWhiteSpace(c))} · кандзи: {kanji} · слов из словаря: {seen.Count}", "Muted", 12).M(0, 6, 0, 0));
        _analysis.Children.Add(U.Card(sp, new Thickness(22, 16, 22, 16)));
    }

    static string Short(string ru)
    {
        var s = ru.Split(';')[0].Trim();
        return s.Length > 40 ? s[..40] + "…" : s;
    }

    void Save()
    {
        var text = FromRomaji((_box.Text ?? "").Trim());
        if (text.Length == 0) { Shell.Toast("Напиши хотя бы одну фразу", "記"); return; }
        bool first = J.GetValueOrDefault(Today, "").Trim().Length == 0;
        J[Today] = text;
        if (first)
        {
            int jp = text.Count(c => c is >= '぀' and <= 'ヿ' or >= '一' and <= '鿿');
            ProgressStore.AddXp(Math.Clamp(jp / 4, 5, 25));
            Sfx.Done();
            Shell.Toast($"Запись сохранена · серия дневника: {Streak()} дн.", "記");
        }
        else Shell.Toast("Запись обновлена", "記");
        ProgressStore.Notify();
        OnShown();
    }

    /// <summary>Сколько дней подряд (по сегодня или вчера) есть записи.</summary>
    public static int Streak()
    {
        var d = DateTime.Today;
        if (!J.ContainsKey(d.ToString("yyyy-MM-dd"))) d = d.AddDays(-1);
        int n = 0;
        while (J.TryGetValue(d.ToString("yyyy-MM-dd"), out var t) && t.Trim().Length > 0) { n++; d = d.AddDays(-1); }
        return n;
    }
}
