using System.Text.Json;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RadioButton = Matsubakira.Compat.RadioButton;
using CheckBox = Matsubakira.Compat.CheckBox;
using ToggleButton = Matsubakira.Compat.ToggleButton;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

public sealed record RToken(string Surface, string Reading, string Lemma, string Pos);
public sealed record RSentence(string Jp, string Ru, RToken[] Tokens);
public sealed record RText(string Title, string Level, RSentence[] Sentences, (string q, string a, string[] opts)[] Questions, string? Book = null);

/// <summary>Читалка: тексты с фуриганой, озвучкой, переводом и словарём по клику; можно вставить свой текст.</summary>
public sealed class ReaderPage : UserControl, IPage
{
    // ─────────── Сколько слов текста уже знакомо ───────────
    static readonly HashSet<string> Grammar = new() { "助詞", "助動詞", "補助記号", "記号", "空白" };
    static (int ver, HashSet<string> set) _known = (-1, new());

    /// <summary>Слова, которые уже в изучении (карточка повторялась хотя бы раз) — по написанию и чтению.</summary>
    static HashSet<string> Known()
    {
        if (_known.ver == ProgressStore.Version) return _known.set;
        var s = new HashSet<string>();
        foreach (var w in Db.Words)
            if (ProgressStore.Card(w.Id)?.Reps > 0) { s.Add(w.Jp); s.Add(Kana.ToHiragana(w.Kana)); }
        foreach (var c in ProgressStore.P.CustomWords) { s.Add(c[0]); s.Add(Kana.ToHiragana(c[1])); }
        _known = (ProgressStore.Version, s);
        return s;
    }

    /// <summary>Процент знакомых слов в тексте (частицы и знаки не считаются).</summary>
    public static int KnownPercent(RText t)
    {
        var known = Known();
        if (known.Count == 0) return 0;
        int all = 0, ok = 0;
        foreach (var tok in t.Sentences.SelectMany(s => s.Tokens))
        {
            if (Grammar.Contains(tok.Pos)) continue;
            all++;
            if (known.Contains(tok.Lemma) || known.Contains(tok.Surface) || (tok.Reading.Length > 0 && known.Contains(Kana.ToHiragana(tok.Reading)))) ok++;
        }
        return all == 0 ? 0 : ok * 100 / all;
    }

    public string Title => "Книги и тексты";
    static readonly Lazy<RText[]> _texts = new(Load);

    public static RText[] Texts => _texts.Value;

    static readonly Dictionary<string, (string a, string b, string jp, string sub)> Covers = new()
    {
        ["Котоха едет в Японию"] = ("#FF9EC0", "#B23A78", "旅", "Повесть о первом месяце в Токио · N5"),
        ["Японские сказки"] = ("#8FB8FF", "#3446A8", "昔", "Момотаро, Урасима Таро и другие · N5–N4"),
        ["Год в Японии"] = ("#7FE0B0", "#1E8A64", "季", "Праздники и обычаи по сезонам · N4"),
        ["Пропавший кот"] = ("#FFC56B", "#C76A12", "謎", "Детектив по соседству · N4"),
        // настоящая японская литература (общественное достояние, тексты — Аодзора Бунко)
        ["Лисёнок Гон"] = ("#F4A259", "#A2421E", "狐", "Ниими Нанкити, «Гон-гицунэ», 1932 · 6 глав"),
        ["Паутинка"] = ("#B9C6D8", "#3C4A63", "糸", "Акутагава Рюноскэ, «Кумо-но ито», 1918 · 3 главы"),
        ["Ваш покорный слуга кот"] = ("#D9C2A0", "#6B4F2E", "猫", "Нацумэ Сосэки, «Вагахай ва нэко дэ ару», 1905 · начало"),
        ["Не сдаваясь дождю"] = ("#9FC9E8", "#2F5F8A", "雨", "Миядзава Кэндзи, «Амэ ни мо макэдзу», 1931 · стихотворение"),
        ["Хайку"] = ("#C8E0A8", "#4E7A2E", "句", "Басё, Бусон, Исса · 12 знаменитых трёхстиший"),
    };

    /// <summary>Русские книги о Японии (только описание — тексты новых книг защищены авторским правом).</summary>
    static readonly (string title, string author, string year, string about, bool free, string jp)[] RussianBooks =
    {
        ("Записки в плену у японцев", "Василий Головнин", "1816", "Капитан русского флота два года провёл в японском плену и подробно описал страну, закрытую от мира: законы, быт, характер людей.", true, "獄"),
        ("Фрегат «Паллада»", "Иван Гончаров", "1858", "Путевые очерки писателя, побывавшего в Нагасаки с посольством Путятина: первые русские впечатления о японских чиновниках, церемониях и еде.", true, "船"),
        ("Корни японского солнца", "Борис Пильняк", "1927", "Записки о поездке в Японию 1926 года: города, театр, люди — живо и субъективно.", false, "根"),
        ("Ветка сакуры", "Всеволод Овчинников", "1971", "Классика: журналист, семь лет проживший в Японии, объясняет японский характер, эстетику и обычаи.", false, "桜"),
        ("Книга японских обыкновений", "Александр Мещеряков", "1999", "Как жили японцы в старину: дом, еда, праздники, приметы — коротко и с юмором.", false, "習"),
    };

    /// <summary>Книги-оригиналы японских писателей (остальные — учебные повести для начинающих).</summary>
    static readonly HashSet<string> Classics = new() { "Лисёнок Гон", "Паутинка", "Ваш покорный слуга кот", "Не сдаваясь дождю", "Хайку" };

    /// <summary>Обложка книги: градиент, крупный иероглиф, прогресс по главам.</summary>
    static Control BookCard(string book, RText[] chapters)
    {
        var (ca, cb, jp, sub) = Covers.TryGetValue(book, out var c) ? c : ("#C59BFF", "#6A3FD0", "本", "");
        int read = chapters.Count(t => ProgressStore.P.Flags.Contains("read:" + t.Title));
        var cover = new Grid { Height = 150 };
        cover.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(16, 16, 0, 0),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse(ca), 0), new GradientStop(Color.Parse(cb), 1) },
            },
        });
        cover.Children.Add(new TextBlock { Text = jp, FontFamily = U.R<FontFamily>("JpSerif"), FontSize = 96, FontWeight = FontWeight.Bold, Foreground = Brushes.White, Opacity = 0.9, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 0, 6) });
        // на обложке — Википе-тан в подходящем образе
        var pic = Classics.Contains(book) ? new Border() : book.StartsWith("Котоха") ? Art.Anime("student", 260) : book.Contains("сказк") ? Art.Anime("reading", 180)
            : book.Contains("кот") ? Art.Anime("teacher", 250) : Art.Anime("casual", 260);
        pic.HorizontalAlignment = HorizontalAlignment.Right; pic.VerticalAlignment = VerticalAlignment.Top; pic.Margin = new Thickness(0, 8, 12, -60);
        cover.ClipToBounds = true;
        cover.Children.Add(pic);
        var info = U.Stack(U.T(book, "H3", 16), U.T(sub, "Muted", 12.5).M(0, 3, 0, 10),
            U.Cols("*", "auto").Also(g =>
            {
                g.Children.Add(new ProgressBar { Maximum = chapters.Length, Value = read, Height = 5, VerticalAlignment = VerticalAlignment.Center });
                g.Children.Add(U.T($"{read}/{chapters.Length} {MainView.Plural(chapters.Length, "глава", "главы", "глав")}", "Muted", 12).M(10, 0, 0, 0).Col(1));
            }));
        var card = new Border
        {
            Classes = { "Card" }, Padding = new Thickness(0), Margin = new Thickness(8), Cursor = Cursors.Hand, ClipToBounds = true,
            Child = U.Stack(cover, info.M(18, 14, 18, 16)),
        };
        HomePage.Hover(card);
        card.PointerReleased += (_, _) =>
        {
            var next = chapters.FirstOrDefault(t => !ProgressStore.P.Flags.Contains("read:" + t.Title)) ?? chapters[0];
            Shell.OpenFrom(card, new BookPage(book, chapters));
        };
        return card;
    }

    static RText[] Load()
    {
        using var s = typeof(ReaderPage).Assembly.GetManifestResourceStream("texts.json")!;
        using var doc = JsonDocument.Parse(s);
        return doc.RootElement.EnumerateArray().Select(t => new RText(
            t.GetProperty("title").GetString()!, t.GetProperty("level").GetString()!,
            t.GetProperty("sentences").EnumerateArray().Select(x => new RSentence(x.GetProperty("jp").GetString()!, x.GetProperty("ru").GetString()!,
                x.GetProperty("tokens").EnumerateArray().Select(k => new RToken(k[0].GetString()!, k[1].GetString()!, k[2].GetString()!, k[3].GetString()!)).ToArray())).ToArray(),
            t.GetProperty("questions").EnumerateArray().Select(q => (q[0].GetString()!, q[1].GetString()!, q[2].GetString()!.Split('|'))).ToArray(),
            t.TryGetProperty("book", out var b) ? b.GetString() : null)).ToArray();
    }

    public void OnShown()
    {
        var root = new StackPanel();
        root.Children.Add(U.Header("読", "Книги и тексты", "Японская классика в оригинале, учебные повести и рассказы: фуригана, озвучка, перевод по клику и «+» в карточки.", art: "globe"));
        // ─────────── Книги ───────────
        root.Children.Add(U.T("Японская литература в оригинале", "H2").M(0, 0, 0, 4));
        root.Children.Add(U.T("Настоящие произведения японских писателей — полный текст с фуриганой, озвучкой и переводом каждого предложения. Сложнее учебных текстов: читай с переводом по клику.", "Muted").Also(t => t.TextWrapping = TextWrapping.Wrap).M(0, 0, 0, 12));
        var classics = new UniformGrid { Columns = U.Columns(3), Margin = new Thickness(-8, 0, -8, 0) };
        foreach (var g in Texts.Where(t => t.Book != null && Classics.Contains(t.Book)).GroupBy(t => t.Book!))
            classics.Children.Add(BookCard(g.Key, g.ToArray()));
        root.Children.Add(classics);
        Anim.Stagger(classics, 50, 60);

        root.Children.Add(U.T("Учебные повести", "H2").M(0, 26, 0, 4));
        root.Children.Add(U.T("Написаны простым языком для уровней N5–N4: каждая глава — 8–11 предложений с озвучкой и вопросами.", "Muted").M(0, 0, 0, 12));
        var books = new UniformGrid { Columns = U.Columns(3), Margin = new Thickness(-8, 0, -8, 0) };
        foreach (var g in Texts.Where(t => t.Book != null && !Classics.Contains(t.Book)).GroupBy(t => t.Book!))
            books.Children.Add(BookCard(g.Key, g.ToArray()));
        root.Children.Add(books);
        Anim.Stagger(books, 50, 60);

        // ─────────── Русские книги о Японии ───────────
        root.Children.Add(U.T("Русские книги о Японии", "H2").M(0, 26, 0, 4));
        root.Children.Add(U.T("Что почитать по-русски о стране, языке и людях. Старые книги — в общественном достоянии (бесплатно в Викитеке), новые — в библиотеке или книжном.", "Muted").Also(t => t.TextWrapping = TextWrapping.Wrap).M(0, 0, 0, 12));
        var ru = new UniformGrid { Columns = U.Columns(2), Margin = new Thickness(-6, 0, -6, 0) };
        foreach (var (title, author, year, about, free, jp) in RussianBooks)
        {
            var g = U.Cols("auto", "*");
            g.Children.Add(new Border
            {
                Width = 54, Height = 72, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Top,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse(free ? "#C9A46A" : "#8C7BD9"), 0), new GradientStop(Color.Parse(free ? "#6E4A1F" : "#3E2F8F"), 1) },
                },
                Child = U.Jp(jp, 26, Brushes.White, true).Center().CenterV(),
            });
            g.Children.Add(U.Stack(U.T(title, "H3", 14.5), U.T($"{author} · {year}", "Muted", 12),
                U.T(about, "Body", 13).Also(t => t.TextWrapping = TextWrapping.Wrap).M(0, 6, 0, 6),
                U.Pill(free ? "общественное достояние" : "в библиотеке", free ? U.B("GoodSoft") : U.B("Surface2"), free ? U.B("Good") : U.B("Muted")).Also(b => b.HorizontalAlignment = HorizontalAlignment.Left))
                .M(14, 0, 0, 0).Col(1));
            ru.Children.Add(new Border { Classes = { "Card" }, Child = g, Margin = new Thickness(6) });
        }
        root.Children.Add(ru);

        // ─────────── Рассказы ───────────
        root.Children.Add(U.T("Короткие рассказы", "H2").M(0, 26, 0, 12));
        var grid = new UniformGrid { Columns = U.Columns(3), Margin = new Thickness(-6, 0, -6, 0) };
        // сначала то, что легче прочитать: больше знакомых слов, затем уровень попроще
        foreach (var t in Texts.Where(t => t.Book == null).OrderByDescending(KnownPercent).ThenBy(t => t.Level, StringComparer.Ordinal).ThenBy(t => t.Title))
        {
            bool done = ProgressStore.P.Flags.Contains("read:" + t.Title);
            int known = KnownPercent(t);
            var sp = U.Stack(Emoji.Img(Emoji.For(t.Title, "open_book"), 44).Also(i => { i.HorizontalAlignment = HorizontalAlignment.Left; i.Margin = new Thickness(0, 0, 0, 8); }),
                U.Row(8, U.Pill(t.Level), done ? U.Pill("✓ прочитано", U.B("GoodSoft"), U.B("Good")) : new Border(),
                    known > 0 ? U.Pill($"знаешь {known}% слов", known >= 80 ? U.B("GoodSoft") : U.B("Surface2"), known >= 80 ? U.B("Good") : U.B("Muted")) : new Border()),
                U.T(t.Title, "H3", 15).M(0, 10, 0, 4), U.Jp(t.Sentences[0].Jp, 14, U.B("Muted")).Also(U.Preview));
            var card = new Border { Classes = { "Card" }, Child = sp, Margin = new Thickness(6), Cursor = Cursors.Hand };
            HomePage.Hover(card);
            var text = t;
            card.OnClick(() => Shell.OpenFrom(card, new TextPage(text)));
            grid.Children.Add(card);
        }
        root.Children.Add(grid);
        Anim.Stagger(grid, 35, 60);

        // Свой текст
        var box = new TextBox { FontSize = 16, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, FontFamily = U.R<FontFamily>("JpFont") };
        var own = U.Stack(U.T("Свой текст", "H3", 15), U.T("Вставь японский текст или ссылку на статью (например, NHK News Web Easy) — он будет разобран по словарю.", "Muted").M(0, 4, 0, 10), box,
            U.Btn("Читать", async () =>
            {
                var text = box.Text?.Trim() ?? "";
                if (text.Length == 0) return;
                if (Uri.TryCreate(text, UriKind.Absolute, out var url) && url.Scheme is "http" or "https")
                {
                    Shell.Toast("Загружаю страницу…", "読");
                    try { text = await FetchJapanese(url); }
                    catch (Exception ex) { CrashReport.Warn("Загрузка статьи", ex); Shell.Toast("Не удалось открыть ссылку — проверь интернет или вставь текст", "!"); return; }
                    if (text.Length == 0) { Shell.Toast("На странице не нашлось японского текста", "!"); return; }
                }
                await OpenOwn(text, "Свой текст");
            }, "Primary", "").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 10, 0, 0); }),
            U.Btn("Открыть файл TXT или EPUB", OpenFile, "", "").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 8, 0, 0); }));
        root.Children.Add(U.Card(own).M(0, 18, 0, 0));
        Content = U.Page(root);
    }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Японский текст со страницы по ссылке.</summary>
    public static async Task<string> FetchJapanese(Uri url)
    {
        ProgressStore.EnsureNetworkAllowed();
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 Kotodama");
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        // не больше 5 МБ: ссылка на огромный файл не должна съесть память
        const int Max = 5 << 20;
        if (resp.Content.Headers.ContentLength > Max) throw new InvalidDataException("страница слишком большая");
        await using var src = await resp.Content.ReadAsStreamAsync();
        var ms = new MemoryStream();
        var buf = new byte[1 << 16]; int n;
        while ((n = await src.ReadAsync(buf)) > 0) { ms.Write(buf, 0, n); if (ms.Length > Max) throw new InvalidDataException("страница слишком большая"); }
        return ExtractJapanese(Decode(ms.ToArray(), resp.Content.Headers.ContentType?.CharSet));
    }

    /// <summary>
    /// Текст страницы в её кодировке: заголовок ответа, затем &lt;meta charset&gt;, иначе UTF-8. Shift_JIS и EUC-JP
    /// (в т.ч. Аодзора Бунко) требуют провайдера кодовых страниц — без него были «кракозябры» (Г11).
    /// </summary>
    public static string Decode(byte[] bytes, string? headerCharset)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var name = headerCharset?.Trim('"', ' ');
        if (string.IsNullOrEmpty(name))
        {
            var head = System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
            var m = System.Text.RegularExpressions.Regex.Match(head, @"charset\s*=\s*[""']?([\w\-]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) name = m.Groups[1].Value;
        }
        System.Text.Encoding enc;
        try { enc = string.IsNullOrEmpty(name) ? System.Text.Encoding.UTF8 : System.Text.Encoding.GetEncoding(name); }
        catch (ArgumentException) { enc = System.Text.Encoding.UTF8; }
        return enc.GetString(bytes);
    }

    /// <summary>
    /// Из HTML: без скриптов, стилей, меню и встроенной фуриганы (rt), только строки, где японских знаков больше половины.
    /// </summary>
    public static string ExtractJapanese(string html)
    {
        const System.Text.RegularExpressions.RegexOptions rx = System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        var s = System.Text.RegularExpressions.Regex.Replace(html, @"<(script|style|rt|rp|noscript|header|footer|nav)\b.*?</\1>", "", rx);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"<br\s*/?>|</p>|</div>|</h\d>|</li>", "\n", rx);
        s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "");
        s = System.Net.WebUtility.HtmlDecode(s);
        static bool Jp(char c) => c is >= '぀' and <= 'ヿ' or >= '一' and <= '鿿' or '。' or '、';
        var lines = s.Split('\n').Select(l => l.Trim()).Where(l => l.Length >= 6 && l.Count(Jp) * 2 > l.Length);
        return string.Join("\n", lines.Distinct());
    }

    /// <summary>Разбор произвольного текста: предложения по 。！？, слова — самым длинным совпадением со словарём.</summary>
    /// <summary>Свой текст из файла (8.2): TXT в любой японской кодировке или книга EPUB — без сети.</summary>
    static async void OpenFile()
    {
        var sp = TopLevel.GetTopLevel(Shell.Main)?.StorageProvider;
        if (sp == null) return;
        var files = await sp.OpenFilePickerAsync(new() { Title = "Японский текст", FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Текст или книга") { Patterns = new[] { "*.txt", "*.epub" } } } });
        if (files.Count == 0) return;
        try
        {
            await using var st = await files[0].OpenReadAsync();
            var ms = new MemoryStream(); await st.CopyToAsync(ms);
            var text = files[0].Name.EndsWith(".epub", StringComparison.OrdinalIgnoreCase) ? FromEpub(ms.ToArray()) : Decode(ms.ToArray(), null);
            if (text.Length > 200_000) text = text[..200_000]; // глава-другая, а не вся «Война и мир»: разбор идёт по словарю
            if (text.Trim().Length == 0) { Shell.Toast("В файле нет текста", "!"); return; }
            await OpenOwn(text, System.IO.Path.GetFileNameWithoutExtension(files[0].Name));
        }
        catch (Exception e) { CrashReport.Warn("Открытие файла", e); Shell.Toast("Не удалось открыть файл: " + e.Message, "!"); }
    }

    /// <summary>Текст книги EPUB: страницы XHTML по порядку архива, без разметки и фуриганы (rt).</summary>
    public static string FromEpub(byte[] epub)
    {
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(epub));
        var sb = new System.Text.StringBuilder();
        foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            using var r = new StreamReader(e.Open());
            sb.AppendLine(ExtractJapanese(r.ReadToEnd()));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Открыть свой текст. Сначала — доля знакомых слов (8.2): если незнакомых много, предлагаем выучить
    /// 5–10 из них перед чтением.
    /// </summary>
    static async Task OpenOwn(string text, string title)
    {
        await JDict.LoadAsync();
        var t = ParseOwn(text) with { Title = title };
        var words = t.Sentences.SelectMany(s => s.Tokens).Where(x => x.Pos != "記号" && x.Surface.Any(Reading.IsKanji)).Select(x => x.Surface).Distinct().ToList();
        var known = Db.Words.Where(w => ProgressStore.Card(w.Id) is { Reps: > 0 }).Select(w => w.Jp).ToHashSet();
        var unknown = words.Where(w => !known.Contains(w)).ToList();
        int pct = words.Count == 0 ? 100 : 100 * (words.Count - unknown.Count) / words.Count;
        if (unknown.Count >= 5 && pct < 85)
        {
            var pick = unknown.Select(w => JDict.Lookup(w)).OfType<DictEntry>().Where(e => e.Common).Take(10).ToList();
            if (pick.Count >= 3 && await Shell.Confirm($"Знакомых слов в тексте — {pct} %. Выучить сначала {pick.Count} незнакомых? Читать станет намного легче.", "Выучить слова", "Сразу читать"))
            {
                foreach (var e in pick) JDict.AddToStudy(e);
                var ids = pick.Select(e => "w:" + (e.Kanji.Length > 0 ? e.Kanji.Split('、')[0] : e.Kana) + "|" + e.Kana).ToHashSet();
                Shell.Open(new TextPage(t));
                Shell.Open(LearnSession.Words("Слова из текста", Db.Words.Where(w => ids.Contains(w.Id)).ToList()));
                return;
            }
        }
        Shell.Open(new TextPage(t));
    }

    static RText ParseOwn(string text)
    {
        var sents = new List<RSentence>();
        foreach (var raw in System.Text.RegularExpressions.Regex.Split(text.Replace("\r", ""), @"(?<=[。！？!?\n])"))
        {
            var s = raw.Trim();
            if (s.Length == 0) continue;
            var toks = new List<RToken>();
            int i = 0;
            while (i < s.Length)
            {
                var (len, e) = JDict.Longest(s, i);
                var surf = s.Substring(i, len);
                bool kanji = surf.Any(c => c >= '一' && c <= '鿿');
                toks.Add(new RToken(surf, kanji && e != null ? e.Kana : "", surf, e == null ? "記号" : "名詞"));
                i += len;
            }
            sents.Add(new RSentence(s, "", toks.ToArray()));
        }
        return new RText("Свой текст", "—", sents.ToArray(), Array.Empty<(string, string, string[])>());
    }
}

public sealed class TextPage : UserControl, IPage, IResumable
{
    readonly RText _t;
    public string ResumeKey => $"text:{_t.Title}";
    public string ResumeTitle => $"Чтение · {_t.Title}";
    public string Title => _t.Title;
    bool _furi = true, _ru, _romaji, _listen;
    readonly HashSet<RSentence> _revealed = new();
    readonly List<(RSentence s, Border card)> _cards = new();
    readonly ContentControl _popup = new();

    public TextPage(RText t, bool listen = false) { _t = t; _listen = listen; }

    public void OnShown() => Build();

    void Build()
    {
        var root = new StackPanel { MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Center };
        root.Children.Add(U.T(_t.Book != null ? $"{_t.Book} · {_t.Level}" : _t.Level, "Overline"));
        root.Children.Add(U.T(_t.Title, "H1").M(0, 4, 0, 14));
        var bar = new WrapPanel();
        bar.Children.Add(Toggle("Фуригана", _furi, v => { _furi = v; Build(); }));
        if (_furi) bar.Children.Add(Toggle("…только незнакомые", ProgressStore.P.FuriUnknownOnly, v => { ProgressStore.P.FuriUnknownOnly = v; ProgressStore.Notify(); Build(); }));
        bar.Children.Add(Toggle("Перевод", _ru, v => { _ru = v; Build(); }));
        bar.Children.Add(Toggle("Латиница", _romaji, v => { _romaji = v; Build(); }));
        bar.Children.Add(Toggle("На слух", _listen, v => { _listen = v; _revealed.Clear(); Build(); }));
        bar.Children.Add(U.Btn(_listen ? "Слушать главу" : "Слушать всё", PlayAll, _listen ? "Primary" : "", "").M(0, 0, 8, 8));
        root.Children.Add(bar);

        if (_listen)
            root.Children.Add(Art.Say("Режим аудирования: текст спрятан. Слушай главу целиком, потом отвечай на вопросы. Нажми на строку, чтобы подсмотреть.", Mood.Think, 90).M(0, 0, 0, 12));
        _cards.Clear();
        // первые предложения — сразу, остальная глава достраивается после первого кадра (длинная глава — это сотни слов-кнопок)
        var later = new List<Func<Control>>();
        foreach (var (s, i) in _t.Sentences.Select((s, i) => (s, i)))
            if (i < 8) root.Children.Add(SentenceView(s)); else later.Add(() => SentenceView(s));
        later.Add(() => _popup.Detach().M(0, 6, 0, 0)); // при перестройке (переключатели) старая страница ещё держит подсказку
        if (_t.Questions.Length > 0) later.Add(Questions);
        U.Deferred(root, later, 3);
        Content = U.Page(new Grid { Children = { root } });
    }

    // кандзи, которые уже в изучении (карточка повторялась хотя бы раз)
    static (int ver, HashSet<char> set) _knownKanji = (-1, new());
    static bool AllKanjiKnown(string surface)
    {
        if (_knownKanji.ver != ProgressStore.Version)
            _knownKanji = (ProgressStore.Version, Db.Kanji.Where(k => ProgressStore.Card(k.Id)?.Reps > 0).Select(k => k.Char[0]).ToHashSet());
        var ks = surface.Where(Reading.IsKanji).ToList();
        return ks.Count > 0 && ks.All(_knownKanji.set.Contains);
    }

    static ToggleButton Toggle(string text, bool on, Action<bool> set)
    {
        var tb = new ToggleButton { Classes = { "Chip" }, Content = text, IsChecked = on };
        tb.Checked += (_, _) => set(true); tb.Unchecked += (_, _) => set(false);
        return tb;
    }

    Control SentenceView(RSentence s)
    {
        if (_listen && !_revealed.Contains(s))
        {
            // текст скрыт: полоска-заглушка, по нажатию — подсмотреть
            int n = Array.IndexOf(_t.Sentences, s) + 1;
            var hidden = U.Cols("auto", "*", "auto");
            hidden.Children.Add(U.T(n.ToString(), "H3", 14, U.B("Muted")).CenterV());
            hidden.Children.Add(new Border { Height = 10, CornerRadius = new CornerRadius(5), Background = U.B("Surface3"), Margin = new Thickness(14, 0, 14, 0), MaxWidth = Math.Min(560, 40 + s.Jp.Length * 14), HorizontalAlignment = HorizontalAlignment.Stretch }.CenterV().Col(1));
            hidden.Children.Add(U.Row(2, U.SpeakBtn(() => s.Jp, true), U.SpeakBtn(() => s.Jp)).Col(2));
            var hc = U.Card(hidden, new Thickness(18, 10, 12, 10)).M(0, 0, 0, 8);
            hc.Cursor = Cursors.Hand;
            hc.PointerReleased += (_, e) => { if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) == null) { _revealed.Add(s); Build(); } };
            _cards.Add((s, hc));
            return hc;
        }
        var wrap = new WrapPanel();
        // ресурсы — один раз на предложение, а не на каждый токен (их в главе сотни)
        var jp = U.R<FontFamily>("JpFont");
        IBrush accent = U.B("Accent"), text = U.B("Text");
        bool unknownOnly = ProgressStore.P.FuriUnknownOnly;
        foreach (var t in s.Tokens)
        {
            var tok = t;
            var stack = new StackPanel { Margin = new Thickness(0, 0, 1, 4), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            bool furi = _furi && t.Reading.Length > 0 && !(unknownOnly && AllKanjiKnown(t.Surface));
            // без фуриганы — не пустая надпись, а отступ той же высоты (меньше элементов: в главе сотни токенов)
            if (furi) stack.Children.Add(new TextBlock { Text = t.Reading, FontSize = 12, Height = FuriH, Foreground = accent, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = jp });
            var surf = new TextBlock { Text = t.Surface, FontSize = 24, FontFamily = jp, Foreground = text, HorizontalAlignment = HorizontalAlignment.Center, Margin = furi ? default : new Thickness(0, FuriH, 0, 0) };
            stack.Children.Add(surf);
            if (t.Pos != "補助記号" && t.Pos != "記号")
            {
                stack.PointerEntered += (_, _) => surf.Foreground = accent;
                stack.PointerExited += (_, _) => surf.Foreground = text;
                stack.OnClick(() => ShowWord(tok));
            }
            wrap.Children.Add(stack);
        }
        var g = U.Cols("*", "auto");
        var col = U.Stack(wrap);
        if (_romaji) col.Children.Add(U.T(Romaji(s), "Muted", 13));
        if (_ru && s.Ru.Length > 0) col.Children.Add(U.T(s.Ru, "Body", 14, U.B("Muted")).M(0, 4, 0, 0));
        g.Children.Add(col);
        var learn = U.Btn("", () => LearnSentence(s), "Ghost", "").Tip("Учить это предложение — попадёт в повторения с переводом и озвучкой");
        g.Children.Add(U.Row(2, learn, U.SpeakBtn(() => s.Jp, true), U.SpeakBtn(() => s.Jp)).Col(1));
        var card = U.Card(g, new Thickness(18, 12, 12, 12)).M(0, 0, 0, 8);
        _cards.Add((s, card));
        return card;
    }

    /// <summary>Предложение — в свои карточки (8.2): повторяется по FSRS вместе со словами, с переводом и озвучкой.</summary>
    static void LearnSentence(RSentence s)
    {
        var kana = string.Concat(s.Tokens.Select(t => t.Reading.Length > 0 ? t.Reading : t.Surface)).Trim();
        var list = ProgressStore.P.CustomWords;
        if (list.Any(c => c[0] == s.Jp)) { Shell.Toast("Это предложение уже в карточках", "済"); return; }
        list.Add(new[] { s.Jp, kana.Length > 0 ? kana : s.Jp, s.Ru.Length > 0 ? s.Ru : "(предложение из читалки)" });
        ProgressStore.Notify();
        Shell.Toast("Предложение добавлено в карточки", "+");
    }

    const double FuriH = 16;

    static string Romaji(RSentence s) => Kana.ToRomaji(string.Join(" ", s.Tokens.Where(t => t.Pos != "補助記号").Select(t =>
        t.Pos == "助詞" && t.Surface is "は" or "へ" or "を" ? (t.Surface == "は" ? "わ" : t.Surface == "へ" ? "え" : "お")
        : t.Reading.Length > 0 ? t.Reading : Kana.ToHiragana(t.Surface))));

    async void ShowWord(RToken t)
    {
        await JDict.LoadAsync();
        var e = JDict.Lookup(t.Lemma) ?? JDict.Lookup(t.Surface);
        string reading = e?.Kana ?? (t.Reading.Length > 0 ? t.Reading : t.Surface);
        Speech.Say(t.Lemma.Any(c => c >= '一' && c <= '鿿') ? reading : t.Lemma);
        var g = U.Cols("*", "auto");
        g.Children.Add(U.Stack(
            U.Row(10, U.Jp(t.Lemma, 26), U.Jp(reading, 15, U.B("Accent")).CenterV()),
            U.T(Kana.ToRomaji(reading), "Muted", 13),
            Pitch.Pattern(e?.Jp ?? t.Lemma, reading) is { } pat ? new Controls.AccentView(pat) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) } : new Border(),
            U.T(e != null ? e.Pretty : "Нет в словаре (служебное слово или имя)", "Body", 14).M(0, 6, 0, 0)));
        var acts = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        acts.Children.Add(U.SpeakBtn(() => reading));
        if (e != null)
        {
            var add = new Button { Classes = { "IconButton" }, Content = JDict.InStudy(e) ? "" : ""}.Tip("В мои карточки");
            add.Click += (_, _) => { if (JDict.AddToStudy(e)) { add.Content = ""; Shell.Toast($"«{e.Jp}» добавлено в «Мои слова»", "追加"); } };
            acts.Children.Add(add);
            var more = new Button { Classes = { "IconButton" }, Content = "" }.Tip("Подробнее: иероглифы, примеры, похожие слова");
            more.Click += (_, _) => Shell.Open(new WordPage(e));
            acts.Children.Add(more);
        }
        g.Children.Add(acts.Col(1));
        var card = U.Card(g);
        card.BorderBrush = U.B("AccentSoft");
        _popup.Content = card;
        Anim.FadeUp(card, 0, 8, 220);
        card.BringIntoView();
    }

    async void PlayAll()
    {
        // текущее предложение подсвечивается рамкой — легко следить за текстом
        foreach (var s in _t.Sentences)
        {
            var card = _cards.FirstOrDefault(c => c.s == s).card;
            var normal = card?.BorderBrush;
            if (card != null) { card.BorderBrush = U.B("Accent"); card.BringIntoView(); }
            bool ok = await Speech.SayAsync(s.Jp);
            if (card != null) card.BorderBrush = normal;
            if (!ok) break;
            await Task.Delay(_listen ? 600 : 250);
        }
    }

    Control Questions()
    {
        var sp = new StackPanel();
        sp.Children.Add(U.T("Вопросы на понимание", "H2").M(0, 0, 0, 10));
        int correct = 0, answered = 0;
        foreach (var (q, a, opts) in _t.Questions)
        {
            sp.Children.Add(U.T(q, "H3", 15).M(0, 8, 0, 6));
            var wrap = new WrapPanel();
            bool locked = false;
            foreach (var o in opts.Shuffled())
            {
                var b = new Button { Content = o, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 8, 14, 8) };
                var opt = o;
                b.Click += (_, _) =>
                {
                    if (locked) return; locked = true; answered++;
                    bool ok = opt == a;
                    b.Background = U.B(ok ? "GoodSoft" : "BadSoft"); b.BorderBrush = U.B(ok ? "Good" : "Bad");
                    if (ok) { correct++; Sfx.Ok(); } else Sfx.Bad();
                    if (answered == _t.Questions.Length)
                    {
                        ProgressStore.P.Flags.Add("read:" + _t.Title);
                        ProgressStore.AddXp(5 + correct * 3);
                        Shell.Toast($"Текст прочитан: {correct}/{answered} верно", "読");
                        if (_t.Book != null && ReaderPage.Texts.SkipWhile(x => x != _t).Skip(1).FirstOrDefault() is { } nx && nx.Book == _t.Book)
                        {
                            var nb = U.Btn("Следующая глава: " + nx.Title, () => { Shell.Back(); Shell.Open(new TextPage(nx)); }, "Primary", "");
                            nb.HorizontalAlignment = HorizontalAlignment.Left; nb.Margin = new Thickness(0, 14, 0, 0);
                            sp.Children.Add(nb);
                        }
                    }
                };
                wrap.Children.Add(b);
            }
            sp.Children.Add(wrap);
        }
        return U.Card(sp).M(0, 12, 0, 0);
    }
}

/// <summary>Оглавление книги.</summary>
public sealed class BookPage : UserControl, IPage
{
    readonly string _book;
    readonly RText[] _chapters;
    public string Title => _book;
    public BookPage(string book, RText[] chapters) { _book = book; _chapters = chapters; }

    public void OnShown()
    {
        var root = new StackPanel { MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Center };
        root.Children.Add(U.Header("本", _book, $"{_chapters.Length} глав · нажми на главу, чтобы читать"));
        bool prevRead = true;
        for (int i = 0; i < _chapters.Length; i++)
        {
            var t = _chapters[i];
            bool read = ProgressStore.P.Flags.Contains("read:" + t.Title);
            bool next = !read && prevRead;
            var g = U.Cols("auto", "*", "auto");
            g.Children.Add(new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Background = U.B(read ? "GoodSoft" : next ? "AccentSoft" : "Surface2"),
                Child = U.T(read ? "✓" : (i + 1).ToString(), "H3", 15, U.B(read ? "Good" : next ? "Accent" : "Muted")).Center().CenterV(),
            });
            g.Children.Add(U.Stack(U.T(t.Title, "H3", 15), U.Jp(t.Sentences[0].Jp, 13.5, U.B("Muted")).Also(U.Preview))
                .CenterV().M(14, 0, 12, 0).Col(1));
            var text = t;
            g.Children.Add(U.Btn(read ? "Перечитать" : "Читать", () => Shell.Open(new TextPage(text)), next ? "Primary" : "", "").CenterV().Col(2));
            var card = new Border { Classes = { "Card" }, Child = g, Padding = new Thickness(14, 12), Margin = new Thickness(0, 0, 0, 8) };
            if (next) card.BorderBrush = U.B("AccentSoft");
            root.Children.Add(card);
            prevRead = read;
        }
        Anim.Stagger(root, 40, 0, 14);
        Content = U.Page(root);
    }
}
