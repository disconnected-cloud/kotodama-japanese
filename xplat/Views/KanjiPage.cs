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
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

public sealed class KanjiPage : UserControl, IPage
{
    public string Title => "Кандзи";
    KanjiInfo? _sel;
    string _level = ProgressStore.P.Level is "N4" or "N3" or "N2" ? ProgressStore.P.Level : "N5";
    readonly ContentControl _detail = new();
    Border? _selTile;

    public void OnShown()
    {
        var kanji = Db.KanjiOf(_level).ToArray();
        if (_sel == null || _sel.Level != _level) _sel = kanji[0];
        int learning = kanji.Count(k => ProgressStore.Card(k.Id)?.Reps > 0);
        int due = kanji.Count(k => ProgressStore.IsDue(k.Id));

        var root = new Grid { Margin = (Shell.Compact ? new Thickness(12, 12, 12, 16) : new Thickness(40, 12, 40, 16)), MaxWidth = 1240 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        var lv = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var l in new[] { "N5", "N4", "N3", "N2" })
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = l, GroupName = "klevel", IsChecked = _level == l };
            var ll = l;
            rb.Checked += (_, _) => { _level = ll; _selTile = null; OnShown(); };
            lv.Children.Add(rb);
        }
        root.Children.Add(U.Header("漢", "Кандзи", $"{kanji.Length} иероглифов уровня {_level}  ·  {learning} в изучении", lv));

        var acts = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        acts.Children.Add(U.Btn("Учить новые (5)", LearnNew, "Primary", "").M(0, 0, 10, 0));
        var rev = U.Btn($"Повторить ({due})", () => Shell.Open(new FlashSession("Повторение кандзи", kanji.Where(k => ProgressStore.IsDue(k.Id)).Shuffled().Select(FlashSession.FromKanji))), "", "").M(0, 0, 10, 0);
        rev.IsEnabled = due > 0;
        acts.Children.Add(rev);
        acts.Children.Add(U.Btn(Shell.Compact ? "Значение" : "Квиз: значение", () => Quiz(false), "", "").M(0, 0, 10, 0));
        acts.Children.Add(U.Btn(Shell.Compact ? "Чтение" : "Квиз: чтение слов", () => Quiz(true), "", "").M(0, 0, 10, 0));
        acts.Children.Add(U.Btn(Shell.Compact ? "Ключи" : "Ключи: из чего состоят", () => Shell.Open(new RadicalsPage()), "", ""));
        if (Shell.Compact)
        {
            // телефон: «учить/повторить» в строку, остальное сеткой в две колонки
            var all = acts.Children.ToList(); acts.Children.Clear();
            var top = new WrapPanel(); var rest = new UniformGrid { Columns = 2 };
            foreach (var c in all.Take(2)) top.Children.Add(c);
            foreach (var c in all.Skip(2)) { c.Margin = new Thickness(0, 0, 8, 8); c.HorizontalAlignment = HorizontalAlignment.Stretch; rest.Children.Add(c); }
            root.Children.Add(U.Stack(top, rest).M(0, 0, 0, 12).RowAt(1));
        }
        else root.Children.Add(acts.RowAt(1));

        var tiles = new WrapPanel();
        foreach (var k in kanji) tiles.Children.Add(Tile(k));
        if (Shell.Compact)
        {
            // телефон: вся страница прокручивается, знак открывается своим экраном (пополам с карточкой было тесно)
            root.RowDefinitions[2].Height = GridLength.Auto;
            root.Children.Add(tiles.RowAt(2));
            Content = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Anim.Stagger(tiles, 12, 0, 60);
            return;
        }
        var body = U.Cols("*", "20", "400");
        body.Children.Add(new ScrollViewer { Content = tiles, Padding = new Thickness(0, 0, 8, 0) });
        if (_detail.Parent is ScrollViewer oldSv) oldSv.Content = null; // панель переиспользуется между показами
        body.Children.Add(new ScrollViewer { Content = _detail }.Col(2));
        root.Children.Add(U.Stackify(body, starRows: true).RowAt(2));
        Content = root;
        ShowDetail(_sel);
        Anim.Stagger(tiles, 12, 0, 60);
    }

    Border Tile(KanjiInfo k)
    {
        var c = ProgressStore.Card(k.Id);
        var g = new Grid();
        g.Children.Add(U.Jp(k.Char, 38, null, true).Center().CenterV());
        if (c != null)
            g.Children.Add(new Ellipse
            {
                Width = 7, Height = 7, Fill = c.Mastered ? U.B("Good") : c.Due <= DateTime.Now ? U.B("Accent") : U.B("Gold"),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 9, 9, 0),
            });
        var b = new Border
        {
            Width = 84, Height = 84, Margin = new Thickness(0, 0, 10, 10), CornerRadius = new CornerRadius(18),
            Background = U.B("Surface"), BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(1), Child = g, Cursor = Cursors.Hand,
            
        }.Tip(k.Ru);
        if (k == _sel) Select(b);
        b.PointerEntered += (_, _) => { if (b != _selTile) b.BorderBrush = U.B("StrokeStrong"); };
        b.PointerExited += (_, _) => { if (b != _selTile) b.BorderBrush = U.B("Stroke"); };
        b.PointerReleased += (_, _) =>
        {
            _sel = k; Select(b); ShowDetail(k);
            if (Shell.Compact && _detail.Content is Control card) { _detail.Content = null; Shell.Open(new SimplePage($"{k.Char} — {k.Ru.Split(',', ';')[0]}", card)); }
        };
        return b;
    }

    void Select(Border b)
    {
        if (_selTile != null) { _selTile.BorderBrush = U.B("Stroke"); _selTile.Background = U.B("Surface"); }
        _selTile = b;
        b.BorderBrush = U.B("Accent");
        b.Background = U.B("AccentSoft");
    }

    void ShowDetail(KanjiInfo k)
    {
        var sp = new StackPanel();
        var strokes = new StrokeView(k.Char, 210) { HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(strokes);
        if (strokes.HasData)
        {
            var ctl = U.Row(8,
                U.Btn("Порядок черт", () => strokes.Play(), "", ""),
                U.Btn("Медленно", () => strokes.Play(0.5), "Ghost", ""),
                U.Btn("Номера", () => strokes.ShowAll(true), "Ghost", ""));
            ctl.HorizontalAlignment = HorizontalAlignment.Center; ctl.Margin = new Thickness(0, 10, 0, 16);
            sp.Children.Add(ctl);
            strokes.Loaded += (_, _) => strokes.Play(1.3);
        }
        var head = U.Cols("*", "auto");
        head.Children.Add(U.Stack(U.T(k.Ru, "H2", 22), U.T($"{k.Strokes} {MainView.Plural(k.Strokes, "черта", "черты", "черт")}", "Muted").M(0, 4, 0, 0)));
        head.Children.Add(U.SpeakBtn(() => k.Examples.Length > 0 ? k.Examples[0].Kana : k.Char).Col(1));
        sp.Children.Add(head);

        if (Mnemonics.For(k.Char) is { } mn && mn.Parts.Length > 0)
        {
            sp.Children.Add(U.T("ИЗ ЧЕГО СОСТОИТ", "Overline").M(0, 16, 0, 6));
            var parts = new WrapPanel();
            foreach (var part in mn.Parts)
                parts.Children.Add(new Border
                {
                    Background = U.B("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6),
                    Child = U.Row(6, U.Jp(part[0], 18, U.B("Accent"), true).CenterV(), U.T(part[1].Length > 0 ? part[1] : "часть", "Muted", 12).CenterV()),
                });
            sp.Children.Add(parts);
            var hint = Mnemonics.Hint(k.Char, k.Ru);
            if (hint.Length > 0)
                sp.Children.Add(new Border
                {
                    Background = U.B("AccentSoft"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 4, 0, 0),
                    Child = U.Row(8, new TextBlock { Text = "\uEA80", FontFamily = U.R<FontFamily>("Icons"), Foreground = U.B("Gold"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) }, U.T(hint, "Body", 13.5).Also(t => t.MaxWidth = 320)),
                });
        }
        sp.Children.Add(Reading("ОН-ЁМИ · китайское чтение", k.On, "Accent2"));
        sp.Children.Add(Reading("КУН-ЁМИ · японское чтение", k.Kun.Length > 0 ? k.Kun : "—", "Accent"));

        sp.Children.Add(U.T("ПРИМЕРЫ", "Overline").M(0, 20, 0, 8));
        foreach (var ex in k.Examples)
        {
            var row = U.Cols("*", "auto");
            row.Children.Add(U.Stack(
                U.Jp(ex.Jp, 20),
                U.T($"{ex.Kana} · {ex.Romaji}", "Muted", 12.5),
                U.T(ex.Ru, "Body", 13.5)));
            row.Children.Add(U.SpeakBtnSmall(ex.Kana).Col(1));
            sp.Children.Add(new Border { Background = U.B("Surface2"), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 10, 10, 10), Margin = new Thickness(0, 0, 0, 8), Child = row });
        }

        var c = ProgressStore.Card(k.Id);
        sp.Children.Add(U.T(c == null ? "Ещё не изучается" : c.Mastered ? "Выучен ✓" : $"Следующее повторение: {c.Due:d MMM, HH:mm}", "Muted").M(0, 10, 0, 0));
        _detail.Content = U.Card(sp);
        Anim.FadeUp((Control)_detail.Content, 0, 10, 260);
    }

    static Control Reading(string label, string value, string brush) =>
        U.Stack(U.T(label, "Overline").M(0, 16, 0, 4), U.Jp(value, 18, U.B(brush)));

    void LearnNew()
    {
        var fresh = Db.KanjiOf(_level).Where(k => ProgressStore.Card(k.Id) == null).Take(5).ToList();
        if (fresh.Count == 0) { Shell.Toast($"Все кандзи {_level} уже в изучении!", "完了"); return; }
        Shell.Open(LearnSession.Kanji(fresh, OnShown));
    }

    void Quiz(bool reading)
    {
        var all = Db.KanjiOf(_level).ToArray();
        var started = all.Where(k => ProgressStore.Card(k.Id) != null).ToList();
        // Проверяем только то, что уже изучено: незнакомые знаки в квизе только сбивают
        if (started.Count < 4) { Shell.Toast("Сначала выучи хотя бы 4 иероглифа — нажми «Учить новые»", "学"); return; }
        var pool = started;
        var ruPool = all.Select(k => k.Ru).ToArray();
        IEnumerable<QuizQ> qs;
        if (!reading)
            qs = pool.Shuffled().Take(15).Select(k => new QuizQ
            {
                Prompt = k.Char, PromptSize = 130, Sub = "Что означает этот иероглиф?", Answer = k.Ru,
                Options = QuizSession.MakeOptions(k.Ru, ruPool), Speak = k.Examples.FirstOrDefault()?.Kana,
                Explain = $"он: {k.On}  ·  кун: {(k.Kun.Length > 0 ? k.Kun : "—")}", Key = k.Id,
            });
        else
        {
            var exs = pool.SelectMany(k => k.Examples).ToList();
            var kanaPool = exs.Select(e => e.Kana).ToArray();
            qs = exs.Shuffled().Take(15).Select(e => new QuizQ
            {
                Prompt = e.Jp, PromptSize = 72, Sub = "Как читается это слово?", Answer = e.Kana, OptionsJp = true,
                Options = QuizSession.MakeOptions(e.Kana, kanaPool), Speak = e.Kana, Explain = $"{e.Kana} ({Kana.ToRomaji(e.Kana)}) — {e.Ru}",
            });
        }
        Shell.Open(new QuizSession(reading ? "Чтение кандзи" : "Значения кандзи", qs));
    }
}

/// <summary>Простой экран с заголовком в верхней панели (телефон: карточка кандзи и т.п.).</summary>
public sealed class SimplePage : UserControl, IPage
{
    public string Title { get; }
    public SimplePage(string title, Control content) { Title = title; Content = U.Page(content); }
}
