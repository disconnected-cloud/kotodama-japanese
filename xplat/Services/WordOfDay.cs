using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Matsubakira.Services;

/// <summary>
/// «Минута японского»: каждый день одно слово с примером. В выбранное время — системное уведомление
/// и озвучка слова и фразы; карточка открывается по клику на уведомление или с главной.
/// </summary>
public static class WordOfDay
{
    /// <summary>Слово на дату: детерминированно (весь день одно и то же), сначала из ещё не изучаемых слов уровня.</summary>
    public static Word? For(DateTime day)
    {
        var p = ProgressStore.P;
        var pool = Db.WordsOf(p.Level is "N5" or "N4" or "N3" or "N2" or "N1" or "1000" ? p.Level : "N5").Where(w => w.ExJp.Length > 0).ToList();
        if (pool.Count == 0) pool = Db.Words.Where(w => w.ExJp.Length > 0).ToList();
        if (pool.Count == 0) return null;
        var fresh = pool.Where(w => ProgressStore.Card(w.Id) == null).ToList();
        var list = fresh.Count > 20 ? fresh : pool;
        int seed = day.Year * 1000 + day.DayOfYear;
        return list[(int)((uint)(seed * 2654435761u) % (uint)list.Count)];
    }

    public static Word? Today => For(DateTime.Today);

    /// <summary>Без служебной нумерации значений из словаря: «1; вся жизнь» → «вся жизнь».</summary>
    public static string CleanRu(string ru) => System.Text.RegularExpressions.Regex.Replace(ru, @"^\s*\d+[;:.)]\s*", "");

    static bool TimeReached()
    {
        var t = ProgressStore.P.WordOfDayTime;
        if (string.IsNullOrEmpty(t) || !TimeSpan.TryParse(t, out var at)) return false;
        return DateTime.Now.TimeOfDay >= at;
    }

    /// <summary>Раз в минуту: пришло время и сегодня ещё не показывали — уведомление + озвучка.</summary>
    public static void Tick()
    {
        var p = ProgressStore.P;
        if (p.LastWordOfDay.Date == DateTime.Today || !TimeReached()) return;
        var w = Today;
        if (w == null) return;
        p.LastWordOfDay = DateTime.Now;
        ProgressStore.Notify();
        Tray.Notify($"Слово дня: {w.Jp}", $"{w.Kana} · {w.Romaji} — {CleanRu(w.Ru)}\n{w.ExJp}\n{w.ExRu}", "word", force: true);
        if (p.WordOfDaySpeak) _ = SpeakAsync(w);
    }

    public static async Task SpeakAsync(Word w)
    {
        await Speech.SayAsync(w.Kana);
        await Task.Delay(500);
        if (w.ExJp.Length > 0) await Speech.SayAsync(w.ExJp);
    }

    /// <summary>Карточка слова дня поверх окна.</summary>
    public static async void Show(bool force = false)
    {
        var w = Today;
        if (w == null) return;
        await JDict.LoadAsync(); // чтение кандзи в примере для латиницы
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(U.T("СЛОВО ДНЯ · " + DateTime.Today.ToString("d MMMM", new System.Globalization.CultureInfo("ru-RU")).ToUpperInvariant(), "Overline").Center());
        sp.Children.Add(U.Jp(w.Jp, 64, null, true).Center().M(0, 10, 0, 0));
        if (w.Kana != w.Jp) sp.Children.Add(U.Jp(w.Kana, 20, U.B("Accent")).Center());
        sp.Children.Add(U.T(w.Romaji, "Muted", 14).Center());
        if (Pitch.Pattern(w.Jp, w.Kana) is { } acc) sp.Children.Add(new Controls.AccentView(acc) { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
        sp.Children.Add(U.T(CleanRu(w.Ru), "H2", 20).Center().M(0, 10, 0, 0));
        if (w.ExJp.Length > 0)
        {
            var ex = new Border
            {
                Background = U.B("Surface2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 10), Margin = new Thickness(0, 16, 0, 0),
                Child = U.Stack(U.Jp(w.ExJp, 17).Center(), U.T(Reading.RomajiWords(w.ExJp), "Muted", 12).Center(), U.T(w.ExRu, "Body", 13).Center().M(0, 4, 0, 0)),
            };
            sp.Children.Add(ex);
        }
        var buttons = U.Row(8,
            U.Btn("Послушать", () => _ = SpeakAsync(w), "", ""),
            U.Btn("Медленно", () => Speech.Say(w.Kana, true), "", ""),
            U.Btn("Учить это слово", () => { Shell.Main.CloseOverlay(); Shell.Open(new Controls.FlashSession("Слово дня", new[] { Controls.FlashSession.FromWord(w) })); }, "Primary", ""));
        buttons.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(buttons.M(0, 18, 0, 0));
        Shell.Main.ShowOverlay(sp);
        if (force || ProgressStore.P.WordOfDaySpeak) _ = SpeakAsync(w);
    }
}
