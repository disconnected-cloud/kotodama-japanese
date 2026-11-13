using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>Телефон: вкладка «Ещё» — все разделы плитками по группам (на компьютере они в боковом меню).</summary>
public sealed class MorePage : UserControl, IPage
{
    public string Title => "Разделы";

    sealed record Entry(string Icon, string Title, string Sub, Action Go);

    static readonly (string group, Entry[] items)[] Groups =
    {
        ("УЧИТЬ", new Entry[]
        {
            new("kana", "Кана", "Хирагана и катакана", () => Shell.Go("kana")),
            new("words", "Слова", "Карточки и квизы", () => Shell.Go("words")),
            new("kanji", "Кандзи", "Черты и чтения", () => Shell.Go("kanji")),
            new("grammar", "Грамматика", $"{Db.Grammar.Length} уроков N5–N1", () => Shell.Go("grammar")),
        }),
        ("ГОВОРИТЬ И ЧИТАТЬ", new Entry[]
        {
            new("dialogs", "Диалоги", "Разговоры по ролям", () => Shell.Go("dialogs")),
            new("books", "Книги", "Тексты с фуриганой", () => Shell.Go("reader")),
            new("torii", "Япония", "Культура и обычаи", () => Shell.Go("japan")),
            new("dict", "Словарь", "Справочник для поиска", () => Shell.Go("dict")),
        }),
        ("ПРАКТИКА", new Entry[]
        {
            new("trainers", "Тренажёры", "Спряжение, тест JLPT", () => Shell.Go("trainers")),
            new("write", "Прописи", "Пиши пальцем", () => Shell.Open(new WritingPage())),
            new("headphones", "Аудиоплеер", "Слушай без экрана", () => Shell.Open(new PlayerPage())),
            new("trophy", "Прогресс", "Активность и навыки", () => Shell.Go("stats")),
        }),
    };

    public void OnShown()
    {
        var sp = new StackPanel();
        sp.Children.Add(U.Header("道", "Разделы", "Всё, что есть в Kotodama"));
        foreach (var (group, items) in Groups)
        {
            sp.Children.Add(U.T(group, "Overline").M(4, 6, 0, 10));
            var grid = new UniformGrid { Columns = 2, Margin = new Thickness(-5, 0, -5, 10) };
            foreach (var e in items) grid.Children.Add(Tile(e));
            sp.Children.Add(grid);
        }
        sp.Children.Add(Row("settings", "Настройки", "Цель, звук, голос, микрофон, тема", () => Shell.Go("settings")).M(0, 4, 0, 0));
        Content = U.Page(sp);
        Anim.Stagger(sp, 25, 0);
    }

    static Border Tile(Entry e)
    {
        var st = new StackPanel();
        st.Children.Add(Art.Icon(e.Icon, 40).Also(i => i.HorizontalAlignment = HorizontalAlignment.Left));
        st.Children.Add(U.T(e.Title, "H3", 15).M(0, 12, 0, 2));
        st.Children.Add(U.T(e.Sub, "Muted", 12).Also(t => { t.TextWrapping = Avalonia.Media.TextWrapping.Wrap; t.MinHeight = 32; }));
        var card = new Border { Classes = { "Card" }, Padding = new Thickness(14), Margin = new Thickness(5), Child = st, Cursor = Cursors.Hand };
        card.OnClick(e.Go);
        return card;
    }

    static Border Row(string icon, string title, string sub, Action go)
    {
        var g = U.Cols("auto", "*", "auto");
        g.Children.Add(Art.Icon(icon, 40));
        g.Children.Add(U.Stack(U.T(title, "H3", 15), U.T(sub, "Muted", 12)).CenterV().M(14, 0, 0, 0).Col(1));
        g.Children.Add(Art.Glyph("back", 18, U.B("Faint")).Also(x => x.RenderTransform = new Avalonia.Media.ScaleTransform(-1, 1)).Col(2));
        var card = new Border { Classes = { "Card" }, Padding = new Thickness(14, 12), Child = g, Cursor = Cursors.Hand };
        card.OnClick(go);
        return card;
    }
}
