using System.Text.Json;
using Avalonia;
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
using Matsubakira.Services;

namespace Matsubakira.Views;

public sealed record RToken(string Surface, string Reading, string Lemma, string Pos);
public sealed record RSentence(string Jp, string Ru, RToken[] Tokens);
public sealed record RText(string Title, string Level, RSentence[] Sentences, (string q, string a, string[] opts)[] Questions);

/// <summary>Читалка: тексты с фуриганой, озвучкой, переводом и словарём по клику; можно вставить свой текст.</summary>
public sealed class ReaderPage : UserControl, IPage
{
    public string Title => "Чтение";
    static readonly Lazy<RText[]> _texts = new(Load);

    static RText[] Load()
    {
        using var s = typeof(ReaderPage).Assembly.GetManifestResourceStream("texts.json")!;
        using var doc = JsonDocument.Parse(s);
        return doc.RootElement.EnumerateArray().Select(t => new RText(
            t.GetProperty("title").GetString()!, t.GetProperty("level").GetString()!,
            t.GetProperty("sentences").EnumerateArray().Select(x => new RSentence(x.GetProperty("jp").GetString()!, x.GetProperty("ru").GetString()!,
                x.GetProperty("tokens").EnumerateArray().Select(k => new RToken(k[0].GetString()!, k[1].GetString()!, k[2].GetString()!, k[3].GetString()!)).ToArray())).ToArray(),
            t.GetProperty("questions").EnumerateArray().Select(q => (q[0].GetString()!, q[1].GetString()!, q[2].GetString()!.Split('|'))).ToArray())).ToArray();
    }

    public void OnShown()
    {
        var root = new StackPanel();
        root.Children.Add(U.Header("読", "Чтение", "Тексты с фуриганой и озвучкой. Нажми на любое слово — перевод и «+» в карточки."));
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(-6, 0, -6, 0) };
        foreach (var t in _texts.Value)
        {
            bool done = ProgressStore.P.Flags.Contains("read:" + t.Title);
            var sp = U.Stack(Emoji.Img(Emoji.For(t.Title, "open_book"), 44).Also(i => { i.HorizontalAlignment = HorizontalAlignment.Left; i.Margin = new Thickness(0, 0, 0, 8); }), U.Row(8, U.Pill(t.Level), done ? U.Pill("✓ прочитано", U.B("GoodSoft"), U.B("Good")) : new Border()),
                U.T(t.Title, "H3", 15).M(0, 10, 0, 4), U.Jp(t.Sentences[0].Jp, 14, U.B("Muted")).Also(x => x.TextTrimming = TextTrimming.CharacterEllipsis).Also(x => x.TextWrapping = TextWrapping.NoWrap));
            var card = new Border { Classes = { "Card" }, Child = sp, Margin = new Thickness(6), Cursor = Cursors.Hand };
            HomePage.Hover(card);
            var text = t;
            card.PointerReleased += (_, _) => Shell.OpenFrom(card, new TextPage(text));
            grid.Children.Add(card);
        }
        root.Children.Add(grid);
        Anim.Stagger(grid, 35, 60);

        // Свой текст
        var box = new TextBox { FontSize = 16, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, FontFamily = U.R<FontFamily>("JpFont") };
        var own = U.Stack(U.T("Свой текст", "H3", 15), U.T("Вставь любой японский текст — он будет разобран по словарю (69 000 слов).", "Muted").M(0, 4, 0, 10), box,
            U.Btn("Читать", async () =>
            {
                if (box.Text.Trim().Length == 0) return;
                await JDict.LoadAsync();
                Shell.Open(new TextPage(ParseOwn(box.Text)));
            }, "Primary", "").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 10, 0, 0); }));
        root.Children.Add(U.Card(own).M(0, 18, 0, 0));
        Content = U.Page(root);
    }

    /// <summary>Разбор произвольного текста: предложения по 。！？, слова — самым длинным совпадением со словарём.</summary>
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

public sealed class TextPage : UserControl, IPage
{
    readonly RText _t;
    public string Title => _t.Title;
    bool _furi = true, _ru, _romaji;
    readonly ContentControl _popup = new();

    public TextPage(RText t) { _t = t; }

    public void OnShown() => Build();

    void Build()
    {
        var root = new StackPanel { MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Center };
        root.Children.Add(U.T(_t.Level, "Overline"));
        root.Children.Add(U.T(_t.Title, "H1").M(0, 4, 0, 14));
        var bar = new WrapPanel();
        bar.Children.Add(Toggle("Фуригана", _furi, v => { _furi = v; Build(); }));
        bar.Children.Add(Toggle("Перевод", _ru, v => { _ru = v; Build(); }));
        bar.Children.Add(Toggle("Латиница", _romaji, v => { _romaji = v; Build(); }));
        bar.Children.Add(U.Btn("Слушать всё", PlayAll, "", "").M(0, 0, 8, 8));
        root.Children.Add(bar);

        foreach (var s in _t.Sentences) root.Children.Add(SentenceView(s));
        root.Children.Add(_popup.M(0, 6, 0, 0));
        if (_t.Questions.Length > 0) root.Children.Add(Questions());
        Content = U.Page(new Grid { Children = { root } });
    }

    static ToggleButton Toggle(string text, bool on, Action<bool> set)
    {
        var tb = new ToggleButton { Classes = { "Chip" }, Content = text, IsChecked = on };
        tb.Checked += (_, _) => set(true); tb.Unchecked += (_, _) => set(false);
        return tb;
    }

    Control SentenceView(RSentence s)
    {
        var wrap = new WrapPanel();
        foreach (var t in s.Tokens)
        {
            var tok = t;
            var stack = new StackPanel { Margin = new Thickness(0, 0, 1, 4), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            stack.Children.Add(new TextBlock { Text = _furi && t.Reading.Length > 0 ? t.Reading : " ", FontSize = 11, Foreground = U.B("Accent"), HorizontalAlignment = HorizontalAlignment.Center, FontFamily = U.R<FontFamily>("JpFont") });
            var surf = new TextBlock { Text = t.Surface, FontSize = 24, FontFamily = U.R<FontFamily>("JpFont"), Foreground = U.B("Text"), HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(surf);
            if (t.Pos != "補助記号" && t.Pos != "記号")
            {
                stack.PointerEntered += (_, _) => surf.Foreground = U.B("Accent");
                stack.PointerExited += (_, _) => surf.Foreground = U.B("Text");
                stack.PointerReleased += (_, _) => ShowWord(tok);
            }
            wrap.Children.Add(stack);
        }
        var g = U.Cols("*", "auto");
        var col = U.Stack(wrap);
        if (_romaji) col.Children.Add(U.T(Romaji(s), "Muted", 13));
        if (_ru && s.Ru.Length > 0) col.Children.Add(U.T(s.Ru, "Body", 14, U.B("Muted")).M(0, 4, 0, 0));
        g.Children.Add(col);
        g.Children.Add(U.Row(2, U.SpeakBtn(() => s.Jp, true), U.SpeakBtn(() => s.Jp)).Col(1));
        return U.Card(g, new Thickness(18, 12, 12, 12)).M(0, 0, 0, 8);
    }

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
            U.T(e != null ? System.Text.RegularExpressions.Regex.Replace(e.Ru.TrimStart(':', ' '), @"\{[^}]*\}", "") : "Нет в словаре (служебное слово или имя)", "Body", 14).M(0, 6, 0, 0)));
        var acts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        acts.Children.Add(U.SpeakBtn(() => reading));
        if (e != null)
        {
            var add = new Button { Classes = { "IconButton" }, Content = JDict.InStudy(e) ? "" : ""}.Tip("В мои карточки");
            add.Click += (_, _) => { if (JDict.AddToStudy(e)) { add.Content = ""; Shell.Toast($"«{e.Jp}» добавлено в «Мои слова»", "追加"); } };
            acts.Children.Add(add);
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
        foreach (var s in _t.Sentences)
            if (!await Speech.SayAsync(s.Jp)) break;
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
            foreach (var o in opts.Shuffle())
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
                    }
                };
                wrap.Children.Add(b);
            }
            sp.Children.Add(wrap);
        }
        return U.Card(sp).M(0, 12, 0, 0);
    }
}
