using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

public sealed record JArticle(string Id, string Title, string Jp, string Emoji, string Cat, string Lead,
    (string h, string text)[] Sections, (string jp, string kana, string ru)[] Words, string[] Facts, (string q, string a, string[] opts)[] Quiz);

/// <summary>Раздел «Япония»: статьи о культуре, обычаях и жизни — с японскими словами, фактами и мини-тестом.</summary>
public sealed class JapanPage : UserControl, IPage
{
    public string Title => "Япония";
    static readonly Lazy<JArticle[]> _all = new(Load);
    public static JArticle[] All => _all.Value;

    static JArticle[] Load()
    {
        using var s = typeof(JapanPage).Assembly.GetManifestResourceStream("japan.json")!;
        using var doc = JsonDocument.Parse(s);
        string S(JsonElement e, string k) => e.GetProperty(k).GetString() ?? "";
        return doc.RootElement.EnumerateArray().Select(a => new JArticle(
            S(a, "id"), S(a, "title"), S(a, "jp"), S(a, "emoji"), S(a, "cat"), S(a, "lead"),
            a.GetProperty("sections").EnumerateArray().Select(x => (x[0].GetString()!, x[1].GetString()!)).ToArray(),
            a.GetProperty("words").EnumerateArray().Select(x => (x[0].GetString()!, x[1].GetString()!, x[2].GetString()!)).ToArray(),
            a.GetProperty("facts").EnumerateArray().Select(x => x.GetString()!).ToArray(),
            a.GetProperty("quiz").EnumerateArray().Select(x => (x[0].GetString()!, x[1].GetString()!, x[2].GetString()!.Split('|'))).ToArray())).ToArray();
    }

    public static bool Read(JArticle a) => ProgressStore.P.Flags.Contains("japan:" + a.Id);

    public void OnShown()
    {
        var root = new StackPanel();
        int read = All.Count(Read);
        root.Children.Add(U.Header("日本", "Япония", $"Культура, обычаи и жизнь японцев · прочитано {read} из {All.Length} статей"));

        // Факт дня — каждый день новый
        var facts = All.SelectMany(a => a.Facts.Select(f => (a, f))).ToArray();
        var (fa, ff) = facts[DateTime.Today.DayOfYear % facts.Length];
        var fact = U.Cols("*", "auto");
        fact.Children.Add(U.Stack(U.T("ФАКТ ДНЯ", "Overline"), U.T(ff, "H3", 16).Also(t => t.TextWrapping = TextWrapping.Wrap).M(0, 6, 0, 8),
            U.Btn("Читать: " + fa.Title, () => Shell.Open(new ArticlePage(fa)), "", "").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left)).CenterV());
        fact.Children.Add(Art.Anime("hello", 150).Col(1).M(16, 0, 0, 0));
        root.Children.Add(U.Card(fact, new Thickness(22, 18, 22, 18)).M(0, 0, 0, 10));

        root.Children.Add(U.T("Статьи", "H2").M(0, 18, 0, 10));
        var grid = new UniformGrid { Columns = U.Columns(3), Margin = new Thickness(-6, 0, -6, 0) };
        foreach (var a in All) grid.Children.Add(Card(a));
        root.Children.Add(grid);
        Content = U.Page(root);
        Anim.Stagger(root, 40, 0, 12);
    }

    static Border Card(JArticle a)
    {
        var head = U.Cols("auto", "*");
        head.Children.Add(Emoji.Img(a.Emoji, 46));
        if (Read(a)) head.Children.Add(U.Pill("✓ прочитано", U.B("GoodSoft"), U.B("Good")).Also(p => { p.HorizontalAlignment = HorizontalAlignment.Right; p.VerticalAlignment = VerticalAlignment.Top; }).Col(1));
        var sp = U.Stack(head, U.T(a.Cat.ToUpperInvariant(), "Overline").M(0, 10, 0, 0), U.T(a.Title, "H3", 15).M(0, 2, 0, 2), U.Jp(a.Jp, 13, U.B("Accent")),
            U.T(a.Lead, "Muted", 12.5).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.MaxHeight = 52; t.TextTrimming = TextTrimming.CharacterEllipsis; }).M(0, 6, 0, 0));
        var card = new Border { Classes = { "Card" }, Child = sp, Margin = new Thickness(6), Cursor = Cursors.Hand };
        HomePage.Hover(card);
        card.PointerReleased += (_, _) => Shell.OpenFrom(card, new ArticlePage(a));
        return card;
    }
}

public sealed class ArticlePage : UserControl, IPage
{
    readonly JArticle _a;
    public string Title => _a.Title;
    public ArticlePage(JArticle a) { _a = a; }

    public void OnShown()
    {
        var root = new StackPanel { MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Center };
        var head = U.Cols("auto", "*");
        head.Children.Add(Emoji.Img(_a.Emoji, 72));
        head.Children.Add(U.Stack(U.T(_a.Cat.ToUpperInvariant(), "Overline"), U.T(_a.Title, "H1", 30), U.Jp(_a.Jp, 16, U.B("Accent"))).CenterV().M(18, 0, 0, 0).Col(1));
        root.Children.Add(head.M(0, 6, 0, 16));
        root.Children.Add(U.T(_a.Lead, "Body", 17).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.LineHeight = 27; }).M(0, 0, 0, 10));

        foreach (var (h, text) in _a.Sections)
        {
            root.Children.Add(U.T(h, "H2", 19).M(0, 18, 0, 6));
            root.Children.Add(U.T(text, "Body", 15).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.LineHeight = 25; }));
        }

        // Слова статьи — с озвучкой и добавлением в карточки
        var words = new StackPanel();
        words.Children.Add(U.T("Слова из статьи", "H2", 19));
        words.Children.Add(U.T("Нажми динамик, чтобы послушать, и «+», чтобы учить в карточках.", "Muted").M(0, 4, 0, 10));
        foreach (var (jp, kana, ru) in _a.Words)
        {
            var g = U.Cols("*", "auto");
            g.Children.Add(U.Row(14, U.Jp(jp, 22).CenterV(), U.Stack(U.Jp(kana != jp ? kana : "", 13, U.B("Accent")), U.T(Kana.ToRomaji(kana), "Muted", 12)).CenterV(),
                U.T(ru, "Body", 14).Also(t => t.TextWrapping = TextWrapping.Wrap).CenterV()));
            var entry = new DictEntry(0, true, jp == kana ? "" : jp, kana, ru);
            var add = new Button { Content = JDict.InStudy(entry) ? "" : "", Width = 34, Height = 34, Classes = { "IconButton" } }.Tip("Добавить в мои карточки");
            add.Click += (_, _) =>
            {
                if (JDict.AddToStudy(entry)) { add.Content = ""; Anim.Pop(add, 0.6); Shell.Toast($"«{jp}» добавлено в «Мои слова»", "追加"); }
                else Shell.Toast("Это слово уже в твоих карточках", "✓");
            };
            g.Children.Add(U.Row(2, U.SpeakBtnSmall(kana), add).CenterV().Col(1));
            words.Children.Add(new Border { Background = U.B("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 8, 8, 8), Margin = new Thickness(0, 0, 0, 6), Child = g });
        }
        root.Children.Add(U.Card(words).M(0, 24, 0, 0));

        // Факты
        var facts = new StackPanel();
        facts.Children.Add(U.T("А знаешь ли ты?", "H2", 19).M(0, 0, 0, 8));
        foreach (var f in _a.Facts)
            facts.Children.Add(U.Row(10, Art.Glyph("sparkle", 16, U.B("Gold")).Also(x => x.VerticalAlignment = VerticalAlignment.Top), U.T(f, "Body", 14).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 700; })).M(0, 0, 0, 8));
        var fb = U.Cols("*", "auto");
        fb.Children.Add(facts);
        fb.Children.Add(Art.Anime("teacher", 170).Col(1).M(12, 0, 0, 0).DesktopOnly());
        root.Children.Add(U.Card(fb).M(0, 16, 0, 0));

        root.Children.Add(Quiz().M(0, 16, 0, 0));
        Content = U.Page(root);
        Anim.Stagger(root, 30, 0, 14);
    }

    Control Quiz()
    {
        var sp = new StackPanel();
        sp.Children.Add(U.T("Проверь себя", "H2", 19).M(0, 0, 0, 6));
        int correct = 0, answered = 0;
        foreach (var (q, a, opts) in _a.Quiz)
        {
            sp.Children.Add(U.T(q, "H3", 15).M(0, 10, 0, 6));
            var wrap = new WrapPanel();
            bool locked = false;
            foreach (var o in opts.Shuffled())
            {
                var b = new Button { Content = o, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 8), FontFamily = U.R<FontFamily>("JpFont") };
                var opt = o;
                b.Click += (_, _) =>
                {
                    if (locked) return; locked = true; answered++;
                    bool ok = opt == a;
                    b.Background = U.B(ok ? "GoodSoft" : "BadSoft"); b.BorderBrush = U.B(ok ? "Good" : "Bad");
                    if (!ok) foreach (var x in wrap.Children.OfType<Button>()) if ((string)x.Content! == a) { x.BorderBrush = U.B("Good"); }
                    if (ok) { correct++; Sfx.Ok(); } else Sfx.Bad();
                    if (answered == _a.Quiz.Length)
                    {
                        bool first = ProgressStore.P.Flags.Add("japan:" + _a.Id);
                        if (first) ProgressStore.AddXp(5 + correct * 3);
                        ProgressStore.Notify();
                        sp.Children.Add(Art.Say(correct == answered ? "Отлично! Ты всё понял(а). すごい！" : $"Верно {correct} из {answered}. Перечитай статью — и всё получится!",
                            correct == answered ? Mood.Joy : Mood.Happy, 90).M(0, 14, 0, 0));
                    }
                };
                wrap.Children.Add(b);
            }
            sp.Children.Add(wrap);
        }
        return U.Card(sp);
    }
}
