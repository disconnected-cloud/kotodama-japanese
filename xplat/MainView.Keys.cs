using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Matsubakira.Services;
using Matsubakira.Views;

namespace Matsubakira;

/// <summary>Горячие клавиши компьютера: разделы Ctrl+1…0, свернуть меню Ctrl+B, полный экран F11, справка F1.</summary>
public partial class MainView
{
    static readonly (Key key, string section, string name)[] SectionKeys =
    {
        (Key.D1, "home", "Главная"), (Key.D2, "path", "Путь обучения"), (Key.D3, "review", "Повторение"), (Key.D4, "words", "Слова"),
        (Key.D5, "kanji", "Кандзи"), (Key.D6, "grammar", "Грамматика"), (Key.D7, "speech", "Говорение"), (Key.D8, "reader", "Книги и тексты"),
        (Key.D9, "dict", "Словарь"), (Key.D0, "trainers", "Тренажёры и игры"),
    };

    /// <summary>Сочетание обработано? Буквы проверяем и по физическому месту — работают в русской раскладке.</summary>
    bool HandleHotkey(KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        bool Letter(Key k, PhysicalKey pk) => e.Key == k || e.PhysicalKey == pk;
        if (e.Key == Key.F1) { ShowHotkeys(); return true; }
        if (e.Key == Key.F11 && Win is { } w) { w.WindowState = w.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen; return true; }
        if (!ctrl) return false;
        if (Letter(Key.B, PhysicalKey.B)) { ToggleRail(); return true; }
        if (Letter(Key.U, PhysicalKey.U)) { StudyPage.Start(); return true; }
        if (Letter(Key.P, PhysicalKey.P) && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { Push(new PlayerPage()); return true; }
        if (e.Key == Key.OemComma || e.PhysicalKey == PhysicalKey.Comma) { NavigateRoot("settings"); return true; }
        if (e.Key == Key.OemQuestion || e.PhysicalKey == PhysicalKey.Slash) { ShowHotkeys(); return true; }
        foreach (var (key, section, _) in SectionKeys)
            if (e.Key == key || e.Key == key - Key.D0 + Key.NumPad0) { NavigateRoot(section); return true; }
        return false;
    }

    /// <summary>Шпаргалка по клавишам (F1).</summary>
    public void ShowHotkeys()
    {
        var rows = new List<(string keys, string what)>
        {
            ("Ctrl+K", "Поиск по всему: разделы, уроки, слова"),
            ("Ctrl+U", "Учиться — умная сессия на сегодня"),
            ("Ctrl+Shift+P", "Аудиоплеер"),
            ("Ctrl+B", "Свернуть / развернуть меню"),
            ("Ctrl+,", "Настройки"),
            ("F11", "Во весь экран"),
            ("Esc", "Назад"),
        };
        rows.AddRange(SectionKeys.Select(s => ($"Ctrl+{(s.key == Key.D0 ? "0" : ((int)(s.key - Key.D0)).ToString())}", s.name)));
        var exercises = new (string, string)[]
        {
            ("1–4", "Вариант ответа в квизе"), ("Enter", "Проверить / дальше"), ("Пробел", "Перевернуть карточку · пауза в плеере"),
            ("← →", "Листать в аудиоплеере"), ("Backspace", "Убрать слово в «Собери предложение»"),
        };

        Control Table(IEnumerable<(string keys, string what)> items)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("auto,*") };
            int r = 0;
            foreach (var (keys, what) in items)
            {
                g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 16, 4) };
                foreach (var part in keys.Split('+'))
                {
                    if (chips.Children.Count > 0) chips.Children.Add(new TextBlock { Text = "+", Foreground = U.B("Faint"), Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center });
                    chips.Children.Add(new Border
                    {
                        Background = U.B("Surface2"), BorderBrush = U.B("StrokeStrong"), BorderThickness = new Thickness(1, 1, 1, 2), CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(7, 2), MinWidth = 26,
                        Child = new TextBlock { Text = part, FontSize = 12, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                    });
                }
                Grid.SetRow(chips, r);
                g.Children.Add(chips);
                g.Children.Add(U.T(what, "Body", 13.5).CenterV().RowAt(r).Col(1));
                r++;
            }
            return g;
        }

        var cols = new Grid { ColumnDefinitions = new ColumnDefinitions("*,32,*") };
        cols.Children.Add(U.Stack(U.T("Везде", "Overline").M(0, 0, 0, 6), Table(rows)));
        cols.Children.Add(U.Stack(U.T("В упражнениях", "Overline").M(0, 0, 0, 6), Table(exercises),
            U.T("Буквенные сочетания работают и в русской раскладке.", "Muted", 12).Also(t => t.TextWrapping = TextWrapping.Wrap).M(0, 16, 0, 0)).Col(2));
        var sp = U.Stack(
            U.Row(12, Controls.Art.Icon("keyboard", 40), U.Stack(U.T("Горячие клавиши", "H2"), U.T("F1 — открыть эту шпаргалку снова", "Muted"))).M(0, 0, 0, 18),
            cols,
            U.Btn("Понятно", CloseOverlay, "Primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Right).M(0, 18, 0, 0));
        sp.MaxWidth = 760;
        ShowOverlay(sp, 820);
    }
}
