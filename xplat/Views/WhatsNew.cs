using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>«Что нового» — один раз после обновления до новой версии (не новичкам: им хватает знакомства).</summary>
public static class WhatsNew
{
    const string Flag = "whatsnew-2.5";

    static readonly (string icon, string title, string text, Func<Control>? open)[] Items =
    {
        ("crown", "Уровень N1", "10 уроков грамматики, ~600 слов, 100 кандзи и 5 юнитов пути — путь теперь до конца JLPT.", () => new GrammarPage()),
        ("headphones", "Аудиоплеер", "Слова и фразы по кругу: японский — пауза — перевод голосом. Можно слушать со свёрнутым окном.", () => new PlayerPage()),
        ("keyboard", "Набор на японском", "Печатай латиницей, как в японской раскладке: скорость, точность и рекорды.", () => new TypingPage()),
        ("clock", "Числа, время, цены", "さんびゃく, はっせん, よじ, ついたち — на слух и на чтение.", () => new NumbersPage()),
        ("dialogs", "Больше живой речи", "12 новых диалогов, 8 уроков разговорника, 6 статей о Японии и две новые книги.", null),
        ("grid", "Удобнее на компьютере", "Свёрнутое меню (Ctrl+B), разделы по Ctrl+1…0, шпаргалка клавиш — F1, спряжение и заметки в словаре, экспорт в Anki.", null),
    };

    /// <summary>true — окно показано (тогда другие всплывающие окна в этот запуск не нужны).</summary>
    public static bool MaybeShow()
    {
        var p = ProgressStore.P;
        if (!p.Flags.Contains("onboarded") || !p.Flags.Add(Flag)) return false;
        ProgressStore.Notify();
        Show();
        return true;
    }

    public static void Show()
    {
        var sp = new StackPanel { MaxWidth = 640 };
        var head = U.Cols("auto", "*");
        head.Children.Add(Art.Mascot(96, Mood.Joy));
        head.Children.Add(U.Stack(U.Jp("新しくなりました！", 15, U.B("Accent")), U.T($"Что нового в Kotodama {Updater.Current.ToString(2)}", "H2", 22).Also(t => t.TextWrapping = TextWrapping.Wrap),
            U.T("Коротко о главном — всё уже внутри.", "Muted")).CenterV().M(16, 0, 30, 0).Col(1));
        sp.Children.Add(head.M(0, 0, 0, 14));
        foreach (var (icon, title, text, open) in Items)
        {
            var g = U.Cols("auto", "*", "auto");
            g.Children.Add(Art.Icon(icon, 36).Also(i => i.VerticalAlignment = VerticalAlignment.Top));
            g.Children.Add(U.Stack(U.T(title, "H3", 14.5), U.T(text, "Muted", 12.5).Also(t => t.TextWrapping = TextWrapping.Wrap)).M(12, 0, 8, 0).Col(1));
            if (open != null)
                g.Children.Add(U.Btn("Открыть", () => { Shell.Main.CloseOverlay(); Shell.Open(open()); }, "Ghost").CenterV().Col(2));
            sp.Children.Add(g.M(0, 0, 0, 10));
        }
        sp.Children.Add(U.Btn("Отлично!", Shell.Main.CloseOverlay, "Primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Right).M(0, 8, 0, 0));
        Shell.Main.ShowOverlay(sp, 700);
    }
}
