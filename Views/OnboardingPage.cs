using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>Первый запуск: приветствие → цель и время → уровень (или тест) → голос и микрофон.</summary>
public sealed class OnboardingPage : UserControl, IPage
{
    public string Title => "Добро пожаловать";
    int _step;
    string _level = "zero";

    public OnboardingPage() => Render();

    void Render()
    {
        var card = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 24) };
        for (int i = 0; i < 4; i++) dots.Children.Add(new Border { Width = i == _step ? 22 : 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(3), Background = U.B(i <= _step ? "Accent" : "Surface3") });
        card.Children.Add(dots);
        card.Children.Add(_step switch { 0 => Welcome(), 1 => Goal(), 2 => Level(), _ => Voice() });
        Content = new Grid { Children = { new ScrollViewer { Content = new Border { Padding = new Thickness(40), Child = card } } } };
        Anim.FadeUp(card, 0, 14, 380);
    }

    void Next() { _step++; Render(); }

    FrameworkElement Welcome() => U.Stack(
        U.Jp("言霊", 64, U.B("Accent"), true).Center(),
        U.T("Добро пожаловать в Kotodama", "H1").Center().M(0, 12, 0, 6),
        U.T("Котодама — «душа слова». Здесь ты научишься читать, понимать и говорить по-японски: шаг за шагом, с озвучкой каждой фразы и проверкой произношения.", "Body", 15).Center().M(0, 0, 0, 24),
        U.Btn("Начать", Next, "Primary", "").Center(),
        U.Btn("Пропустить настройку", Finish, "Ghost").Center().M(0, 8, 0, 0));

    FrameworkElement Goal()
    {
        var p = ProgressStore.P;
        var goals = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (key, text) in new[] { ("anime", "🎌 Аниме и манга"), ("travel", "✈ Путешествие"), ("work", "💼 Работа или учёба"), ("jlpt", "🎓 Сдать JLPT"), ("self", "🌸 Для себя") })
        {
            var rb = new RadioButton { Style = U.St("ChipRadio"), Content = text, GroupName = "goal", IsChecked = p.Goal == key, Margin = new Thickness(0, 0, 8, 8) };
            rb.Checked += (_, _) => p.Goal = key;
            goals.Children.Add(rb);
        }
        var time = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (min, xp) in new[] { (5, 20), (10, 35), (20, 60), (30, 100) })
        {
            var rb = new RadioButton { Style = U.St("ChipRadio"), Content = $"{min} мин в день", GroupName = "time", IsChecked = p.DailyGoal == xp, Margin = new Thickness(0, 0, 8, 8) };
            rb.Checked += (_, _) => { p.DailyGoal = xp; ProgressStore.Notify(); };
            time.Children.Add(rb);
        }
        return U.Stack(U.T("Зачем учишь японский?", "H2").Center(), goals.M(0, 14, 0, 26),
            U.T("Сколько времени в день готов уделять?", "H2").Center(), time.M(0, 14, 0, 6),
            U.T("Лучше понемногу, но каждый день: 10 минут ежедневно дают больше, чем час раз в неделю.", "Muted").Center().M(0, 0, 0, 24),
            U.Btn("Дальше", Next, "Primary", "").Center());
    }

    FrameworkElement Level()
    {
        var list = new StackPanel { MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (key, text, sub) in new[] {
            ("zero", "Совсем с нуля", "Начнём с азбуки"), ("hira", "Знаю хирагану", "Пропустим первые юниты"),
            ("kana", "Знаю хирагану и катакану", "Сразу к словам и грамматике"), ("n5", "Знаю основы (N5)", "Начнём с N4"), ("n4", "Уровень N4", "Повторение и N3") })
        {
            var rb = new RadioButton { Style = U.St("ChipRadio"), GroupName = "lvl", IsChecked = _level == key, Margin = new Thickness(0, 0, 0, 8), HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = U.Stack(new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold }, U.T(sub, "Muted", 12)) };
            rb.Checked += (_, _) => _level = key;
            list.Children.Add(rb);
        }
        return U.Stack(U.T("Что ты уже знаешь?", "H2").Center(), list.M(0, 14, 0, 14),
            U.Btn("Не уверен — пройти тест уровня (2 мин)", RunTest, "", "").Center(),
            U.Btn("Дальше", () => { Apply(_level); Next(); }, "Primary", "").Center().M(0, 12, 0, 0));
    }

    /// <summary>Тест уровня: 4 блока по 3 вопроса — хирагана, катакана, N5, N4.</summary>
    void RunTest()
    {
        var score = new int[4];
        var qs = new List<QuizQ>();
        var basic = Kana.All.Where(c => c.Group == Kana.Basic).Shuffle().Take(6).ToList();
        var romajiPool = Kana.All.Select(c => c.Romaji).ToArray();
        for (int i = 0; i < 6; i++)
        {
            int sec = i < 3 ? 0 : 1; var c = basic[i];
            qs.Add(new QuizQ { Prompt = c.Get(sec == 1), PromptSize = 96, Sub = "Как читается?", Answer = c.Romaji, Options = QuizSession.MakeOptions(c.Romaji, romajiPool), OnResult = ok => { if (ok) score[sec]++; } });
        }
        foreach (var (lvl, sec) in new[] { ("N5", 2), ("N4", 3) })
        {
            var words = Db.WordsOf(lvl).Shuffle().Take(2).ToList();
            var ru = Db.WordsOf(lvl).Select(w => w.Ru).ToArray();
            foreach (var w in words) qs.Add(new QuizQ { Prompt = w.Jp, PromptSize = 56, Sub = "Что значит?", Answer = w.Ru, Options = QuizSession.MakeOptions(w.Ru, ru), OnResult = ok => { if (ok) score[sec]++; } });
            var g = Db.Grammar.Where(x => x.Level == lvl).SelectMany(x => x.Quiz).Shuffle().First();
            qs.Add(new QuizQ { Prompt = g.Question, PromptSize = 30, Sub = g.Ru, Answer = g.Answer, Options = g.Options.Shuffle().ToArray(), OptionsJp = true, OnResult = ok => { if (ok) score[sec]++; } });
        }
        Shell.Open(new QuizSession("Тест уровня", qs, (_, _) =>
        {
            _level = score[3] >= 2 && score[2] >= 2 ? "n4" : score[2] >= 2 && score[1] >= 2 ? "n5" : score[1] >= 2 && score[0] >= 2 ? "kana" : score[0] >= 2 ? "hira" : "zero";
            var name = _level switch { "n4" => "уровень N4", "n5" => "основы N5", "kana" => "хирагана и катакана", "hira" => "хирагана", _ => "с нуля" };
            Shell.Toast($"Результат теста: {name}", "結果");
            Render();
        }));
    }

    /// <summary>Отмечает пройденное по выбранному уровню: юниты пути и знание каны.</summary>
    static void Apply(string level)
    {
        var p = ProgressStore.P;
        void Kana_(bool kata) { foreach (var c in Kana.All.Where(c => c.Group == Kana.Basic)) p.KanaScore[(kata ? "k:" : "h:") + c.Get(kata)] = 3; }
        void Skip(Func<CourseUnit, bool> which) { foreach (var u in Db.Course.Where(which)) { p.Flags.Add("skip:" + u.Id); p.Flags.Add("theory:" + u.Id); } }
        switch (level)
        {
            case "hira": Kana_(false); Skip(u => u.Id <= 3); break;
            case "kana": Kana_(false); Kana_(true); Skip(u => u.Id <= 6); break;
            case "n5": Kana_(false); Kana_(true); Skip(u => u.Level == "N5"); p.Level = "N4"; break;
            case "n4": Kana_(false); Kana_(true); Skip(u => u.Level is "N5" or "N4"); p.Level = "N3"; break;
        }
        ProgressStore.Notify();
    }

    FrameworkElement Voice()
    {
        var sp = U.Stack(U.T("Голос и микрофон", "H2").Center(),
            U.T("Каждая фраза в приложении озвучена. Проверь звук:", "Muted").Center().M(0, 6, 0, 10),
            U.Btn("Послушать: こんにちは", () => Speech.Say("こんにちは、一緒に日本語を勉強しましょう。"), "", "").Center());
        sp.Children.Add(SpeechUi.ModelCard().M(0, 20, 0, 0));
        sp.Children.Add(U.T("Офлайн-голоса VOICEVOX (работают без интернета) можно поставить позже: Настройки → «Офлайн-голоса».", "Muted").Center().M(0, 12, 0, 0));
        sp.Children.Add(U.Row(10, U.Btn("Проверить микрофон", () => Shell.Open(new MicCheckPage()), "", ""), U.Btn("Готово — к обучению!", Finish, "Primary", "")).Center().M(0, 22, 0, 0));
        return sp;
    }

    static void Finish()
    {
        ProgressStore.P.Flags.Add("onboarded");
        ProgressStore.Notify();
        Shell.Go("path");
    }
}
