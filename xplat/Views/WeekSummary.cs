using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>
/// «Итоги недели»: опыт, дни занятий, новые слова, фразы вслух, лучший день — с Википе-тан.
/// Показывается один раз в начале новой недели (если на прошлой неделе были занятия) и по кнопке на странице «Прогресс».
/// </summary>
public static class WeekSummary
{
    static readonly System.Globalization.CultureInfo Ru = new("ru-RU");

    /// <summary>Понедельник прошлой недели.</summary>
    static DateTime LastWeek => Challenges.WeekStart.AddDays(-7);

    static int Xp(DateTime d) => ProgressStore.P.History.GetValueOrDefault(d.ToString("yyyy-MM-dd"));

    /// <summary>Вызывается при запуске: впервые на этой неделе и прошлая неделя была не пустой — показываем итоги.</summary>
    public static void MaybeShow()
    {
        var flag = "weeksum:" + LastWeek.ToString("yyyy-MM-dd");
        var p = ProgressStore.P;
        if (p.Flags.Contains(flag) || !p.Flags.Contains("onboarded")) return;
        if (Enumerable.Range(0, 7).Sum(i => Xp(LastWeek.AddDays(i))) == 0) return;
        p.Flags.Add(flag);
        ProgressStore.Notify();
        Show(LastWeek);
    }

    public static void Show(DateTime monday)
    {
        var days = Enumerable.Range(0, 7).Select(i => monday.AddDays(i)).ToArray();
        int xp = days.Sum(Xp), active = days.Count(d => Xp(d) > 0);
        int Sum(string kind) => days.Sum(d => Challenges.CountOn(d, kind));
        int fresh = Sum("new"), spoken = Sum("speak"), reviewed = Sum("review"), heard = Sum("listen");
        var best = days.OrderByDescending(Xp).First();
        bool thisWeek = monday == Challenges.WeekStart;

        var (say, mood) = active >= 6 ? ("Почти каждый день! Вот это упорство — すごい！", Mood.Joy)
            : active >= 3 ? ("Хорошая неделя! Чуть регулярнее — и слова начнут всплывать сами.", Mood.Happy)
            : ("Неделя была спокойной. Даже 5 минут в день дают заметный результат — попробуем?", Mood.Wink);

        var sp = new StackPanel();
        sp.Children.Add(U.T((thisWeek ? "ЭТА НЕДЕЛЯ · " : "ИТОГИ НЕДЕЛИ · ") + $"{monday:d MMM} — {monday.AddDays(6):d MMM}".ToUpper(Ru), "Overline").M(0, 0, 40, 0));
        sp.Children.Add(Art.Say(say, mood, 110).M(0, 10, 0, 6));

        var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(-4, 6, -4, 0) };
        void Tile(string icon, string value, string label) => grid.Children.Add(new Border
        {
            Background = U.B("Surface2"), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 10), Margin = new Thickness(4),
            Child = U.Row(10, Art.Icon(icon, 34).CenterV(), U.Stack(U.T(value, "H2", 20), U.T(label, "Muted", 12))),
        });
        Tile("star", $"{xp} XP", "опыт за неделю");
        Tile("flame", $"{active} из 7", MainView.Plural(active, "день занятий", "дня занятий", "дней занятий"));
        Tile("words", fresh.ToString(), "новых слов и кандзи");
        Tile("speech", spoken.ToString(), "фраз сказано вслух");
        Tile("review", reviewed.ToString(), "карточек повторено");
        Tile("voice", heard.ToString(), "слов узнано на слух");
        sp.Children.Add(grid);

        if (Xp(best) > 0)
            sp.Children.Add(U.T($"Лучший день — {best.ToString("dddd", Ru)}: {Xp(best)} XP", "Body", 14).M(0, 12, 0, 0));

        var ok = U.Btn(thisWeek ? "Продолжаем!" : "Отлично, новая неделя!", () => Shell.Main.CloseOverlay(), "Primary", "");
        ok.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(ok.M(0, 16, 0, 0));
        Shell.Main.ShowOverlay(sp);
    }

    /// <summary>Для страницы «Прогресс»: итоги прошлой недели (или текущей, если прошлая пустая).</summary>
    public static void ShowLatest() =>
        Show(Enumerable.Range(0, 7).Sum(i => Xp(LastWeek.AddDays(i))) > 0 ? LastWeek : Challenges.WeekStart);
}
