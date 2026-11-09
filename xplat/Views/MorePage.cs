using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>Телефон: вкладка «Ещё» — все разделы списком (на компьютере они в боковом меню).</summary>
public sealed class MorePage : UserControl, IPage
{
    public string Title => "Разделы";

    static readonly (string key, string jp, string title, string sub)[] Items =
    {
        ("kana", "あ", "Кана", "Хирагана и катакана"),
        ("words", "語", "Слова", "Карточки и квизы"),
        ("kanji", "漢", "Кандзи", "Порядок черт и чтения"),
        ("grammar", "文", "Грамматика", "45 уроков N5–N3"),
        ("dialogs", "対", "Диалоги", "Разговоры по ролям"),
        ("reader", "読", "Чтение", "Тексты с фуриганой"),
        ("dict", "辞", "Словарь", "69 000 слов"),
        ("trainers", "練", "Тренажёры", "Спряжение, игры, тест JLPT"),
        ("settings", "設", "Настройки", "Цель, звук, микрофон"),
    };

    public void OnShown()
    {
        var sp = new StackPanel { Spacing = 8 };
        sp.Children.Add(U.Header("道", "Разделы", "Всё, что есть в Kotodama"));
        foreach (var (key, jp, title, sub) in Items)
        {
            var g = U.Cols("auto", "*", "auto");
            g.Children.Add(new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Background = U.B("AccentSoft"),
                Child = new TextBlock { Text = jp, FontFamily = U.R<FontFamily>("JpSerif"), FontSize = 20, Foreground = U.B("Accent"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            g.Children.Add(U.Stack(U.T(title, "H3", 15), U.T(sub, "Muted", 12)).CenterV().M(14, 0, 0, 0).Col(1));
            g.Children.Add(new TextBlock { Text = "", FontFamily = U.R<FontFamily>("Icons"), Foreground = U.B("Faint"), VerticalAlignment = VerticalAlignment.Center }.Col(2));
            var card = new Border { Classes = { "Card" }, Padding = new Thickness(14, 10), Child = g, Cursor = Cursors.Hand };
            var k = key;
            card.OnClick(() => Shell.Go(k));
            sp.Children.Add(card);
        }
        Content = U.Page(sp);
        Anim.Stagger(sp, 25, 0);
    }
}
