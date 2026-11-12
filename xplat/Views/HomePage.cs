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

public sealed class HomePage : UserControl, IPage
{
    public string Title => "Главная";

    public void OnShown() => Content = U.Page(Build());

    Control Build()
    {
        var p = ProgressStore.P;
        var root = new StackPanel();

        // ─────────── Приветствие ───────────
        var (jpGreet, ruGreet) = DateTime.Now.Hour switch
        {
            < 5 => ("こんばんは", "Доброй ночи"),
            < 11 => ("おはようございます", "Доброе утро"),
            < 18 => ("こんにちは", "Добрый день"),
            _ => ("こんばんは", "Добрый вечер"),
        };
        // ─────────── Баннер: Википе-тан + продолжить путь ───────────
        var u = Course.Current;
        int d = Course.DoneCount(u), t = Course.Total(u);
        int due = p.Cards.Count(kv => kv.Value.Due <= DateTime.Now);
        var (say, mood) = due >= 10 ? ($"На повторение {due} {MainView.Plural(due, "карточка", "карточки", "карточек")} — пара минут, и память снова свежая!", Mood.Think)
            : p.Streak >= 3 ? ($"Уже {p.Streak} {MainView.Plural(p.Streak, "день", "дня", "дней")} подряд! すごい！ Продолжаем?", Mood.Joy)
            : d == 0 ? ("Новый юнит! Сначала коротко объясню, потом потренируемся.", Mood.Happy)
            : ("Продолжим с того места, где остановились?", Mood.Wink);

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(U.Jp(jpGreet, 15, U.B("Accent")));
        left.Children.Add(U.T(ruGreet, "H1", 30).M(0, 2, 0, 10));
        left.Children.Add(new Border
        {
            Background = U.B("Surface"), CornerRadius = new CornerRadius(14, 14, 14, 4), Padding = new Thickness(14, 9), HorizontalAlignment = HorizontalAlignment.Left,
            BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(1), Child = U.T(say, "Body", 13.5).Also(x => x.TextWrapping = TextWrapping.Wrap),
        });
        // Главное действие 2.0 — «Учиться»: сессия собирается сама, видно из чего она состоит
        bool studied = p.Flags.Contains("study:" + DateTime.Today.ToString("yyyy-MM-dd"));
        bool resume = StudyPage.Active is { } act && !studied && act.IsResumable;
        var plan = StudyPage.Plan(Math.Clamp(p.StudyMode, 0, 2));
        left.Children.Add(U.T(studied ? "СЕССИЯ НА СЕГОДНЯ ВЫПОЛНЕНА ✓" : resume ? "СЕССИЯ НА ПАУЗЕ" : "СЕССИЯ НА СЕГОДНЯ", "Overline").M(0, 20, 0, 0));
        left.Children.Add(U.T(studied ? "Отлично! Можно закрепить ещё раз или выбрать раздел" : $"{plan.Count} {MainView.Plural(plan.Count, "шаг", "шага", "шагов")} · около {plan.Sum(x => x.Minutes)} минут", "H2", 19).M(0, 4, 0, 10));
        var chips = new WrapPanel();
        foreach (var st in plan)
            chips.Children.Add(new Border
            {
                Background = U.B("Surface"), BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(6, 4, 12, 4), Margin = new Thickness(0, 0, 6, 6),
                Child = U.Row(7, Art.Icon(st.Icon, 22), U.T(st.Title, "Body", 12.5).CenterV()),
            }.Tip(st.Why));
        left.Children.Add(chips);
        var acts = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var go = U.Btn(studied ? "Ещё сессия" : resume ? "Продолжить сессию" : "Учиться", StudyPage.Start, "Primary", "");
        go.MinWidth = 190; go.Height = 44; go.FontSize = 15;
        acts.Children.Add(go.M(0, 0, 10, 6));
        acts.Children.Add(U.Btn($"Путь: юнит {u.Id}", () => { Shell.Go("path"); Shell.Open(new UnitPage(u)); }, "", "").M(0, 0, 10, 6).Tip($"{u.Title} · {d}/{t}"));
        var rb = U.Btn(due > 0 ? $"Повторить ({due})" : "Всё повторено ✓", () => Shell.Go("review"), "", "");
        rb.IsEnabled = due > 0;
        acts.Children.Add(rb.M(0, 0, 10, 6));
        left.Children.Add(acts);

        var hero = U.Cols("*", "auto");
        hero.Children.Add(left.M(28, 22, 12, 22));
        // крупно и по пояс: баннер обрезает низ картинки
        var kotoha = Art.Mascot(mood == Mood.Joy ? 330 : 480, mood);
        kotoha.VerticalAlignment = VerticalAlignment.Top;
        kotoha.Margin = new Thickness(0, 14, 30, mood == Mood.Joy ? 0 : -400);
        hero.Children.Add(kotoha.DesktopOnly().Col(1));
        var heroCard = new Border
        {
            CornerRadius = new CornerRadius(22), ClipToBounds = true, BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(((ISolidColorBrush)U.B("Surface")).Color, 0), new GradientStop(((ISolidColorBrush)U.B("AccentSoft")).Color, 1) },
            },
            Child = hero,
        };
        var row = heroCard;
        root.Children.Add(heroCard);

        // ─────────── Статистика ───────────
        int wordsKnown = Db.Words.Count(w => ProgressStore.Card(w.Id)?.Reps > 0);
        int kanjiKnown = Db.Kanji.Count(k => ProgressStore.Card(k.Id)?.Reps > 0);
        var stats = new UniformGrid { Columns = U.Columns(4, 2), Margin = new Thickness(-6, 16, -6, 0) };
        stats.Children.Add(Stat("Серия", p.Streak, MainView.Plural(p.Streak, "день", "дня", "дней"), "Gold"));
        stats.Children.Add(Stat("Сегодня", ProgressStore.TodayXp, $"из {p.DailyGoal} XP", "Accent"));
        stats.Children.Add(Stat("Слова", wordsKnown, "в изучении", "Info"));
        stats.Children.Add(Stat("Кандзи", kanjiKnown, $"из {Db.Kanji.Length}", "Good"));
        foreach (var c in stats.Children.OfType<Border>()) { c.Cursor = Cursors.Hand; Hover(c); c.PointerReleased += (_, _) => Shell.Go("stats"); }
        root.Children.Add(stats);
        root.Children.Add(ChallengesCard.Build().M(0, 16, 0, 0));
        // Факт о Японии — каждый день новый, ведёт в раздел «Япония»
        var facts = JapanPage.All.SelectMany(a => a.Facts.Select(f => (a, f))).ToArray();
        if (facts.Length > 0)
        {
            var (fa, ff) = facts[(DateTime.Today.DayOfYear * 7) % facts.Length];
            var jf = U.Cols("auto", "*", "auto");
            jf.Children.Add(Art.Icon("torii", 44).CenterV());
            jf.Children.Add(U.Stack(U.T("ЯПОНИЯ · ФАКТ ДНЯ", "Overline"), U.T(ff, "Body", 14.5).Also(t => t.TextWrapping = TextWrapping.Wrap).M(0, 4, 0, 0)).CenterV().M(16, 0, 12, 0).Col(1));
            jf.Children.Add(U.Btn(fa.Title, () => Shell.Open(new ArticlePage(fa)), "", "").CenterV().Col(2));
            root.Children.Add(U.Card(jf, new Thickness(20, 14, 20, 14)).M(0, 16, 0, 0));
        }
        var hint = U.T("Совет: Ctrl+K — быстрый поиск по всему приложению", "Muted", 12).DesktopOnly();
        hint.Margin = new Thickness(2, 10, 0, 0);
        root.Children.Add(hint);

        // ─────────── Разделы ───────────
        root.Children.Add(U.T("Разделы", "H2").M(0, 28, 0, 12));
        int kanaKnown = Kana.All.Count(k => ProgressStore.KanaScore("h:" + k.Hira) >= 3) + Kana.All.Count(k => ProgressStore.KanaScore("k:" + k.Kata) >= 3);
        var sections = new UniformGrid { Columns = U.Columns(4, 2), Margin = new Thickness(-6, 0, -6, 0) };
        sections.Children.Add(Section("kana", "Кана", $"{kanaKnown * 100 / (Kana.All.Count * 2)}% освоено", "kana"));
        sections.Children.Add(Section("words", "Слова", $"{Db.Words.Length} для изучения · N5–N3", "words"));
        sections.Children.Add(Section("kanji", "Кандзи", $"{Db.Kanji.Length} знаков", "kanji"));
        sections.Children.Add(Section("grammar", "Грамматика", $"{p.GrammarDone.Count}/{Db.Grammar.Length} уроков", "grammar"));
        sections.Children.Add(Section("speech", "Говорение", $"{Db.SpeechLessons.Length} уроков", "speech"));
        sections.Children.Add(Section("dialogs", "Диалоги", $"{Db.Dialogs.Length} сценок", "dialogs"));
        sections.Children.Add(Section("dict", "Словарь", $"справочник · {JDict.CountLabel} статей", "dict"));
        sections.Children.Add(Section("books", "Книги", "книги и рассказы", "reader"));
        sections.Children.Add(Section("torii", "Япония", $"{JapanPage.All.Length} статей о культуре", "japan"));
        sections.Children.Add(Section("trainers", "Игры", "6 игр на слова и кану", "trainers"));
        sections.Children.Add(Section("kanji", "Ключи кандзи", "из чего состоят знаки", "radicals"));
        root.Children.Add(sections);
        Anim.Stagger(sections, 30, 120);

        // ─────────── Активность + слово дня ───────────
        var bottom = U.Cols("*", "16", "340");
        bottom.Margin = new Thickness(0, 28, 0, 0);
        bottom.Children.Add(Activity());
        bottom.Children.Add(WordOfDay().Col(2));
        root.Children.Add(U.Stackify(bottom));

        Anim.FadeUp(row, 40, 12, 380);
        Anim.FadeUp(stats, 90, 12, 380);
        return root;
    }

    static Border Stat(string label, int value, string unit, string color)
    {
        var num = U.T("0", "H1", 24, U.B(color));
        Anim.CountUp(num, value);
        var sp = U.Stack(U.T(label, "Muted", 12), U.Row(6, num, U.T(unit, "Muted", 12).CenterV().M(0, 6, 0, 0)).M(0, 2, 0, 0));
        return new Border { Classes = { "Card" }, Child = sp, Margin = new Thickness(6), Padding = new Thickness(16, 12, 16, 12) };
    }

    static Border Section(string icon, string title, string sub, string key)
    {
        var g = U.Cols("auto", "*");
        g.Children.Add(Art.Icon(icon, 40).CenterV());
        g.Children.Add(U.Stack(U.T(title, "H3", 14), U.T(sub, "Muted", 12)).CenterV().M(12, 0, 0, 0).Col(1));
        var card = new Border { Classes = { "Card" }, Child = g, Margin = new Thickness(6), Padding = new Thickness(16, 12, 16, 12), Cursor = Cursors.Hand };
        Hover(card);
        card.PointerReleased += (_, _) => { if (key == "radicals") Shell.Open(new RadicalsPage()); else Shell.Go(key); };
        return card;
    }

    /// <summary>Лёгкий подъём карточки и подсветка рамки при наведении (только трансформация, без перерасчёта layout).</summary>
    public static void Hover(Border card)
    {
        var t = new TranslateTransform();
        card.RenderTransform = t;
        var normal = card.BorderBrush;
        card.PointerEntered += (_, _) => { Anim.Run((t, "y"), t.Y, -3, 180, v => t.Y = v); card.BorderBrush = U.B("StrokeStrong"); };
        card.PointerExited += (_, _) => { Anim.Run((t, "y"), t.Y, 0, 260, v => t.Y = v); card.BorderBrush = normal; };
    }

    static Border Activity()
    {
        var sp = new StackPanel();
        var h = U.Cols("*", "auto");
        h.Children.Add(U.T("Активность", "H3"));
        h.Children.Add(U.Btn("Весь прогресс", () => Shell.Go("stats"), "Ghost", "").Col(1));
        sp.Children.Add(h);
        sp.Children.Add(U.T("Последние 18 недель", "Muted", 12).M(0, 2, 0, 14));
        const int weeks = 18;
        var grid = new UniformGrid { Rows = 7, Columns = weeks, HorizontalAlignment = HorizontalAlignment.Left };
        var start = DateTime.Today.AddDays(-(weeks * 7 - 1) - ((int)DateTime.Today.DayOfWeek + 6) % 7 + 6);
        var cells = new Border[7, weeks];
        for (int w = 0; w < weeks; w++)
            for (int d = 0; d < 7; d++)
            {
                var day = start.AddDays(w * 7 + d);
                int xp = ProgressStore.P.History.TryGetValue(day.ToString("yyyy-MM-dd"), out var x) ? x : 0;
                byte a = xp == 0 ? (byte)0 : xp < 20 ? (byte)70 : xp < 50 ? (byte)140 : (byte)230;
                cells[d, w] = new Border
                {
                    Width = 15, Height = 15, Margin = new Thickness(2), CornerRadius = new CornerRadius(4),
                    Background = day > DateTime.Today ? Brushes.Transparent : xp == 0 ? U.B("Surface2") : new SolidColorBrush(Color.FromArgb(a, 0xF0, 0x7A, 0xA0)),
                    
                }.Tip(day > DateTime.Today ? null : $"{day:d MMM}: {xp} XP");
            }
        for (int d = 0; d < 7; d++) for (int w = 0; w < weeks; w++) grid.Children.Add(cells[d, w]);
        sp.Children.Add(grid);
        return new Border { Classes = { "Card" }, Child = sp };
    }

    static Border WordOfDay()
    {
        // то же слово, что приходит в уведомлении «Слово дня»
        var w = Services.WordOfDay.Today ?? Db.Words[DateTime.Today.DayOfYear % Db.Words.Length];
        var sp = new StackPanel();
        var head = U.Cols("*", "auto");
        head.Children.Add(U.T("Слово дня", "H3"));
        head.Children.Add(U.SpeakBtn(() => w.Kana).Col(1));
        sp.Children.Add(head);
        sp.Children.Add(U.Jp(w.Jp, 40, null, true).M(0, 8, 0, 0));
        sp.Children.Add(U.Jp(w.Kana, 14, U.B("Accent")).M(0, 4, 0, 0));
        sp.Children.Add(U.T(w.Romaji, "Muted", 13));
        sp.Children.Add(U.T(w.Ru, "H3", 16).M(0, 10, 0, 0));
        sp.Children.Add(U.T("Нажми — пример фразы", "Muted", 11.5).M(0, 8, 0, 0));
        var card = new Border { Classes = { "Card" }, Child = sp, Cursor = Cursors.Hand };
        card.PointerReleased += (_, e) => { if (e.Source is not Button && e.Source is not TextBlock { Parent: Button }) Services.WordOfDay.Show(force: true); };
        return card;
    }
}
