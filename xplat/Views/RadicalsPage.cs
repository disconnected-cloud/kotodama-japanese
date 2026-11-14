using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>
/// Ключи кандзи: из каких частей собираются иероглифы. Выбери часть — увидишь все знаки программы,
/// в которых она встречается. Так новые иероглифы запоминаются «по кирпичикам».
/// </summary>
public sealed class RadicalsPage : UserControl, IPage
{
    public string Title => "Ключи кандзи";
    string? _sel;
    readonly ContentControl _detail = new();

    // часть → (название, иероглифы с ней); только части с названием и хотя бы 2 иероглифами
    static readonly Lazy<List<(string part, string name, List<KanjiInfo> kanji)>> Index = new(() =>
    {
        var d = new Dictionary<string, (string name, List<KanjiInfo> list)>();
        foreach (var k in Db.Kanji)
            foreach (var p in Mnemonics.For(k.Char)?.Parts ?? Array.Empty<string[]>())
            {
                if (p.Length < 2 || p[1].Length == 0) continue;
                if (!d.TryGetValue(p[0], out var e)) d[p[0]] = e = (p[1], new List<KanjiInfo>());
                if (!e.list.Contains(k)) e.list.Add(k);
            }
        return d.Where(x => x.Value.list.Count >= 2).OrderByDescending(x => x.Value.list.Count)
            .Select(x => (x.Key, x.Value.name, x.Value.list.OrderBy(k => k.Level == "N5" ? 0 : k.Level == "N4" ? 1 : 2).ToList())).ToList();
    });

    public void OnShown()
    {
        var all = Index.Value;
        _sel ??= all.FirstOrDefault().part;
        var root = new StackPanel();
        root.Children.Add(U.Header("部", "Ключи кандзи", $"{all.Count} частей, из которых собраны иероглифы программы. Выучи часть — и узнаешь её в десятках знаков."));

        var body = U.Cols("*", "24", "420");
        var grid = new WrapPanel();
        foreach (var (part, name, kanji) in all)
        {
            bool on = part == _sel;
            var tile = new Border
            {
                Width = 96, Height = 96, Margin = new Thickness(0, 0, 10, 10), CornerRadius = new CornerRadius(16), Cursor = Cursors.Hand,
                Background = U.B(on ? "AccentSoft" : "Surface"), BorderBrush = U.B(on ? "Accent" : "Stroke"), BorderThickness = new Thickness(1),
                Child = U.Stack(U.Jp(part, 32, on ? U.B("Accent") : null, true).Center(),
                    U.T(name.Length > 12 ? name[..12] + "…" : name, "Muted", 12).Center(), U.T($"×{kanji.Count}", "Muted", 12, U.B("Faint")).Center()).CenterV(),
            }.Tip(name);
            var pp = part;
            tile.PointerReleased += (_, _) => { _sel = pp; OnShown(); };
            grid.Children.Add(tile);
        }
        body.Children.Add(grid);
        body.Children.Add(_detail.Detach().Col(2));
        root.Children.Add(U.Stackify(body));
        Content = U.Page(root);
        ShowDetail(all.First(x => x.part == _sel));
    }

    void ShowDetail((string part, string name, List<KanjiInfo> kanji) r)
    {
        var sp = new StackPanel();
        var head = U.Cols("auto", "*");
        head.Children.Add(U.Jp(r.part, 64, U.B("Accent"), true));
        head.Children.Add(U.Stack(U.T(r.name, "H2", 22).Also(t => t.TextWrapping = TextWrapping.Wrap), U.T($"встречается в {r.kanji.Count} {MainView.Plural(r.kanji.Count, "иероглифе", "иероглифах", "иероглифах")}", "Muted")).CenterV().M(16, 0, 0, 0).Col(1));
        sp.Children.Add(head);
        sp.Children.Add(U.T("ИЕРОГЛИФЫ С ЭТОЙ ЧАСТЬЮ", "Overline").M(0, 16, 0, 8));
        foreach (var k in r.kanji.Take(24))
        {
            var g = U.Cols("auto", "*", "auto");
            g.Children.Add(U.Jp(k.Char, 30, null, true).CenterV());
            g.Children.Add(U.Stack(U.T(k.Ru, "H3", 14).Also(t => t.TextTrimming = TextTrimming.CharacterEllipsis),
                U.T(Mnemonics.Hint(k.Char, k.Ru) is { Length: > 0 } h ? h : $"он: {k.On}", "Muted", 12).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.MaxHeight = 34; })).CenterV().M(12, 0, 8, 0).Col(1));
            g.Children.Add(U.Pill(k.Level).CenterV().Col(2));
            var row = new Border { Background = U.B("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8), Margin = new Thickness(0, 0, 0, 6), Child = g, Cursor = Cursors.Hand };
            var kk = k;
            row.PointerReleased += async (_, _) =>
            {
                await JDict.LoadAsync();
                if (JDict.Lookup(kk.Char) is { } e) Shell.Open(new WordPage(e));
            };
            sp.Children.Add(row);
        }
        var learn = U.Btn("Учить эти иероглифы", () =>
        {
            var fresh = r.kanji.Where(k => ProgressStore.Card(k.Id) == null).Take(5).ToList();
            if (fresh.Count == 0) { Shell.Toast("Все эти иероглифы уже в изучении!", "完了"); return; }
            Shell.Open(LearnSession.Kanji(fresh, OnShown));
        }, "Primary", "");
        learn.HorizontalAlignment = HorizontalAlignment.Left; learn.Margin = new Thickness(0, 10, 0, 0);
        sp.Children.Add(learn);
        var card = U.Card(sp);
        _detail.Content = card;
        Anim.FadeUp(card, 0, 10, 240);
    }
}
