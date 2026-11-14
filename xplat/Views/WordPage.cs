using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>
/// Карточка слова из словаря: значения, питч-акцент, разбор иероглифов с порядком черт,
/// примеры из всех материалов приложения и слова с теми же иероглифами.
/// </summary>
public sealed class WordPage : UserControl, IPage
{
    readonly DictEntry _e;
    public string Title => _e.Jp;
    public WordPage(DictEntry e) { _e = e; Remember(e.Jp); }

    /// <summary>
    /// Формы слова: глагол (перевод похож на глагол: «…ть», «…ться») — 11 форм спряжения,
    /// い-прилагательное (перевод «…ый/…ий») — отрицание, прошедшее, て, наречие, условная.
    /// </summary>
    /// <summary>Чтение формы: у основы с кандзи берём чтение слова, окончание — как есть (来る — отдельно).</summary>
    public static string FormKana(string jp, string kana, string form)
    {
        if (jp == "来る" && Conj.All("くる", "くる") is { } k && Conj.All(jp, kana) is { } f)
            foreach (var (key, v) in f) if (v == form) return k[key];
        int p = 0;
        while (p < jp.Length && p < form.Length && jp[p] == form[p]) p++;
        int tail = jp.Length - p;
        return tail <= kana.Length ? kana[..(kana.Length - tail)] + form[p..] : form;
    }

    public static List<(string name, string form)> Forms(string jp, string kana, string ru)
    {
        var res = new List<(string, string)>();
        var first = ru.Split(',', ';', '(')[0].Trim().Split(' ').LastOrDefault() ?? "";
        bool verbRu = System.Text.RegularExpressions.Regex.IsMatch(ru, @"[а-яё]+(ть|ться|чь|чься|ти|тись)(\b|\s|,|;|$)");
        if (verbRu && Conj.All(jp, kana) is { } all)
        {
            res.Add(("словарная", jp));
            foreach (var (key, name) in Conj.Forms) if (all.TryGetValue(key, out var v)) res.Add((name, v));
            return res;
        }
        bool adjRu = System.Text.RegularExpressions.Regex.IsMatch(first, @"(ый|ий|ой|ая|ое|ые)$");
        if (adjRu && jp.EndsWith('い') && jp.Length >= 2 && !jp.EndsWith("きれい") && !jp.EndsWith("嫌い") && !jp.EndsWith("綺麗"))
        {
            var st = jp is "いい" or "良い" ? (jp == "いい" ? "よ" : "良") : jp[..^1];
            res.Add(("отрицание", st + "くない"));
            res.Add(("прошедшее", st + "かった"));
            res.Add(("прош. отрицание", st + "くなかった"));
            res.Add(("て-форма", st + "くて"));
            res.Add(("наречие", st + "く"));
            res.Add(("если", st + "ければ"));
            res.Add(("вежливо", jp + "です"));
            res.Add(("вежл. прошедшее", st + "かったです"));
        }
        return res;
    }

    /// <summary>История просмотренных слов (для пустого экрана словаря).</summary>
    static void Remember(string jp)
    {
        var h = ProgressStore.P.DictHistory;
        h.Remove(jp); h.Insert(0, jp);
        if (h.Count > 16) h.RemoveRange(16, h.Count - 16);
        ProgressStore.Notify();
    }

    // Все предложения приложения — для поиска примеров
    static readonly Lazy<(string jp, string ru)[]> Corpus = new(() =>
        Db.Words.Where(w => w.ExJp.Length > 0).Select(w => (w.ExJp, w.ExRu))
            .Concat(ReaderPage.Texts.SelectMany(t => t.Sentences.Where(s => s.Ru.Length > 0).Select(s => (s.Jp, s.Ru))))
            .Concat(Db.SpeechLessons.SelectMany(l => l.Items.Select(i => (i.Jp, i.Ru))))
            .Concat(Db.Dialogs.SelectMany(d => d.Lines.Select(l => (l.Jp, l.Ru))))
            .Concat(Db.Grammar.SelectMany(g => g.Points.SelectMany(p => p.Examples.Select(x => (x.Jp, x.Ru)))))
            .Concat(Db.Kanji.SelectMany(k => k.Examples.Select(x => (x.Jp, x.Ru))))
            .Where(x => x.Item1.Length > 0).DistinctBy(x => x.Item1).ToArray());

    public static List<(string jp, string ru)> Examples(DictEntry e, int max = 6)
    {
        var forms = new List<string>();
        if (e.Kanji.Length > 0) forms.AddRange(e.Kanji.Split('、', StringSplitOptions.RemoveEmptyEntries));
        else if (e.Kana.Length >= 2) forms.Add(e.Kana);
        // для глаголов и прилагательных ищем и по основе: 食べる → 食べ
        foreach (var f in forms.ToList())
            if (f.Length >= 2 && "るうくすつむぶぐぬい".Contains(f[^1])) forms.Add(f[..^1]);
        return Corpus.Value.Where(x => forms.Any(f => x.jp.Contains(f, StringComparison.Ordinal))).OrderBy(x => x.jp.Length).Take(max).ToList();
    }

    public void OnShown()
    {
        var root = new StackPanel { MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Center };

        // ─────────── Шапка ───────────
        var head = U.Cols("*", "auto");
        var title = new StackPanel();
        title.Children.Add(U.Jp(_e.Jp, _e.Jp.Length > 6 ? 40 : 60, null, true));
        if (_e.Kanji.Length > 0) title.Children.Add(U.Jp(_e.Kana, 22, U.B("Accent")).M(0, 4, 0, 0));
        title.Children.Add(U.T(_e.Romaji, "Muted", 14));
        if (Pitch.Pattern(_e.Jp, _e.Kana) is { } pat)
            title.Children.Add(new AccentView(pat) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) }
                .Tip(Pitch.Describe(Pitch.Accent(_e.Jp, _e.Kana) ?? 0, pat.Count - 1)));
        var tags = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (_e.Common) tags.Children.Add(U.Pill("частое слово", U.B("GoodSoft"), U.B("Good")).M(0, 0, 6, 0));
        var lvl = Db.Words.FirstOrDefault(w => w.Jp == _e.Jp && w.Kana == _e.Kana)?.Level;
        if (lvl != null) tags.Children.Add(U.Pill(lvl == "1000" ? "1000 частых" : lvl == "Живой" ? "Живой язык" : "JLPT " + lvl).M(0, 0, 6, 0));
        if (_e.Kanji.Contains('、')) tags.Children.Add(U.Pill("варианты: " + _e.Kanji.Replace("、", ", "), U.B("Surface2"), U.B("Muted")));
        title.Children.Add(tags);
        head.Children.Add(title);
        var add = U.Btn(JDict.InStudy(_e) ? "В карточках ✓" : "Учить это слово", () => { }, JDict.InStudy(_e) ? "" : "Primary", "");
        add.Click += (_, _) =>
        {
            if (JDict.AddToStudy(_e)) { Shell.Toast($"«{_e.Jp}» добавлено в «Мои слова»", "追加"); add.IsEnabled = false; }
        };
        head.Children.Add(U.Stack(U.Row(6, U.SpeakBtn(() => _e.Kana, true), U.SpeakBtn(() => _e.Kana)).Also(r => r.HorizontalAlignment = HorizontalAlignment.Right), add.M(0, 12, 0, 0)).Col(1));
        root.Children.Add(U.Card(head, new Thickness(28, 22, 28, 22)));

        // ─────────── Значения ───────────
        // в JMdict значения нумеруются «1) … 2) …», иначе разделены «;»
        var pretty = _e.Pretty;
        var senses = (System.Text.RegularExpressions.Regex.IsMatch(pretty, @"(^|\s)1\)")
                ? System.Text.RegularExpressions.Regex.Split(pretty, @"(?:^|[\s,;]+)\d+\)\s*")
                : pretty.Split(';')).Select(x => x.Trim(' ', ',', ';')).Where(x => x.Length > 0).ToArray();
        var ms = new StackPanel();
        ms.Children.Add(U.T("Значения", "H2", 19).M(0, 0, 0, 8));
        for (int i = 0; i < senses.Length; i++)
            ms.Children.Add(U.Row(10, new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = U.B("AccentSoft"), VerticalAlignment = VerticalAlignment.Top,
                Child = U.T((i + 1).ToString(), "H3", 12, U.B("Accent")).Center().CenterV(),
            }, U.T(senses[i], "Body", 15).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 820; })).M(0, 0, 0, 8));
        root.Children.Add(U.Card(ms).M(0, 14, 0, 0));

        // ─────────── Формы слова: спряжение глагола или прилагательного ───────────
        if (Forms(_e.Jp, _e.Kana, senses.FirstOrDefault() ?? "") is { Count: > 0 } forms)
        {
            var fp = new StackPanel();
            fp.Children.Add(U.T(forms[0].name.StartsWith("отриц") ? "Формы прилагательного" : "Спряжение", "H2", 19).M(0, 0, 0, 4));
            fp.Children.Add(U.T("Нажми на форму, чтобы послушать.", "Muted", 12.5).M(0, 0, 0, 10));
            var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = U.Columns(4, 2) };
            foreach (var (name, form) in forms)
            {
                var f = form;
                var b = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 8, 8), Padding = Shell.Compact ? new Thickness(10, 8) : new Thickness(12, 8),
                    Content = U.Stack(U.T(name, "Muted", 11.5), U.Jp(f, Shell.Compact ? 15.5 : 18).Also(t => t.TextWrapping = TextWrapping.NoWrap), U.T(Kana.ToRomaji(FormKana(_e.Jp, _e.Kana, f)), "Muted", 11)),
                };
                b.Click += (_, _) => Speech.Say(f);
                grid.Children.Add(b);
            }
            fp.Children.Add(grid);
            root.Children.Add(U.Card(fp).M(0, 14, 0, 0));
        }

        // ─────────── Личная заметка ───────────
        var note = new TextBox
        {
            Text = ProgressStore.P.Notes.GetValueOrDefault(_e.Id, ""), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64,
            Watermark = "Своя подсказка, ассоциация или пример — увидишь её здесь и на карточке",
        };
        // сохраняем, когда поле теряет фокус или страница закрывается (не на каждую букву)
        void SaveNote()
        {
            var t = (note.Text ?? "").Trim();
            var old = ProgressStore.P.Notes.GetValueOrDefault(_e.Id, "");
            if (t == old) return;
            if (t.Length == 0) ProgressStore.P.Notes.Remove(_e.Id); else ProgressStore.P.Notes[_e.Id] = t;
            ProgressStore.Notify();
        }
        note.LostFocus += (_, _) => SaveNote();
        note.DetachedFromVisualTree += (_, _) => SaveNote();
        root.Children.Add(U.Card(U.Stack(U.Row(8, Art.Icon("note", 26), U.T("Моя заметка", "H2", 19).CenterV()).M(0, 0, 0, 10), note)).M(0, 14, 0, 0));

        // ─────────── Иероглифы ───────────
        var chars = _e.Jp.Where(c => c >= '一' && c <= '鿿').Distinct().ToList();
        if (chars.Count > 0)
        {
            var kp = new StackPanel();
            kp.Children.Add(U.T("Иероглифы в слове", "H2", 19).M(0, 0, 0, 10));
            var wrap = new WrapPanel();
            foreach (var ch in chars) wrap.Children.Add(KanjiTile(ch));
            kp.Children.Add(wrap);
            root.Children.Add(U.Card(kp).M(0, 14, 0, 0));
        }

        // ─────────── Примеры ───────────
        var ex = Examples(_e);
        var ep = new StackPanel();
        ep.Children.Add(U.T("Примеры", "H2", 19).M(0, 0, 0, 8));
        if (ex.Count == 0) ep.Children.Add(U.T("В материалах приложения пока нет примеров с этим словом.", "Muted"));
        foreach (var (jp, ru) in ex)
        {
            var g = U.Cols("*", "auto");
            g.Children.Add(U.Stack(Highlight(jp), U.T(ru, "Muted", 13).Also(t => t.TextWrapping = TextWrapping.Wrap)));
            g.Children.Add(U.SpeakBtnSmall(jp).CenterV().Col(1));
            ep.Children.Add(new Border { Background = U.B("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 8, 8, 8), Margin = new Thickness(0, 0, 0, 6), Child = g });
        }
        root.Children.Add(U.Card(ep).M(0, 14, 0, 0));

        // ─────────── Слова с теми же иероглифами ───────────
        if (chars.Count > 0)
        {
            var rel = chars.SelectMany(c => JDict.WithKanji(c, 8)).Where(x => x.Jp != _e.Jp).DistinctBy(x => x.Jp).Take(14).ToList();
            if (rel.Count > 0)
            {
                var rp = new StackPanel();
                rp.Children.Add(U.T("Слова с теми же иероглифами", "H2", 19).M(0, 0, 0, 10));
                var wrap = new WrapPanel();
                foreach (var r in rel)
                {
                    var b = new Button { Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 6),
                        Content = U.Stack(U.Jp(r.Jp, 18), U.T(r.Ru.Split(';')[0].Trim() is var s && s.Length > 22 ? s[..22] + "…" : s, "Muted", 11.5)) };
                    var rr = r;
                    b.Click += (_, _) => Shell.Open(new WordPage(rr));
                    wrap.Children.Add(b);
                }
                rp.Children.Add(wrap);
                root.Children.Add(U.Card(rp).M(0, 14, 0, 0));
            }
        }
        Content = U.Page(root);
        Anim.Stagger(root, 40, 0, 8);
    }

    /// <summary>Пример с подсвеченным словом.</summary>
    Control Highlight(string jp)
    {
        var tb = new TextBlock { FontFamily = U.R<FontFamily>("JpFont"), FontSize = 18, TextWrapping = TextWrapping.Wrap };
        string form = _e.Kanji.Length > 0 ? _e.Kanji.Split('、')[0] : _e.Kana;
        if (form.Length >= 2 && !jp.Contains(form) && "るうくすつむぶぐぬい".Contains(form[^1])) form = form[..^1];
        int i = jp.IndexOf(form, StringComparison.Ordinal);
        if (i < 0) { tb.Text = jp; return tb; }
        tb.Inlines!.Add(new Avalonia.Controls.Documents.Run(jp[..i]));
        tb.Inlines.Add(new Avalonia.Controls.Documents.Run(form) { Foreground = U.B("Accent"), FontWeight = FontWeight.SemiBold });
        tb.Inlines.Add(new Avalonia.Controls.Documents.Run(jp[(i + form.Length)..]));
        return tb;
    }

    static Control KanjiTile(char ch)
    {
        var k = Db.Kanji.FirstOrDefault(x => x.Char == ch.ToString());
        var sv = new StrokeView(ch.ToString(), 110);
        if (sv.HasData) sv.Loaded += (_, _) => sv.Play(1.4);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), MaxWidth = 220 };
        if (k != null)
        {
            info.Children.Add(U.T(k.Ru, "H3", 15).Also(t => t.TextWrapping = TextWrapping.Wrap));
            info.Children.Add(U.T("он: " + k.On, "Muted", 12.5).M(0, 4, 0, 0));
            info.Children.Add(U.T("кун: " + (k.Kun.Length > 0 ? k.Kun : "—"), "Muted", 12.5));
            info.Children.Add(U.Row(6, U.Pill(k.Level), U.T($"{k.Strokes} черт", "Muted", 12).CenterV()).M(0, 6, 0, 0));
            if (Mnemonics.Hint(k.Char, k.Ru) is { Length: > 0 } h) info.Children.Add(U.T("💡 " + h, "Body", 12).Also(t => t.TextWrapping = TextWrapping.Wrap).M(0, 6, 0, 0));
        }
        else if (JDict.Lookup(ch.ToString()) is { } d)
        {
            info.Children.Add(U.T(d.Ru.Split(';')[0], "H3", 15).Also(t => t.TextWrapping = TextWrapping.Wrap));
            info.Children.Add(U.T("чтение: " + d.Kana, "Muted", 12.5).M(0, 4, 0, 0));
        }
        else info.Children.Add(U.T("Вне программы N5–N1", "Muted"));
        var row = U.Row(0, sv.HasData ? sv : U.Jp(ch.ToString(), 70, null, true), info);
        return new Border { Background = U.B("Surface2"), CornerRadius = new CornerRadius(14), Padding = new Thickness(10), Margin = new Thickness(0, 0, 10, 10), Child = row };
    }
}
