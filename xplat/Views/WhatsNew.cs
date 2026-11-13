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
    const string Flag = "whatsnew-3.0";

    static readonly (string icon, string title, string text, Func<Control>? open)[] Items =
    {
        ("headphones", "Мини-плеер", "Аудиоплеер больше не останавливается при переходе: внизу окна — панель с паузой и листанием. Учись под озвучку.", () => new PlayerPage()),
        ("tomato", "Фокус-таймер", "25 минут занятий — перерыв. Кнопка в заголовке окна или Ctrl+T; за каждую сессию +10 XP.", null),
        ("check", "Выверенный словарь", "Исправлены ~300 переводов, неверные примеры и латиница (には — niwa, お母さん — okaasan); у примеров N1 появилась латиница.", null),
        ("kana", "Озвучка каны", "Отдельный знак は звучит «ха», а не «ва»; へ — «хэ».", null),
        ("trophy", "Итоговый тест юнита", "Открывается в любом юните — раньше в юнитах каны кнопка молчала.", () => new PathPage()),
        ("flame", "Быстрее и чище", "Главы книг открываются заметно быстрее, интерфейс меньше пересчитывается, лишние файлы удалены.", null),
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
