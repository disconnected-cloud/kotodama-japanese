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
    const string Flag = "whatsnew-3.1";

    /// <summary>Телефон: прошлый APK был 2.1 — рассказываем обо всём, что пришло с тех пор, без функций компьютера.</summary>
    static readonly (string icon, string title, string text, Func<Control>? open)[] PhoneItems =
    {
        ("kana", "Кана больше не квадратики", "Японский шрифт теперь внутри приложения: хирагана и катакана видны на любом телефоне.", null),
        ("crown", "Уровень N1", "10 уроков грамматики, 591 слово, 100 кандзи с порядком черт и 5 новых юнитов пути — всего 41.", () => new PathPage()),
        ("headphones", "Аудиоплеер", "Слушай без экрана: японский — пауза — перевод голосом. Подходит для дороги.", () => new PlayerPage()),
        ("clock", "Числа и набор", "Тренажёры «Числа, время, цены» (さんびゃく, はっせん) и «Набор на японском» — латиница превращается в кану.", () => new NumbersPage()),
        ("dialogs", "Новые диалоги и тексты", "12 диалогов (конбини, вокзал, аптека, онсэн), 8 уроков разговорника, статьи о Японии, новые книги.", null),
        ("check", "Выверенный словарь", "Исправлены ~300 переводов и латиница; на странице слова — таблица спряжения и личные заметки.", null),
    };

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
        bool phone = Shell.Compact;
        head.Children.Add(Art.Mascot(phone ? 64 : 96, Mood.Joy));
        head.Children.Add(U.Stack(U.Jp("新しくなりました！", phone ? 12 : 15, U.B("Accent")), U.T($"Что нового в Kotodama {Updater.Current.ToString(2)}", "H2", phone ? 19 : 22).Also(t => t.TextWrapping = TextWrapping.Wrap),
            U.T("Коротко о главном — всё уже внутри.", "Muted")).CenterV().M(phone ? 12 : 16, 0, 30, 0).Col(1));
        sp.Children.Add(head.M(0, 0, 0, 14));
        foreach (var (icon, title, text, open) in Shell.Compact ? PhoneItems : Items)
        {
            var g = U.Cols("auto", "*", "auto");
            g.Children.Add(Art.Icon(icon, 36).Also(i => i.VerticalAlignment = VerticalAlignment.Top));
            var body = U.Stack(U.T(title, "H3", 14.5), U.T(text, "Muted", 12.5).Also(t => t.TextWrapping = TextWrapping.Wrap));
            g.Children.Add(body.M(12, 0, 8, 0).Col(1));
            // на узком экране кнопка под текстом, иначе текст сжимается в столбик
            var btn = open == null ? null : U.Btn("Открыть", () => { Shell.Main.CloseOverlay(); Shell.Open(open()); }, "Ghost");
            if (btn != null && Shell.Compact) body.Children.Add(btn.Also(b => b.HorizontalAlignment = HorizontalAlignment.Left).M(-12, 2, 0, 0));
            else if (btn != null) g.Children.Add(btn.CenterV().Col(2));
            sp.Children.Add(g.M(0, 0, 0, 10));
        }
        sp.Children.Add(U.Btn("Отлично!", Shell.Main.CloseOverlay, "Primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Right).M(0, 8, 0, 0));
        Shell.Main.ShowOverlay(sp, 700);
    }
}
