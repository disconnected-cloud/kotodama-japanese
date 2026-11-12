using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>Карточка «Испытания дня» на главной: три задания с прогрессом, недельная цель кольцом и щиты серии.</summary>
public static class ChallengesCard
{
    public static Border Build()
    {
        var p = ProgressStore.P;
        var list = Challenges.Today();
        int done = list.Count(c => c.Complete);

        var left = new StackPanel();
        var head = U.Cols("*", "auto");
        head.Children.Add(U.Stack(U.T("ИСПЫТАНИЯ ДНЯ", "Overline"),
            U.T(done == list.Count ? "Все выполнены! 完璧" : $"Выполнено {done} из {list.Count} · +10 XP за каждое", "H3", 16).M(0, 4, 0, 0)));
        if (done == list.Count) head.Children.Add(Art.Anime("gift", 92).Also(g => g.Margin = new Thickness(0, -14, 0, -10)).DesktopOnly().Col(1));
        left.Children.Add(head);
        foreach (var c in list)
        {
            var row = U.Cols("auto", "*", "auto");
            row.Margin = new Thickness(0, 12, 0, 0);
            row.Children.Add(Art.Icon(c.Icon, 34).CenterV().Also(i => i.Opacity = c.Complete ? 0.55 : 1));
            var bar = new ProgressBar { Maximum = c.Target, Value = c.Done, Height = 6, Margin = new Thickness(0, 6, 0, 0), Foreground = U.B(c.Complete ? "Good" : "Accent") };
            row.Children.Add(U.Stack(U.T(c.Title, "Body", 13.5, c.Complete ? U.B("Muted") : null), bar).CenterV().M(12, 0, 12, 0).Col(1));
            row.Children.Add((c.Complete
                ? U.Pill("✓", U.B("GoodSoft"), U.B("Good"))
                : (Control)U.T($"{c.Done}/{c.Target}", "Muted", 12)).CenterV().Col(2));
            left.Children.Add(row);
        }

        // Неделя: кольцо опыта + щиты
        int wx = Challenges.WeekXp, wt = Challenges.WeekTarget;
        double frac = Math.Min(1, wx / (double)wt);
        var ring = new Grid { Width = 112, Height = 112, HorizontalAlignment = HorizontalAlignment.Center };
        ring.Children.Add(new Ellipse { Stroke = U.B("Surface2"), StrokeThickness = 9 });
        if (frac > 0)
        {
            var arc = Results.Ring(56, 51.5, Math.Max(0.02, frac));
            arc.Stroke = U.B(frac >= 1 ? "Good" : "GoldGradient"); arc.StrokeThickness = 9; arc.StrokeLineCap = PenLineCap.Round;
            ring.Children.Add(arc);
        }
        ring.Children.Add(U.Stack(U.T($"{wx}", "H2", 20).Center(), U.T($"из {wt} XP", "Muted", 11).Center()).CenterV());
        var shields = U.Row(4);
        shields.HorizontalAlignment = HorizontalAlignment.Center;
        for (int i = 0; i < Challenges.MaxShields; i++)
            shields.Children.Add(Art.Icon("shield", 26).Also(s => s.Opacity = i < p.Shields ? 1 : 0.22));
        var right = U.Stack(U.T("НЕДЕЛЬНАЯ ЦЕЛЬ", "Overline").Center(), ring.M(0, 10, 0, 10), shields,
            U.T(p.Shields > 0 ? $"Щитов: {p.Shields} — спасут серию в пропущенный день" : "Возьми недельную цель — получишь щит серии", "Muted", 11.5)
                .Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextAlignment = TextAlignment.Center; t.MaxWidth = 200; }).Center().M(0, 6, 0, 0));
        right.Tip($"Награда за неделю: щит серии (до {Challenges.MaxShields}) и значок. Значков недель: {p.Badges.Count}");

        var g = U.Cols("*", "28", "auto");
        g.Children.Add(left);
        g.Children.Add(right.CenterV().Col(2));
        return U.Card(U.Stackify(g), Shell.Compact ? new Thickness(16) : new Thickness(22));
    }
}
